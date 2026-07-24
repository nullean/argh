# Console app quick start

The simplest way to use Nullean.Argh - no dependencies, no host, just a CLI.

:::::{stepper}

::::{step} Add the package reference

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

::::

::::{step} Create a minimal CLI

```csharp
using Nullean.Argh;

var app = new ArghApp(); // <1>
app.Map("hello", MyHandlers.SayHello); // <2>

return await app.RunAsync(args); // <3>
```

1. Create a new `ArghApp` instance.
2. Register a command named `hello` bound to a method group.
3. Dispatch into generated code. The source generator emits parsing, routing, and dispatch logic at build time.

::::

::::{step} Run it

```shell
dotnet run -- hello
```

::::

:::::

## Registration forms

All three registration forms are supported:

```csharp
using Nullean.Argh;

var app = new ArghApp();

app.Map("deploy", DeployHandlers.Run); // <1>
app.Map("greet", (string name) => Console.WriteLine($"Hello, {name}!")); // <2>
app.Map<StorageHandlers>(); // <3>

return await app.RunAsync(args);
```

1. **Method group** - direct typed dispatch.
2. **Lambda** - convenient for simple one-liners.
3. **Class** - registers every public method on `T` as a command.

## Adding a root command

Use `MapRoot` to define a handler that runs when no subcommand is given:

```csharp
var app = new ArghApp();
app.MapRoot(Handlers.DefaultAction);
app.Map("deploy", Handlers.Deploy);

return await app.RunAsync(args);
```

## App description

For apps without a root command, use `UseCliDescription` to set a one-liner shown beneath the `Usage:` line in `--help`:

```csharp
app.UseCliDescription("Manage and deploy your application's cloud resources.");
```

:::{important}
`UseCliDescription` cannot be combined with `MapRoot`. If you have a root command, put the description in its XML doc `<summary>` instead.
:::
