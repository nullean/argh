# Advanced topics

Configuration patterns for complex CLIs with shared state across commands.

## In this section

[**Global options**](global-options.md) - Share flags across all commands with `UseGlobalOptions<T>()`. Parsed before routing, available everywhere.

[**Namespace options**](namespace-options.md) - Scope options to a namespace and its children with `UseNamespaceOptions<T>()`. Must inherit the parent options type.
