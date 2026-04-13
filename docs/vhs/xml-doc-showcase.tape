# Run from repository root: vhs < docs/vhs/xml-doc-showcase.tape
Output docs/assets/xml-doc-showcase-help.gif

Set FontSize 15
Set Width 1100
Set Height 580
Set Padding 28
Set Theme "Catppuccin Mocha"

Type "dotnet run --project examples/XmlDocShowcase -- --help"
Enter
Sleep 4s

Type "dotnet run --project examples/XmlDocShowcase -- welcome --help"
Enter
Sleep 6s
