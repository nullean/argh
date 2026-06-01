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

// A DU case is a structural namespace if its single field is itself a union type (not a record).
let private isStructuralNs (ci: UnionCaseInfo) =
    let fs = ci.GetFields()
    fs.Length = 1 && FSharpType.IsUnion(fs.[0].PropertyType)

let buildGraph<'TCase when 'TCase : comparison and 'TCase : not null>
    (appName: string)
    (appDescription: string option)
    (bind: 'TCase -> Definition<'TCase>)
    (optionDecls: (string * string option * string option * bool) list)
    : BuildGraph =

    let graph = BuildGraph()
    graph.AppName <- appName
    graph.AppDescription <- match appDescription with Some d -> d | None -> null

    // Register global options for help rendering (Property is null for F# option refs).
    for (long, short, desc, isFlag) in optionDecls do
        graph.GlobalOptions.Add(
            GlobalOptionNode(
                Long        = long,
                Short       = (match short with Some s -> s | None -> null),
                Description = (match desc  with Some d -> d | None -> null),
                IsFlag      = isFlag))

    let cases = FSharpType.GetUnionCases(typeof<'TCase>)

    // First pass: collect namespace markers — either structural (union-payload case) or explicit Make.ns.
    let namespaces =
        cases
        |> Array.choose (fun ci ->
            if isStructuralNs ci then
                Some (ci.Name, toKebabCase ci.Name)
            else
                let v = defaultCaseValue ci :?> 'TCase
                match bind v with
                | FsNamespace(segment, _) -> Some (ci.Name, segment)
                | _ -> None)

    // Derive CLI route for non-structural cases using namespace prefix matching (backward compat).
    let deriveRoute (caseName: string) =
        namespaces
        |> Array.tryPick (fun (nsName, segment) ->
            if caseName.StartsWith(nsName, StringComparison.Ordinal) && caseName.Length > nsName.Length then
                Some [| segment; toKebabCase (caseName.Substring(nsName.Length)) |]
            else None)
        |> Option.defaultValue [| toKebabCase caseName |]

    // Map 'TCase value → TargetNode for dep resolution.
    let caseToNode = Collections.Generic.Dictionary<'TCase, TargetNode>()

    // Register one TargetNode. caseInfoForBody drives the SyncBody closure shape.
    // For structural namespace sub-cases, caseInfoForBody is the sub-DU's UnionCaseInfo.
    // NOTE: namespace sub-cases must be parameterless; payload sub-DUs are not supported.
    let registerNode (route: string[]) (def: Definition<'TCase>) (caseInfoForBody: UnionCaseInfo) (key: 'TCase) =
        let kind    = match def with | FsCommand _ -> TargetKind.Command | _ -> TargetKind.Target
        let rawDesc = match def with | FsTarget(d,_,_) | FsCommand(d,_,_,_) -> d | _ -> ""
        // Commands: leave description empty so MakeHelpPrinter can auto-generate it from the graph.
        // Targets: fall back to the kebab-case name when no description is provided.
        let desc    =
            if not (String.IsNullOrEmpty(rawDesc)) then rawDesc
            elif kind = TargetKind.Command then ""
            else toKebabCase (Array.last route)
        let fields  = caseInfoForBody.GetFields()

        let plainBody () =
            match def with
            | FsTarget(_, _, b)          -> b sharedCtx
            | FsCommand(_, _, _, Some b) -> b sharedCtx
            | _ -> ()

        // Re-binds CLI args at execution time and re-invokes bind for typed-payload cases.
        let payloadBody () =
            let payloadType = fields.[0].PropertyType
            let targetArgs  =
                let ctx = MakeContext.Current
                if obj.ReferenceEquals(ctx, null) then [||] else ctx.TargetArgs
            let payload   = FsDtoBinder.bind payloadType targetArgs
            let boundCase : 'TCase = FSharpValue.MakeUnion(caseInfoForBody, [| payload |]) :?> 'TCase
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
        caseToNode.[key] <- node

    // Second pass: create TargetNode for each non-namespace case.
    for caseInfo in cases do
        if isStructuralNs caseInfo then
            // Structural namespace: register each sub-case with route [segment, sub-name].
            let subDuType = caseInfo.GetFields().[0].PropertyType
            let segment =
                namespaces |> Array.tryPick (fun (n, s) -> if n = caseInfo.Name then Some s else None)
                |> Option.defaultValue (toKebabCase caseInfo.Name)
            for subCaseInfo in FSharpType.GetUnionCases(subDuType) do
                let subDefault = defaultCaseValue subCaseInfo
                let fullVal : 'TCase = FSharpValue.MakeUnion(caseInfo, [| subDefault |]) :?> 'TCase
                let def = bind fullVal
                match def with
                | FsNamespace _ -> ()
                | _ ->
                    let route = [| segment; toKebabCase subCaseInfo.Name |]
                    registerNode route def subCaseInfo fullVal
        else
            let defaultVal : 'TCase = defaultCaseValue caseInfo :?> 'TCase
            let def = bind defaultVal
            match def with
            | FsNamespace _ -> ()
            | _ ->
                let route = deriveRoute caseInfo.Name
                registerNode route def caseInfo defaultVal

    // Third pass: resolve deps/requires/composes by iterating over all registered nodes.
    for KeyValue(tcase, node) in caseToNode do
        match bind tcase with
        | FsNamespace _ -> ()
        | FsTarget(_, deps, _) ->
            for dep in deps do
                match caseToNode.TryGetValue(dep) with
                | true, depNode -> node.RequiresResolved.Add(depNode)
                | _ -> ()
        | FsCommand(_, requires, composes, _) ->
            for dep in requires do
                match caseToNode.TryGetValue(dep) with
                | true, depNode -> node.RequiresResolved.Add(depNode)
                | _ -> ()
            for dep in composes do
                match caseToNode.TryGetValue(dep) with
                | true, depNode -> node.ComposesResolved.Add(depNode)
                | _ -> ()

    graph
