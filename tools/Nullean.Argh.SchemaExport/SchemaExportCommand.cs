using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;
using Nullean.Argh.Schema;

namespace Nullean.Argh.SchemaExport;

/// <summary>Exports the JSON Schema for the Argh CLI schema document format.</summary>
internal static class SchemaExportCommand
{
	/// <summary>Writes the JSON Schema for ArghCliSchemaDocument to stdout or a file.</summary>
	/// <param name="out">-o, Output file path (.json). Writes to stdout when omitted.</param>
	public static int Run([FileExtensions(Extensions = ".json")] FileInfo? @out = null)
	{
		var serializerOptions = new JsonSerializerOptions
		{
			PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
			TypeInfoResolver = new DefaultJsonTypeInfoResolver()
		};

		var exportOptions = new JsonSchemaExporterOptions
		{
			TransformSchemaNode = static (ctx, schema) =>
			{
				if (schema is not JsonObject obj || ctx.PropertyInfo is null)
					return schema;

				var declaringType = ctx.PropertyInfo.DeclaringType?.Name;
				var propName = ctx.PropertyInfo.Name;

				// Fix schemaVersion: integer-only with const 1 (web defaults add string/pattern noise)
				if (declaringType == nameof(ArghCliSchemaDocument) && propName == "schemaVersion")
				{
					obj.Remove("pattern");
					obj["type"] = "integer";
					obj["const"] = 1;
					return obj;
				}

				// Add enum constraints to the string fields that the meta-schema closes
				var enumValues = (declaringType, propName) switch
				{
					(nameof(CliParameterSchema), "role") =>
						["flag", "positional", "confirmationSkip", "dryRun"],
					(nameof(CliParameterSchema), "type") =>
						["string", "integer", "number", "boolean", "array", "enum"],
					(nameof(CliParameterSchema), "elementType") =>
						["string", "integer", "number", "boolean"],
					(nameof(CliConstraintSchema), "kind") =>
						["range", "timeSpanRange", "length", "count", "regex", "allowed", "denied",
						 "email", "url", "uriScheme", "fileExtensions", "existing", "nonExisting",
						 "rejectSymbolicLinks"],
					(nameof(CliIntentSchema), "scope") =>
						["file", "directory", "global"],
					(nameof(CliDefaultHandlerSchema), "kind") =>
						["root", "namespace"],
					_ => (string[]?)null
				};

				if (enumValues is not null)
					obj["enum"] = new JsonArray(enumValues.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());

				return obj;
			}
		};

		var schema = JsonSchemaExporter.GetJsonSchemaAsNode(serializerOptions, typeof(ArghCliSchemaDocument), exportOptions);

		// Strip ["T","null"] → "T" and remove nullable fields from required arrays
		StripNullablesAndFixRequired(schema);
		// JsonSchemaExporter skips TransformSchemaNode for types with [JsonConverter]; fix deprecated here
		FixDeprecatedProperties(schema);

		var json = JsonSerializer.Serialize(schema, new JsonSerializerOptions { WriteIndented = true });

		if (@out is not null)
			File.WriteAllText(@out.FullName, json);
		else
			Console.Write(json);

		return 0;
	}

	/// <summary>
	/// Recursively strips null from type arrays and removes nullable properties from required lists.
	/// Run after schema generation so we can identify which fields were nullable before stripping.
	/// </summary>
	private static void StripNullablesAndFixRequired(JsonNode? node)
	{
		if (node is JsonArray arr)
		{
			foreach (var item in arr)
				StripNullablesAndFixRequired(item);
			return;
		}

		if (node is not JsonObject obj)
			return;

		// Identify nullable property names before stripping so we can remove them from required
		var nullableProps = new HashSet<string>();
		if (obj["properties"] is JsonObject props)
		{
			foreach (var (key, val) in props)
			{
				if (IsNullableSchemaNode(val))
					nullableProps.Add(key);
			}
		}

		// Remove nullable fields from the required array
		if (nullableProps.Count > 0 && obj["required"] is JsonArray req)
		{
			for (var i = req.Count - 1; i >= 0; i--)
			{
				if (req[i]?.GetValue<string>() is { } f && nullableProps.Contains(f))
					req.RemoveAt(i);
			}
			if (req.Count == 0)
				obj.Remove("required");
		}

		// Strip "null" from this node's own type
		StripNullType(obj);

		// Remove "default": null (noise from JsonSchemaExporter for nullable optionals).
		// JsonNode stores JSON null as a C# null ref, so check ContainsKey + null ref.
		if (obj.ContainsKey("default") && obj["default"] is null)
			obj.Remove("default");

		// Recurse into all child nodes
		foreach (var (_, val) in obj)
			StripNullablesAndFixRequired(val);
	}

	/// <summary>
	/// Replaces every "deprecated" property schema with the cli-schema oneOf (bool true | details object).
	/// JsonSchemaExporter bypasses TransformSchemaNode for [JsonConverter]-annotated types, so this
	/// post-processing step is the reliable path for that particular property.
	/// </summary>
	private static void FixDeprecatedProperties(JsonNode? node)
	{
		if (node is JsonObject obj)
		{
			if (obj["properties"] is JsonObject props)
			{
				foreach (var key in props.Select(p => p.Key).ToList())
				{
					if (key == "deprecated")
						props["deprecated"] = BuildDeprecatedOneOf();
					else
						FixDeprecatedProperties(props[key]);
				}
			}
			foreach (var (k, v) in obj)
			{
				if (k != "properties")
					FixDeprecatedProperties(v);
			}
		}
		else if (node is JsonArray arr)
		{
			foreach (var item in arr)
				FixDeprecatedProperties(item);
		}
	}

	private static JsonObject BuildDeprecatedOneOf() =>
		new()
		{
			["oneOf"] = new JsonArray
			{
				new JsonObject { ["type"] = "boolean", ["const"] = true },
				new JsonObject
				{
					["type"] = "object",
					["properties"] = new JsonObject
					{
						["message"] = new JsonObject { ["type"] = "string" },
						["since"] = new JsonObject { ["type"] = "string" },
						["removedIn"] = new JsonObject { ["type"] = "string" }
					}
				}
			}
		};

	private static bool IsNullableSchemaNode(JsonNode? node)
	{
		if (node is not JsonObject obj) return false;
		if (obj["type"] is JsonArray typeArr)
			return typeArr.Any(t => t?.GetValue<string>() == "null");
		// JsonSchemaExporter emits {"default": null} (C# null ref, not JsonValue) for converter-backed nullables
		return obj.ContainsKey("default") && obj["default"] is null;
	}

	private static void StripNullType(JsonObject obj)
	{
		if (obj["type"] is not JsonArray typeArr) return;
		var nonNull = typeArr.Select(t => t?.GetValue<string>()).Where(t => t != "null").ToList();
		if (nonNull.Count == 1)
			obj["type"] = JsonValue.Create(nonNull[0]);
		else if (nonNull.Count > 1)
			obj["type"] = new JsonArray(nonNull.Select(t => (JsonNode?)JsonValue.Create(t)).ToArray());
		else
			obj.Remove("type");
	}
}
