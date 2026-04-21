using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using System.Text.Json.Schema;
using Nullean.Argh.Schema;

namespace Nullean.Argh.SchemaExport;

/// <summary>Exports the JSON Schema for the Argh CLI schema document format.</summary>
internal static class SchemaExportCommand
{
    /// <summary>Writes the JSON Schema for ArghCliSchemaDocument to stdout or a file.</summary>
    /// <param name="out">-o, Output file path (.json). Writes to stdout when omitted.</param>
    public static int Run([FileExtensions(Extensions = ".json")] FileInfo? @out = null)
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver()
        };
        var schema = JsonSchemaExporter.GetJsonSchemaAsNode(options, typeof(ArghCliSchemaDocument));
        var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });

        if (@out is not null)
            File.WriteAllText(@out.FullName, json);
        else
            Console.Write(json);

        return 0;
    }
}
