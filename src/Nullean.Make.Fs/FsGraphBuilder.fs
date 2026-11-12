#nowarn "3261" // nullable reference type warnings — F# DU values are never null
module internal Nullean.Make.Fs.FsGraphBuilder

open System
open Microsoft.FSharp.Reflection
open Nullean.Make
open Nullean.Make.Discovery
open Nullean.Make.Execution

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

/// The payload record of a case value, or null when the case carries none. A namespace sub-case carries no payload.
let private payloadOf (value: obj) : obj =
    if isNull value then null
    else
        let ci, fields = FSharpValue.GetUnionFields(value, value.GetType())
        if fields.Length = 1 && FSharpType.IsRecord(ci.GetFields().[0].PropertyType) then fields.[0] else null

/// The targets of an F# MakeApp: a node's value is the DU case value. `bind` runs again for each value, so a dependency
/// declared as `Test { Filter = Some "Unit" }` runs the body bind returns for that value.
type private FsTargetSource<'TCase when 'TCase : comparison and 'TCase : not null>
    (bind: 'TCase -> Definition<'TCase>,
     nodeCases: Collections.Generic.Dictionary<TargetNode, 'TCase * UnionCaseInfo>,
     resolve: TargetNode -> 'TCase -> TargetNode) =

    let refs (node: TargetNode) (deps: 'TCase list) =
        deps |> List.map (fun d -> DepRef(resolve node d, box d)) |> ResizeArray

    interface Nullean.Make.Execution.ITargetSource with
        member _.DefaultValue(node) = box (fst nodeCases.[node])

        member _.BindArguments(node, args) =
            let defaultValue, caseInfo = nodeCases.[node]
            let fields = caseInfo.GetFields()
            if fields.Length = 1 && FSharpType.IsRecord(fields.[0].PropertyType) then
                FSharpValue.MakeUnion(caseInfo, [| FsDtoBinder.bind fields.[0].PropertyType args |])
            else box defaultValue

        member _.Describe(node, value) =
            let spec = Nullean.Make.Execution.InstanceSpec()
            let body (b: FsContext -> unit) =
                Func<Threading.CancellationToken, Threading.Tasks.Task>(fun _ -> b sharedCtx; Threading.Tasks.Task.CompletedTask)
            match bind (unbox<'TCase> value) with
            | FsTarget(_, deps, b, _) ->
                spec.Add(Nullean.Make.Execution.StageKind.Requires, refs node deps)
                spec.Body <- body b
            | FsCommand(_, requires, stages, b) ->
                spec.Add(StageKind.Requires, refs node requires)
                for sideBySide, targets in stages do spec.Add(StageKind.Composes, refs node targets, sideBySide)
                b |> Option.iter (fun b -> spec.Body <- body b)
            | FsNamespace _ -> ()
            spec

        member _.FormatValue(node, value) =
            Nullean.Make.Execution.ValueDisplay.Differences(payloadOf value, payloadOf (box (fst nodeCases.[node])))

let buildGraph<'TCase when 'TCase : comparison and 'TCase : not null>
    (appName: string)
    (appDescription: string option)
    (bind: 'TCase -> Definition<'TCase>)
    (defaultCase: 'TCase option)
    (optionDecls: (string * string option * string option * bool * Type option) list)
    : BuildGraph =

    let graph = BuildGraph()
    graph.AppName <- appName
    graph.AppDescription <- match appDescription with Some d -> d | None -> null

    // Register global options for help rendering (Property is null for F# option refs).
    for (long, short, desc, isFlag, valueType) in optionDecls do
        graph.GlobalOptions.Add(
            GlobalOptionNode(
                Long        = long,
                Short       = (match short with Some s -> s | None -> null),
                Description = (match desc  with Some d -> d | None -> null),
                IsFlag      = isFlag,
                ValueType   = (match valueType with Some t -> t | None -> null)))

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

    // Map each case to its TargetNode by case identity (case name, plus sub-case for namespaces), not by value:
    // a dependency such as `Test { Filter = Some "x" }` must find the `Test` node registered with defaults.
    let caseKey (v: 'TCase) : string =
        let ci, fields = FSharpValue.GetUnionFields(box v, typeof<'TCase>)
        if isStructuralNs ci then
            let subCi, _ = FSharpValue.GetUnionFields(fields.[0], ci.GetFields().[0].PropertyType)
            ci.Name + "." + subCi.Name
        else ci.Name
    let caseToNode = Collections.Generic.Dictionary<string, TargetNode * 'TCase>()
    // Each node's default value, and the case whose payload a value of it carries.
    let nodeCases = Collections.Generic.Dictionary<TargetNode, 'TCase * UnionCaseInfo>(HashIdentity.Reference)

    // Register one TargetNode. caseInfoForBody drives the SyncBody closure shape.
    // For structural namespace sub-cases, caseInfoForBody is the sub-DU's UnionCaseInfo.
    // NOTE: namespace sub-cases must be parameterless; payload sub-DUs are not supported.
    let registerNode (route: string[]) (def: Definition<'TCase>) (caseInfoForBody: UnionCaseInfo) (key: 'TCase) =
        let kind    = match def with | FsCommand _ -> TargetKind.Command | _ -> TargetKind.Target
        let explicitDesc = match def with | FsTarget(d,_,_,_) | FsCommand(d,_,_,_) -> d | _ -> ""
        // No explicit description: use the case's /// doc comment (T:<Union>.<Case> in the XML doc file).
        let rawDesc =
            if not (String.IsNullOrEmpty explicitDesc) then explicitDesc
            else
                let unionType = caseInfoForBody.DeclaringType
                let docId = "T:" + unionType.FullName.Replace('+', '.') + "." + caseInfoForBody.Name
                match Nullean.Make.Docs.DocDescriptions.Summary(unionType.Assembly, docId) with
                | null -> ""
                | summary -> summary
        // Commands: leave description empty so MakeHelpPrinter can auto-generate it from the graph.
        // Targets: fall back to the kebab-case name when no description is provided.
        let desc    =
            if not (String.IsNullOrEmpty(rawDesc)) then rawDesc
            elif kind = TargetKind.Command then ""
            else toKebabCase (Array.last route)
        let fields  = caseInfoForBody.GetFields()
        let dtoType  = if fields.Length = 1 && FSharpType.IsRecord(fields.[0].PropertyType)
                       then fields.[0].PropertyType else null
        let node =
            TargetNode(
                Route           = route,
                ConfigureMethod = null,
                Kind            = kind,
                Hidden          = (match def with FsTarget(_, _, _, true) -> true | _ -> false),
                Description     = desc,
                DtoType         = dtoType)

        graph.Targets.Add(node)
        graph.ByRoute.[String.concat "/" route] <- node
        caseToNode.[caseKey key] <- (node, key)
        nodeCases.[node] <- (key, caseInfoForBody)

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
    let resolve (node: TargetNode) (dep: 'TCase) =
        match caseToNode.TryGetValue(caseKey dep) with
        | true, (depNode, _) -> depNode
        | _ ->
            let route = String.concat " " node.Route
            raise (MakeException($"Target '{route}' depends on '{caseKey dep}', which is not a target or command.", 1))
    for KeyValue(_, (node, tcase)) in caseToNode do
        match bind tcase with
        | FsNamespace _ -> ()
        | FsTarget(_, deps, _, _) ->
            for dep in deps do node.RequiresResolved.Add(resolve node dep)
        | FsCommand(_, requires, stages, _) ->
            let composes = stages |> List.collect snd
            for dep in requires do node.RequiresResolved.Add(resolve node dep)
            for dep in composes do node.ComposesResolved.Add(resolve node dep)

    graph.Source <- FsTargetSource<'TCase>(bind, nodeCases, (fun node dep -> resolve node dep))

    match defaultCase with
    | None -> ()
    | Some d ->
        match caseToNode.TryGetValue(caseKey d) with
        | true, (node, _) -> graph.Default <- node
        | _ -> raise (MakeException($"The default target '{caseKey d}' is not a target or command.", 1))

    graph
