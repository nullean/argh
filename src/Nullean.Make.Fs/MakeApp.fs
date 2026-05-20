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

    let parseAs (t: Type) (raw: string) : obj =
        if   t = typeof<string>         then raw :> obj
        elif t = typeof<int>            then int raw :> obj
        elif t = typeof<int64>          then int64 raw :> obj
        elif t = typeof<float>          then float raw :> obj
        elif t = typeof<bool>           then String.Equals(raw, "true", StringComparison.OrdinalIgnoreCase) :> obj
        elif t = typeof<string option>  then (Some raw : string option) :> obj
        elif t = typeof<int option>     then (Some (int raw) : int option) :> obj
        elif t = typeof<bool option>    then
            (Some (String.Equals(raw, "true", StringComparison.OrdinalIgnoreCase)) : bool option) :> obj
        else raw :> obj

    type FsOptionDecl =
        { Long: string; Short: string option; Description: string option; IsFlag: bool; Set: string -> unit }

    let inline tryExec (f: unit -> 'a) =
        try Ok (f ())
        with :? MakeException as ex -> Error (ex.ExitCode, ex.Message)

/// Entry point for F# build scripts using a DU as the target identity.
type MakeApp<'TCase when 'TCase : comparison and 'TCase : not null>
    (appName: string, _description: string option) =

    let _options = Collections.Generic.List<FsOptionDecl>()
    let mutable _bindFn : ('TCase -> Definition<'TCase>) option = None

    let extractGlobals (argv: string[]) =
        let remaining    = Collections.Generic.List<string>()
        let mutable singleTarget = false
        let mutable showHelp     = false
        let mutable showVersion  = false
        let mutable i = 0
        while i < argv.Length do
            let arg = argv.[i]
            if   arg = "-h"  || arg = "--help"          then showHelp     <- true; i <- i + 1
            elif arg = "--version"                       then showVersion  <- true; i <- i + 1
            elif arg = "-s"  || arg = "--single-target" then singleTarget <- true; i <- i + 1
            else
                let norm = arg.TrimStart('-')
                let matched =
                    _options |> Seq.tryFind (fun o ->
                        let longNorm  = o.Long.TrimStart('-')
                        let shortNorm = o.Short |> Option.map (fun s -> s.TrimStart('-'))
                        norm = longNorm || (shortNorm |> Option.exists (fun s -> norm = s)))
                match matched with
                | Some opt ->
                    if opt.IsFlag then opt.Set "true"; i <- i + 1
                    else
                        i <- i + 1
                        if i < argv.Length then opt.Set argv.[i]; i <- i + 1
                | None -> remaining.Add(arg); i <- i + 1
        remaining.ToArray(), singleTarget, showHelp, showVersion

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
        _options.Add({ Long = long; Short = short; Description = desc; IsFlag = true; Set = r.Set })
        r

    /// Register a global option of type 'T. Returns a mutable ref; read .Value or use ctx.Get() in target bodies.
    member _.Option<'T>(long: string, ?short: string, ?desc: string, ?defaultValue: 'T) : OptionRef<'T> =
        let dv = defaultValue |> Option.defaultValue Unchecked.defaultof<'T>
        let t  = typeof<'T>
        let parser (raw: string) : 'T = parseAs t raw :?> 'T
        let r  = OptionRef<'T>(long, short, desc, dv, parser)
        _options.Add({ Long = long; Short = short; Description = desc; IsFlag = false; Set = r.Set })
        r

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
            |> Seq.map (fun o -> o.Long, o.Short, o.Description, o.IsFlag)
            |> Seq.toList

        task {
            match tryExec (fun () -> FsGraphBuilder.buildGraph<'TCase> appName bind optDecls) with
            | Error (code, msg) ->
                eprintfn "%s" msg
                return code
            | Ok graph ->

            match tryExec (fun () -> GraphValidator.Validate(graph)) with
            | Error (code, msg) ->
                eprintfn "%s" msg
                return code
            | Ok () ->

            if argv.Length = 0 then
                MakeHelpPrinter.PrintRoot(graph, appName)
                return 0
            else

            let remaining, singleTarget, showHelp, showVersion = extractGlobals argv

            if showVersion then
                printfn "0.0.0"
                return 0
            else

            let routeKey, targetArgs = resolveRoute remaining graph.ByRoute

            if showHelp then
                match graph.ByRoute.TryGetValue(routeKey) with
                | true, node when node.Kind = TargetKind.Command ->
                    MakeHelpPrinter.PrintCommand(node, graph, appName)
                | true, node ->
                    MakeHelpPrinter.PrintTarget(node, graph, appName)
                | _ ->
                    MakeHelpPrinter.PrintRoot(graph, appName)
                return 0
            else

            if String.IsNullOrEmpty(routeKey) then
                match remaining |> Array.tryFind (fun t -> not (t.StartsWith("-"))) with
                | Some t ->
                    eprintfn "Unknown target '%s'." t
                    return 2
                | None ->
                    MakeHelpPrinter.PrintRoot(graph, appName)
                    return 0
            else

            match graph.ByRoute.TryGetValue(routeKey) with
            | false, _ ->
                eprintfn "Unknown target '%s'." routeKey
                return 2
            | true, node ->
                let parsed =
                    ParsedArgs(
                        Target       = node,
                        TargetArgs   = targetArgs,
                        SingleTarget = singleTarget)
                return! DepGraphExecutor.ExecuteAsync(node, parsed, graph)
        }
