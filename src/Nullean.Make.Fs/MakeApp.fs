#nowarn "3261" // nullable reference type warnings
namespace Nullean.Make.Fs

open System
open System.Threading.Tasks
open Nullean.Make
open Nullean.Make.Discovery
open Nullean.Make.Execution
open Nullean.Make.Help
open Nullean.Make.Parsing

[<AutoOpen>]
module private MakeAppHelpers =

    type FsOptionDecl =
        { Long: string; Short: string option; Description: string option; IsFlag: bool; ValueType: Type option; Set: string -> unit }

    /// The informational version of the entry assembly, as Argh's own --version prints.
    let entryVersion () =
        match Reflection.Assembly.GetEntryAssembly() with
        | null -> "0.0.0"
        | asm ->
            match Reflection.CustomAttributeExtensions.GetCustomAttribute<Reflection.AssemblyInformationalVersionAttribute>(asm) with
            | null -> asm.GetName().Version.ToString()
            | attr -> attr.InformationalVersion

    let inline tryExec (f: unit -> 'a) =
        try Ok (f ())
        with :? MakeException as ex -> Error (ex.ExitCode, ex.Message)

/// Entry point for F# build scripts using a DU as the target identity.
type MakeApp<'TCase when 'TCase : comparison and 'TCase : not null>
    (appName: string, description: string option) =

    let _options = Collections.Generic.List<FsOptionDecl>()
    let mutable _bindFn : ('TCase -> Definition<'TCase>) option = None
    let mutable _default : 'TCase option = None

    let extractGlobals (argv: string[]) =
        let remaining    = Collections.Generic.List<string>()
        let mutable singleTarget = false
        let mutable showHelp     = false
        let mutable showVersion  = false
        let mutable graph : GraphFormat option = None
        let mutable i = 0
        while i < argv.Length do
            let arg = argv.[i]
            let graphFormat = GraphOption.TryRead(argv, &i)
            if graphFormat.HasValue then graph <- Some graphFormat.Value
            elif arg = "-h"  || arg = "--help"          then showHelp     <- true; i <- i + 1
            elif arg = "--version"                       then showVersion  <- true; i <- i + 1
            elif arg = "-s"  || arg = "--single-target" then singleTarget <- true; i <- i + 1
            else
                // --name=value carries the value inline.
                let eq = if arg.StartsWith("-") then arg.IndexOf('=') else -1
                let nameToken, inlineValue =
                    if eq > 0 then arg.Substring(0, eq), Some (arg.Substring(eq + 1)) else arg, None
                let matched =
                    if not (nameToken.StartsWith("-")) then None
                    else
                        let norm = nameToken.TrimStart('-')
                        _options |> Seq.tryFind (fun o ->
                            let longNorm  = o.Long.TrimStart('-')
                            let shortNorm = o.Short |> Option.map (fun s -> s.TrimStart('-'))
                            norm = longNorm || (shortNorm |> Option.exists (fun s -> norm = s)))
                match matched with
                | Some opt ->
                    i <- i + 1
                    match inlineValue with
                    | Some v -> opt.Set v
                    | None when opt.IsFlag -> opt.Set "true"
                    | None ->
                        if i >= argv.Length then
                            raise (MakeException($"Missing value for '{opt.Long}'.", 2))
                        opt.Set argv.[i]
                        i <- i + 1
                | None -> remaining.Add(arg); i <- i + 1
        remaining.ToArray(), singleTarget, showHelp, showVersion, graph

    let resolveRoute (remaining: string[]) (byRoute: Collections.Generic.Dictionary<string, TargetNode>) =
        let routeTokens = Collections.Generic.List<string>()
        let rest        = Collections.Generic.List<string>()
        let mutable routeDone = false
        for token in remaining do
            if not routeDone && not (token.StartsWith("-")) then
                let candidate = String.concat "/" [| yield! routeTokens; token.ToLowerInvariant() |]
                if byRoute.ContainsKey(candidate) then
                    routeTokens.Add(token.ToLowerInvariant())
                else
                    let isPrefix =
                        byRoute.Keys
                        |> Seq.exists (fun k -> k.StartsWith(candidate + "/", StringComparison.OrdinalIgnoreCase))
                    if isPrefix then routeTokens.Add(token.ToLowerInvariant())
                    else routeDone <- true; rest.Add(token)
            else rest.Add(token)
        String.concat "/" routeTokens, rest.ToArray()

    /// Register a boolean flag. Returns a mutable ref; read .Value or use ctx.IsSet() in target bodies.
    member _.Flag(long: string, ?short: string, ?desc: string) : OptionRef<bool> =
        let r = OptionRef<bool>(long, short, desc, false, fun s ->
            String.Equals(s, "true", StringComparison.OrdinalIgnoreCase) || s = "1")
        _options.Add({ Long = long; Short = short; Description = desc; IsFlag = true; ValueType = None; Set = r.Set })
        r

    /// Register a global option of type 'T. Returns a mutable ref; read .Value or use ctx.Get() in target bodies.
    member _.Option<'T>(long: string, ?short: string, ?desc: string, ?defaultValue: 'T) : OptionRef<'T> =
        let dv = defaultValue |> Option.defaultValue Unchecked.defaultof<'T>
        let t  = typeof<'T>
        let parser (raw: string) : 'T = FsDtoBinder.parseScalar t raw (long.TrimStart('-')) :?> 'T
        let r  = OptionRef<'T>(long, short, desc, dv, parser)
        _options.Add({ Long = long; Short = short; Description = desc; IsFlag = false; ValueType = Some t; Set = r.Set })
        r

    /// Name the target that runs when no target is given, so `./build.sh` builds. Help shows it.
    /// A run with `-s` and no target still prints help.
    member _.Default(case: 'TCase) =
        _default <- Some case

    /// Provide the single exhaustive binding function: one match arm per DU case.
    member _.Bind(fn: 'TCase -> Definition<'TCase>) =
        _bindFn <- Some fn

    /// Parse argv, build the execution plan, and run it. Returns an exit code.
    member _.RunAsync(argv: string[]) : Task<int> =
        match _bindFn with
        | None ->
            eprintfn "[make] app.Bind(...) was not called"
            Task.FromResult(1)
        | Some bind ->

        let optDecls =
            _options
            |> Seq.map (fun o -> o.Long, o.Short, o.Description, o.IsFlag, o.ValueType)
            |> Seq.toList

        task {
            match tryExec (fun () -> FsGraphBuilder.buildGraph<'TCase> appName description bind _default optDecls) with
            | Error (code, msg) ->
                eprintfn "%s" msg
                return code
            | Ok graph ->

            match tryExec (fun () -> GraphValidator.Validate(graph)) with
            | Error (code, msg) ->
                eprintfn "%s" msg
                return code
            | Ok () ->

            match Nullean.Make.Wrappers.MakeIntrinsics.TryRun(argv, graph) |> Option.ofNullable with
            | Some exitCode -> return exitCode
            | None ->

            if argv.Length = 0 && _default.IsNone then
                MakeHelpPrinter.PrintRoot(graph, appName)
                return 0
            else

            // Everything after the first "--" is forwarded to the target untouched.
            let sep = Array.IndexOf(argv, "--")
            let argv, forwarded =
                if sep >= 0 then argv.[.. sep - 1], argv.[sep + 1 ..] else argv, [||]

            match tryExec (fun () -> extractGlobals argv) with
            | Error (code, msg) ->
                eprintfn "%s" msg
                return code
            | Ok (remaining, singleTarget, showHelp, showVersion, graphFormat) ->

            if showVersion then
                printfn "%s" (entryVersion ())
                return 0
            else

            let routeKey, routeArgs = resolveRoute remaining graph.ByRoute
            let targetArgs = if forwarded.Length > 0 then Array.append routeArgs (Array.append [| "--" |] forwarded) else routeArgs

            if showHelp then
                match graph.ByRoute.TryGetValue(routeKey) with
                | true, node when node.Kind = TargetKind.Command ->
                    MakeHelpPrinter.PrintCommand(node, graph, appName)
                | true, node ->
                    MakeHelpPrinter.PrintTarget(node, graph, appName)
                | _ when not (String.IsNullOrEmpty routeKey) && MakeHelpPrinter.PrintNamespace(routeKey, graph, appName) -> ()
                | _ ->
                    MakeHelpPrinter.PrintRoot(graph, appName)
                return 0
            else

            let namedTarget = remaining |> Array.tryFind (fun t -> not (t.StartsWith("-")))
            let routeKey, targetArgs =
                match namedTarget, Option.ofObj graph.Default with
                | None, Some d when not singleTarget && String.IsNullOrEmpty(routeKey) ->
                    // No target named: run the default one. Flags left over belong to it.
                    String.concat "/" d.Route, targetArgs
                | _ -> routeKey, targetArgs

            if String.IsNullOrEmpty(routeKey) then
                match namedTarget with
                | Some t ->
                    eprintfn "Unknown target '%s'." t
                    return 2
                | None when graphFormat.IsSome ->
                    return DepGraphExecutor.DrawWholeGraph(graph, graphFormat.Value)
                | None ->
                    MakeHelpPrinter.PrintRoot(graph, appName)
                    return 0
            else

            match graph.ByRoute.TryGetValue(routeKey) with
            | false, _ ->
                eprintfn "Unknown target '%s'." routeKey
                return 2
            | true, node when isNull node.DtoType && targetArgs.Length > 0 ->
                let first = if targetArgs.[0] = "--" && targetArgs.Length > 1 then targetArgs.[1] else targetArgs.[0]
                eprintfn "Unexpected argument '%s'. Target '%s' takes no arguments." first routeKey
                return 2
            | true, node ->
                let parsed =
                    ParsedArgs(
                        Target       = node,
                        TargetArgs   = targetArgs,
                        SingleTarget = singleTarget,
                        Graph        = Option.toNullable graphFormat)
                return! DepGraphExecutor.ExecuteAsync(node, parsed, graph)
        }
