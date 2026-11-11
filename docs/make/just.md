# just

`__wrappers sync` writes `.make/make.just`, a justfile with one recipe per target. Import it from the justfile:

```text
# justfile
import? '.make/make.just'
```

The `?` makes a checkout without the file still parse.

```bash
just test --filter Unit
just pkg-generate
just --list
```

To write only this file:

```bash
./build.cs __wrappers sync --only just
```

## The generated recipes

```text
# Pack the NuGet packages
[positional-arguments]
@pkg-generate *args:
    dotnet run build.cs -- pkg generate "$@"
```

- `[positional-arguments]` passes each argument as its own word, so `just test --filter "A B"` keeps `A B` together.
- The target's description becomes the recipe's comment, and `just --list` shows it.
- The first recipe in a justfile is its default. Put your own default recipe above the `import?`, or a bare `just`
  runs the first target.

See [Wrappers](wrappers.md) for the options and the task names.
