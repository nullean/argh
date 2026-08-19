/// Builds the public documentation locally and serves it.
///
/// The branded landing page (argh-landing.html) is a standalone document that replaces the
/// generated index.html after the docs-builder build — the only way to preview the real site
/// is to build, apply the override, and serve the output.
module Documentation

open System
open System.IO
open System.IO.Compression
open System.Net
open System.Net.Http
open System.Text
open System.Threading.Tasks
open ProcNet

let private exec binary args = Proc.Exec(binary, List.toArray args) |> ignore

let private docsSource = "docs"
let private landingPage = Path.Combine(docsSource, "argh-landing.html")

/// docs-builder always writes here.
let private htmlOutput = Path.Combine(".artifacts", "docs", "html")

// ─────────────────────────────  acquiring docs-builder  ─────────────────────────────

let private toolPath =
    let exe = if OperatingSystem.IsWindows() then "docs-builder.exe" else "docs-builder"
    Path.Combine(".artifacts", "tools", exe)

let private archiveName () =
    let arch =
        match Runtime.InteropServices.RuntimeInformation.OSArchitecture with
        | Runtime.InteropServices.Architecture.Arm64 -> "arm64"
        | Runtime.InteropServices.Architecture.X64 -> "x64"
        | other -> failwithf "docs-builder ships no binary for %O" other
    if OperatingSystem.IsMacOS() then sprintf "docs-builder-mac-%s.zip" arch
    elif OperatingSystem.IsLinux() then sprintf "docs-builder-linux-%s.zip" arch
    elif OperatingSystem.IsWindows() then sprintf "docs-builder-win-%s.zip" arch
    else failwith "unsupported operating system for docs-builder"

let ensureTool () =
    if File.Exists toolPath then toolPath
    else

    let archive = archiveName ()
    let version =
        match Environment.GetEnvironmentVariable "DOCS_BUILDER_VERSION" with
        | null | "" -> "latest"
        | v -> v
    let url =
        match version with
        | "latest" -> sprintf "https://github.com/elastic/docs-builder/releases/latest/download/%s" archive
        | v -> sprintf "https://github.com/elastic/docs-builder/releases/download/%s/%s" v archive

    printfn "docs-builder not cached, downloading %s" url
    Directory.CreateDirectory(Path.GetDirectoryName toolPath) |> ignore

    let zip = Path.Combine(Path.GetTempPath(), archive)
    use client = new HttpClient()
    client.Timeout <- TimeSpan.FromMinutes 5.0
    do
        use response = client.GetAsync(url).GetAwaiter().GetResult()
        response.EnsureSuccessStatusCode() |> ignore
        use file = File.Create zip
        response.Content.CopyToAsync(file).GetAwaiter().GetResult()

    let name = Path.GetFileName toolPath
    do
        use zipFile = ZipFile.OpenRead zip
        let entry =
            zipFile.Entries
            |> Seq.tryFind (fun e -> String.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase))
            |> Option.defaultWith (fun () -> failwithf "%s did not contain %s" archive name)
        entry.ExtractToFile(toolPath, true)
    File.Delete zip

    if not (OperatingSystem.IsWindows()) then
        File.SetUnixFileMode(
            toolPath,
            UnixFileMode.UserRead ||| UnixFileMode.UserWrite ||| UnixFileMode.UserExecute
            ||| UnixFileMode.GroupRead ||| UnixFileMode.GroupExecute
            ||| UnixFileMode.OtherRead ||| UnixFileMode.OtherExecute)

    printfn "docs-builder cached at %s" toolPath
    toolPath

// ─────────────────────────────  build  ─────────────────────────────

/// Build docs and apply the landing page override.
/// argh.nullean.net serves from the domain root — no path prefix needed.
let build () =
    let tool = ensureTool ()

    exec tool ["build"; "--path"; docsSource]

    if not (Directory.Exists htmlOutput) then
        failwithf "docs-builder reported success but %s does not exist" htmlOutput

    File.Copy(landingPage, Path.Combine(htmlOutput, "index.html"), true)
    printfn "applied landing page override -> %s" (Path.Combine(htmlOutput, "index.html"))

// ─────────────────────────────  serve  ─────────────────────────────

