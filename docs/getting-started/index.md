# Getting started

Nullean.Argh provides two entry points depending on your application model. Both share the same registration surface (`Map`, `Map<T>`, `MapRoot`, `MapNamespace`, etc.) and the same source generator. The difference is how the runtime is bootstrapped and whether DI is available.

::::tab-set

:::tab-item Console app
:group: app-type
:sync: console

Use `Nullean.Argh` directly with `ArghApp` for lightweight CLIs with no external dependencies.

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh" />
</ItemGroup>
```

```csharp
var app = new ArghApp();
app.Map("hello", MyHandlers.SayHello);
return await app.RunAsync(args);
```

**[Console app quick start →](console-app.md)**

:::

:::tab-item Hosted app
:group: app-type
:sync: hosted

Use `Nullean.Argh.Hosting` when your app is built on `Microsoft.Extensions.Hosting` and you want DI, lifetimes, and host integration.

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Hosting" />
</ItemGroup>
```

```csharp
var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddArgh(args, b =>
{
    b.Map("hello", MyHandlers.SayHello);
});
await builder.Build().RunAsync();
```

**[Hosted app quick start →](hosted-app.md)**

:::

::::

## Which package do I need?

| Scenario | Package |
|----------|---------|
| Simple CLI tool, no DI needed | `Nullean.Argh` |
| App uses `IHost`, needs DI, logging, config | `Nullean.Argh.Hosting` |
| Building a shared middleware/parser library | `Nullean.Argh.Interfaces` |
