#nowarn "3261" // nullable reference type warnings
module internal Nullean.Make.Fs.FsDtoBinder

open System
open Microsoft.FSharp.Reflection

let private isOption (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

let private noneValue (optType: Type) =
    let noneCase = FSharpType.GetUnionCases(optType) |> Array.find (fun c -> c.Name = "None")
    FSharpValue.MakeUnion(noneCase, [||])

let private someValue (optType: Type) (inner: obj) =
    let someCase = FSharpType.GetUnionCases(optType) |> Array.find (fun c -> c.Name = "Some")
    FSharpValue.MakeUnion(someCase, [| inner |])

let private toKebabCase (name: string) =
    let sb = Text.StringBuilder()
    for i in 0 .. name.Length - 1 do
        let c = name.[i]
        if Char.IsUpper(c) && i > 0 then sb.Append('-') |> ignore
        sb.Append(Char.ToLowerInvariant(c)) |> ignore
    sb.ToString()

let parseScalar (t: Type) (raw: string) (paramName: string) : obj =
    let innerType = if isOption t then t.GetGenericArguments().[0] else t
    let parsed =
        try
            if   innerType = typeof<string>          then raw :> obj
            elif innerType = typeof<int>              then int raw :> obj
            elif innerType = typeof<int64>            then int64 raw :> obj
            elif innerType = typeof<float>            then float raw :> obj
            elif innerType = typeof<bool>             then Boolean.Parse(raw) :> obj
            elif innerType = typeof<IO.FileInfo>      then IO.FileInfo(raw) :> obj
            elif innerType = typeof<IO.DirectoryInfo> then IO.DirectoryInfo(raw) :> obj
            elif innerType = typeof<Uri>              then Uri(raw) :> obj
            elif innerType = typeof<TimeSpan>         then TimeSpan.Parse(raw) :> obj
            elif innerType.IsEnum                     then Enum.Parse(innerType, raw, true)
            else failwith $"Unsupported type '{innerType.Name}' for '--%s{paramName}'"
        with
        | :? Nullean.Make.MakeException -> reraise ()
        | ex ->
            raise (Nullean.Make.MakeException(
                $"Cannot parse '{raw}' as {innerType.Name} for '--%s{paramName}': {ex.Message}", 2))
    if isOption t then someValue t parsed else parsed

let private isList (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<list<_>>

let private makeList (listType: Type) (items: obj list) : obj =
    let cases = FSharpType.GetUnionCases(listType)
    let empty = cases |> Array.find (fun c -> c.Name = "Empty")
    let cons  = cases |> Array.find (fun c -> c.Name = "Cons")
    List.foldBack (fun item acc -> FSharpValue.MakeUnion(cons, [| item; acc |])) items (FSharpValue.MakeUnion(empty, [||]))

/// Bind args into a value of the given type.
/// Handles F# records (with option<T> fields) and falls back to the C# DtoBinder for anything else.
/// Everything after a `--` token goes to the field marked [<Rest>]; a `'T list` field marked [<Argument>] takes every
/// positional token that is left.
let bind (dtoType: Type) (args: string[]) : obj =
    if not (FSharpType.IsRecord(dtoType)) then
        Nullean.Make.Parsing.DtoBinder.Bind(dtoType, args)
    else

    let fields = FSharpType.GetRecordFields(dtoType)

    // On an F# record, an attribute that is only valid on parameters ([<Argument>]) lands on the constructor
    // parameter, not on the property, so look at both.
    let ctorParams =
        dtoType.GetConstructors() |> Array.collect (fun c -> c.GetParameters())
    let hasAttribute (attr: Type) (f: Reflection.PropertyInfo) =
        f.GetCustomAttributes(attr, false).Length > 0
        || ctorParams |> Array.exists (fun p ->
            String.Equals(p.Name, f.Name, StringComparison.OrdinalIgnoreCase)
            && p.GetCustomAttributes(attr, false).Length > 0)

    let isRest (f: Reflection.PropertyInfo) = hasAttribute typeof<RestAttribute> f
    let isPositional (f: Reflection.PropertyInfo) = not (isRest f) && hasAttribute typeof<Nullean.Argh.ArgumentAttribute> f

    let positionals = fields |> Array.filter isPositional
    let restField   = fields |> Array.tryFind isRest

    let flagMap =
        fields
        |> Array.filter (fun f -> not (isPositional f) && not (isRest f))
        |> Array.map (fun f -> toKebabCase f.Name, f)
        |> dict

    let values =
        fields |> Array.map (fun f ->
            let t = f.PropertyType
            if   isOption t   then noneValue t
            elif isList t     then makeList t []
            elif t = typeof<bool> then false :> obj
            elif t.IsValueType    then Activator.CreateInstance(t)
            else null)

    let fieldIdx (f: Reflection.PropertyInfo) =
        fields |> Array.findIndex (fun ff -> ff.Name = f.Name)

    let parseList (f: Reflection.PropertyInfo) (tokens: string list) =
        let elem = f.PropertyType.GetGenericArguments().[0]
        makeList f.PropertyType (tokens |> List.map (fun tok -> parseScalar elem tok (toKebabCase f.Name)))

    let mutable argIdx = 0
    let mutable posIdx = 0
    let variadic = ResizeArray<string>()
    let rest = ResizeArray<string>()
    let mutable afterSeparator = false

    while argIdx < args.Length do
        let arg = args.[argIdx]
        if afterSeparator then
            rest.Add arg
            argIdx <- argIdx + 1
        elif arg = "--" then
            afterSeparator <- true
            argIdx <- argIdx + 1
        elif arg.StartsWith("--") || (arg.StartsWith("-") && arg.Length = 2) then
            // --name=value carries its value inline.
            let eq = if arg.StartsWith("--") then arg.IndexOf('=') else -1
            let nameToken, inlineValue =
                if eq > 0 then arg.Substring(0, eq), Some (arg.Substring(eq + 1)) else arg, None
            let flagName = nameToken.TrimStart('-')
            let negated  = flagName.StartsWith("no-") && inlineValue.IsNone
            let lookup   = if negated then flagName.Substring(3) else flagName

            match flagMap.TryGetValue(lookup) with
            | false, _ ->
                raise (Nullean.Make.MakeException($"Unknown flag '{arg}'.", 2))
            | true, field ->
                let idx = fieldIdx field
                let ft  = field.PropertyType
                let isBool = ft = typeof<bool> || (isOption ft && ft.GetGenericArguments().[0] = typeof<bool>)
                let flag = toKebabCase field.Name
                match inlineValue with
                | Some v ->
                    values.[idx] <- parseScalar ft v flag
                    argIdx <- argIdx + 1
                | None when isBool ->
                    values.[idx] <- (not negated) :> obj
                    argIdx <- argIdx + 1
                | None ->
                    argIdx <- argIdx + 1
                    if argIdx >= args.Length then
                        raise (Nullean.Make.MakeException($"Flag '--{lookup}' requires a value.", 2))
                    values.[idx] <- parseScalar ft args.[argIdx] flag
                    argIdx <- argIdx + 1
        else
            if posIdx < positionals.Length && not (isList positionals.[posIdx].PropertyType) then
                let field = positionals.[posIdx]
                values.[fieldIdx field] <- parseScalar field.PropertyType arg (toKebabCase field.Name)
                posIdx <- posIdx + 1
            elif posIdx < positionals.Length then
                variadic.Add arg
            else
                raise (Nullean.Make.MakeException($"Unexpected argument '{arg}'.", 2))
            argIdx <- argIdx + 1

    if variadic.Count > 0 then
        let field = positionals.[posIdx]
        values.[fieldIdx field] <- parseList field (List.ofSeq variadic)

    if rest.Count > 0 then
        match restField with
        | Some field -> values.[fieldIdx field] <- parseList field (List.ofSeq rest)
        | None -> raise (Nullean.Make.MakeException($"Unexpected argument '{rest.[0]}' after '--'.", 2))

    FSharpValue.MakeRecord(dtoType, values)