let private contentType (path: string) =
    match Path.GetExtension(path).ToLowerInvariant() with
    | ".html" | ".htm" -> "text/html; charset=utf-8"
    | ".css"  -> "text/css; charset=utf-8"
    | ".js" | ".mjs" -> "text/javascript; charset=utf-8"
    | ".json" -> "application/json; charset=utf-8"
    | ".svg"  -> "image/svg+xml"
    | ".woff2"-> "font/woff2"
    | ".woff" -> "font/woff"
    | ".ttf"  -> "font/ttf"
    | ".png"  -> "image/png"
    | ".jpg" | ".jpeg" -> "image/jpeg"
    | ".gif"  -> "image/gif"
    | ".webp" -> "image/webp"
    | ".ico"  -> "image/x-icon"
    | ".txt"  -> "text/plain; charset=utf-8"
    | ".xml"  -> "application/xml; charset=utf-8"
    | ".wasm" -> "application/wasm"
    | _       -> "application/octet-stream"

let private writeResponse (response: HttpListenerResponse) (path: string) =
    response.ContentType <- contentType path
    let bytes = File.ReadAllBytes path
    response.OutputStream.Write(bytes, 0, bytes.Length)

let private notFound (response: HttpListenerResponse) (raw: string) =
    response.StatusCode <- 404
    response.ContentType <- "text/plain; charset=utf-8"
    let body = Encoding.UTF8.GetBytes(sprintf "404 %s" raw)
    response.OutputStream.Write(body, 0, body.Length)

let private handle (root: string) (context: HttpListenerContext) =
    let response = context.Response
    try
        try
            let raw = Uri.UnescapeDataString context.Request.Url.AbsolutePath
            let candidate = Path.GetFullPath(Path.Combine(root, raw.TrimStart('/')))

            if not (candidate.StartsWith(root, StringComparison.Ordinal)) then
                notFound response raw
            elif raw = "/" || raw = "" then
                let index = Path.Combine(root, "index.html")
                if File.Exists index then writeResponse response index else notFound response raw
            elif File.Exists candidate then
                writeResponse response candidate
            elif Directory.Exists candidate then
                if not (raw.EndsWith "/") then response.Redirect(raw + "/")
                else
                    let index = Path.Combine(candidate, "index.html")
                    if File.Exists index then writeResponse response index else notFound response raw
            else
                notFound response raw
        with e ->
            response.StatusCode <- 500
            let body = Encoding.UTF8.GetBytes e.Message
            response.OutputStream.Write(body, 0, body.Length)
    finally
        response.OutputStream.Close()

let serve (port: int) =
    let root = Path.GetFullPath htmlOutput
    let url = sprintf "http://localhost:%d/" port

    let listener = new HttpListener()
    listener.Prefixes.Add(sprintf "http://localhost:%d/" port)
    try listener.Start()
    with :? HttpListenerException ->
        failwithf "could not listen on port %d — it is probably already in use. Pass --port <n>." port

    printfn ""
    printfn "  documentation serving at %s" url
    printfn "  ctrl-c to stop; re-run './build.sh docs' to pick up edits"
    printfn ""

    let headless =
        [ "CI"; "TF_BUILD"; "GITHUB_ACTIONS" ]
        |> List.exists (fun v -> not (String.IsNullOrEmpty(Environment.GetEnvironmentVariable v)))
    if not headless then
        try
            let opener, a =
                if OperatingSystem.IsMacOS() then "open", url
                elif OperatingSystem.IsWindows() then "cmd", sprintf "/c start %s" url
                else "xdg-open", url
            Diagnostics.ProcessStartInfo(opener, Arguments = a, UseShellExecute = false)
            |> Diagnostics.Process.Start
            |> ignore
        with _ -> ()

    let mutable running = true
    Console.CancelKeyPress.Add(fun e ->
        e.Cancel <- true
        running <- false
        listener.Stop())

    while running do
        try
            let context = listener.GetContext()
            Task.Run(fun () -> handle root context) |> ignore
        with
        | :? HttpListenerException -> ()
        | :? ObjectDisposedException -> ()

    (listener :> IDisposable).Dispose()
    printfn "stopped"
