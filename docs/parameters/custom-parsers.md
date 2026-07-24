---
title: Custom parsers
---

# Custom parsing — `IArgumentParser<T>`

For types with no built-in support, implement `IArgumentParser<T>` and annotate the parameter with `[ArgumentParser]`.

## Implementing a parser

```csharp
public class SemVerParser : IArgumentParser<SemVer>
{
    public static bool TryParse(string value, out SemVer result) =>
        SemVer.TryParse(value, out result);
}
```

## Using the parser

Annotate the parameter with `[ArgumentParser(typeof(...))]`:

```csharp
public static Task<int> Release([ArgumentParser(typeof(SemVerParser))] SemVer version) { … }
// myapp release 1.2.3
```

## Package reference

`IArgumentParser<T>` is defined in `Nullean.Argh.Interfaces`. If you are building a shared library of custom parsers for reuse across multiple CLI apps, reference the interfaces package directly:

```xml
<ItemGroup>
  <PackageReference Include="Nullean.Argh.Interfaces" />
</ItemGroup>
```

For normal apps that reference `Nullean.Argh` or `Nullean.Argh.Hosting`, the interfaces are already available transitively.
