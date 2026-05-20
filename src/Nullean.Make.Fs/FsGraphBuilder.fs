#nowarn "3261" // nullable reference type warnings — F# DU values are never null
module internal Nullean.Make.Fs.FsGraphBuilder

open System
open Microsoft.FSharp.Reflection
open Nullean.Make
open Nullean.Make.Discovery

let private toKebabCase = BuildScanner.ToKebabCase

let private isOption (t: Type) =
    t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

let private noneValue (optType: Type) =
    let noneCase = FSharpType.GetUnionCases(optType) |> Array.find (fun c -> c.Name = "None")
    FSharpValue.MakeUnion(noneCase, [||])

let private someValue (optType: Type) (inner: obj) =
    let someCase = FSharpType.GetUnionCases(optType) |> Array.find (fun c -> c.Name = "Some")
    FSharpValue.MakeUnion(someCase, [| inner |])

let rec private makeDefault (t: Type) : obj =
    if   isOption t    then noneValue t
    elif t.IsValueType then Activator.CreateInstance(t)
    elif FSharpType.IsRecord(t) then
        let fields = FSharpType.GetRecordFields(t)
        FSharpValue.MakeRecord(t, fields |> Array.map (fun f -> makeDefault f.PropertyType))
    else null

let private defaultCaseValue (caseInfo: UnionCaseInfo) : obj =
    let defaults = caseInfo.GetFields() |> Array.map (fun f -> makeDefault f.PropertyType)
    FSharpValue.MakeUnion(caseInfo, defaults)

// Shared stateless context instance — FsContext has no mutable state.
let private sharedCtx = FsContext()

let buildGraph<'TCase when 'TCase : comparison and 'TCase : not null>
    (appName: string)
    (bind: 'TCase -> Definition<'TCase>)
    (optionDecls: (string * string option * string option * bool) list)
    : BuildGraph =

    let graph = BuildGraph()
    graph.AppName <- appName

    // Register global options for help rendering (Property is null for F# option refs).
    for (long, short, desc, isFlag) in optionDecls do
        graph.GlobalOptions.Add(
            GlobalOptionNode(
                Long        = long,
                Short       = (match short with Some s -> s | None -> null),
                Description = (match desc  with Some d -> d | None -> null),
                IsFlag      = isFlag))

    let cases = FSharpType.GetUnionCases(typeof<'TCase>)

    // First pass: collect namespace markers (DU case name → CLI segment).
    let namespaces =
        cases
        |> Array.choose (fun ci ->
            let v = defaultCaseValue ci :?> 'TCase
            match bind v with
            | FsNamespace(segment, _) -> Some (ci.Name, segment)
            | _ -> None)

    // Derive CLI route for a DU case name using namespace prefix matching.
    let deriveRoute (caseName: string) =
        namespaces
        |> Array.tryPick (fun (nsName, segment) ->
            if caseName.StartsWith(nsName, StringComparison.Ordinal) && caseName.Length > nsName.Length then
                Some [| segment; toKebabCase (caseName.Substring(nsName.Length)) |]
            else None)
        |> Option.defaultValue [| toKebabCase caseName |]

    // Map default DU value → TargetNode for dep resolution.
    let caseToNode = Collections.Generic.Dictionary<'TCase, TargetNode>()

    // Second pass: create TargetNode for each non-namespace case.
    for caseInfo in cases do
        let defaultVal : 'TCase = defaultCaseValue caseInfo :?> 'TCase
        let def = bind defaultVal

        match def with
        | FsNamespace _ -> ()
        | FsTarget(desc, _, _) | FsCommand(desc, _, _, _) ->

            let route  = deriveRoute caseInfo.Name
            let kind   = match def with | FsCommand _ -> TargetKind.Command | _ -> TargetKind.Target
            let fields = caseInfo.GetFields()

            // Body for DU cases without a payload: captured once at graph-build time.
            let plainBody () =
                match def with
                | FsTarget(_, _, b)          -> b sharedCtx
                | FsCommand(_, _, _, Some b) -> b sharedCtx
                | _ -> ()

            // Body for payload cases: re-binds CLI args and re-invokes bind at execution time.
            let payloadBody () =
                let payloadType = fields.[0].PropertyType
                let targetArgs  =
                    let ctx = MakeContext.Current
                    if obj.ReferenceEquals(ctx, null) then [||] else ctx.TargetArgs
                let payload   = FsDtoBinder.bind payloadType targetArgs
                let boundCase : 'TCase = FSharpValue.MakeUnion(caseInfo, [| payload |]) :?> 'TCase
                match bind boundCase with
                | FsTarget(_, _, b)          -> b sharedCtx
                | FsCommand(_, _, _, Some b) -> b sharedCtx
                | _ -> ()

            let syncBody = Action(if fields.Length = 0 then plainBody else payloadBody)

            let node =
                TargetNode(
                    Route           = route,
                    ConfigureMethod = null,
                    Kind            = kind,
                    Description     = desc,
                    SyncBody        = syncBody)

            graph.Targets.Add(node)
            graph.ByRoute.[String.concat "/" route] <- node
            caseToNode.[defaultVal] <- node

    // Third pass: resolve deps directly from caseToNode (bypasses C# ByMethod).
    for caseInfo in cases do
        let defaultVal : 'TCase = defaultCaseValue caseInfo :?> 'TCase
        match bind defaultVal with
        | FsNamespace _ -> ()
        | FsTarget(_, deps, _) ->
            match caseToNode.TryGetValue(defaultVal) with
            | true, node ->
                for dep in deps do
                    match caseToNode.TryGetValue(dep) with
                    | true, depNode -> node.RequiresResolved.Add(depNode)
                    | _ -> ()
            | _ -> ()
        | FsCommand(_, requires, composes, _) ->
            match caseToNode.TryGetValue(defaultVal) with
            | true, node ->
                for dep in requires do
                    match caseToNode.TryGetValue(dep) with
                    | true, depNode -> node.RequiresResolved.Add(depNode)
                    | _ -> ()
                for dep in composes do
                    match caseToNode.TryGetValue(dep) with
                    | true, depNode -> node.ComposesResolved.Add(depNode)
                    | _ -> ()
            | _ -> ()

    graph
