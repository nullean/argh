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

let private parseScalar (t: Type) (raw: string) (paramName: string) : obj =
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

/// Bind args into a value of the given type.
/// Handles F# records (with option<T> fields) and falls back to the C# DtoBinder for anything else.
let bind (dtoType: Type) (args: string[]) : obj =
    if not (FSharpType.IsRecord(dtoType)) then
        Nullean.Make.Parsing.DtoBinder.Bind(dtoType, args)
    else

    let fields = FSharpType.GetRecordFields(dtoType)

    let isPositional (f: Reflection.PropertyInfo) =
        f.GetCustomAttributes(typeof<Nullean.Argh.ArgumentAttribute>, false).Length > 0

    let positionals = fields |> Array.filter isPositional

    let flagMap =
        fields
        |> Array.filter (isPositional >> not)
        |> Array.map (fun f -> toKebabCase f.Name, f)
        |> dict

    let values =
        fields |> Array.map (fun f ->
            let t = f.PropertyType
            if   isOption t   then noneValue t
            elif t = typeof<bool> then false :> obj
            elif t.IsValueType    then Activator.CreateInstance(t)
            else null)

    let fieldIdx (f: Reflection.PropertyInfo) =
        fields |> Array.findIndex (fun ff -> ff.Name = f.Name)

    let mutable argIdx = 0
    let mutable posIdx = 0

    while argIdx < args.Length do
        let arg = args.[argIdx]
        if arg.StartsWith("--") || (arg.StartsWith("-") && arg.Length = 2) then
            let flagName = arg.TrimStart('-')
            let negated  = flagName.StartsWith("no-")
            let lookup   = if negated then flagName.Substring(3) else flagName

            match flagMap.TryGetValue(lookup) with
            | false, _ ->
                raise (Nullean.Make.MakeException($"Unknown flag '{arg}'.", 2))
            | true, field ->
                let idx = fieldIdx field
                let ft  = field.PropertyType
                let isBool = ft = typeof<bool> || (isOption ft && ft.GetGenericArguments().[0] = typeof<bool>)
                if isBool then
                    values.[idx] <- (not negated) :> obj
                    argIdx <- argIdx + 1
                else
                    argIdx <- argIdx + 1
                    if argIdx >= args.Length then
                        raise (Nullean.Make.MakeException($"Flag '--{lookup}' requires a value.", 2))
                    values.[idx] <- parseScalar ft args.[argIdx] field.Name
                    argIdx <- argIdx + 1
        else
            if posIdx < positionals.Length then
                let field = positionals.[posIdx]
                values.[fieldIdx field] <- parseScalar field.PropertyType arg field.Name
                posIdx <- posIdx + 1
            argIdx <- argIdx + 1

    FSharpValue.MakeRecord(dtoType, values)
