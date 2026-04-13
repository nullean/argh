# From repo root: publish first, then record:
#   dotnet publish examples/XmlDocShowcase/XmlDocShowcase.csproj -c Release -o docs/vhs/publish
#   vhs < docs/vhs/xml-doc-showcase.tape
Output docs/assets/xml-doc-showcase-help.gif

Set FontSize 15
Set Width 1100
Set Height 620
Set Padding 28
Set Theme "Catppuccin Mocha"
Set TypingSpeed 25ms

Type "./docs/vhs/publish/XmlDocShowcase --help"
Enter
Sleep 3s

Type "./docs/vhs/publish/XmlDocShowcase docs --help"
Enter
Sleep 3s

Type "./docs/vhs/publish/XmlDocShowcase docs cross-refs --help"
Enter
Sleep 5s
