using Nullean.Argh;
using Nullean.Argh.SchemaExport;

var app = new ArghApp();
app.MapRoot(SchemaExportCommand.Run);
return await app.RunAsync(args);
