# Fuzzy matching

Typos produce actionable errors with the correct qualified path and a `--help` suggestion. There is no silent no-match.

## Example

```
$ myapp stoarge list
Error: unknown command or namespace 'stoarge'. Did you mean 'storage'?

Run 'myapp storage --help' for usage.
Run 'myapp --help' for usage.
```

## Nested namespaces

Inside a namespace, the suggestion includes the full path (`storage blob upload`, not just `upload`):

```
$ myapp storage blop upload
Error: unknown command or namespace 'blop'. Did you mean 'blob'?

Run 'myapp storage blob --help' for usage.
Run 'myapp storage --help' for usage.
```

## How it works

The generator emits the full command tree at build time. When a token doesn't match any known command or namespace at a given level, the runtime computes edit distance against all valid candidates.

If the closest match is within a reasonable threshold, it is suggested in the error message.
