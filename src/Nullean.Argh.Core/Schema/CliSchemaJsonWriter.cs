using System.Globalization;
using System.Text;

namespace Nullean.Argh.Schema;

/// <summary>
/// Hand-rolled, write-only JSON emitter for <see cref="ArghCliSchemaDocument"/> (<c>__schema</c>).
/// The schema document is the only JSON producer in Argh, and it is never deserialized at runtime,
/// so a full serializer (reflection-based or source-generated) is unnecessary weight for AOT-published
/// apps: <c>System.Text.Json</c> alone adds several megabytes to a trimmed Native AOT binary even when
/// only a source-generated context is referenced. This writer produces the same 2-space indented,
/// camelCase JSON shape the previous <c>System.Text.Json</c>-based implementation did.
/// </summary>
internal sealed class CliSchemaJsonWriter
{
	private readonly StringBuilder _sb = new();
	private readonly Stack<bool> _isFirstInContainer = new();
	private int _depth;

	public static string Write(ArghCliSchemaDocument document)
	{
		var writer = new CliSchemaJsonWriter();
		writer.WriteDocument(document);
		return writer._sb.ToString();
	}

	private void WriteDocument(ArghCliSchemaDocument doc)
	{
		BeginObject();
		WriteNumber("schemaVersion", doc.SchemaVersion);
		WriteString("name", doc.Name);
		WriteString("version", doc.Version);
		WriteString("description", doc.Description);
		WriteStringArray("reservedMetaCommands", doc.ReservedMetaCommands);
		WriteObjectArray("globalOptions", doc.GlobalOptions, WriteParameter);
		WriteObject("rootDefault", doc.RootDefault, WriteDefaultHandler);
		WriteObjectArray("commands", doc.Commands, WriteCommand);
		WriteObjectArray("namespaces", doc.Namespaces, WriteNamespace);
		WriteStringArray("tags", doc.Tags);
		WriteNullableBool("requiresAuth", doc.RequiresAuth);
		WriteStringArray("authCommands", doc.AuthCommands);
		WriteObject("environment", doc.Environment, WriteEnvironment);
		EndObject();
	}

	private void WriteNamespace(CliNamespaceSchema ns)
	{
		WriteString("segment", ns.Segment);
		WriteString("summary", ns.Summary);
		WriteString("notes", ns.Notes);
		WriteObjectArray("options", ns.Options, WriteParameter);
		WriteObject("defaultCommand", ns.DefaultCommand, WriteDefaultHandler);
		WriteObjectArray("commands", ns.Commands, WriteCommand);
		WriteObjectArray("namespaces", ns.Namespaces, WriteNamespace);
	}

	private void WriteCommand(CliCommandSchema c)
	{
		WriteStringArray("path", c.Path);
		WriteString("name", c.Name);
		WriteString("summary", c.Summary);
		WriteString("notes", c.Notes);
		WriteString("usage", c.Usage);
		WriteStringArray("examples", c.Examples);
		WriteObjectArray("parameters", c.Parameters, WriteParameter);
		WriteStringArray("aliases", c.Aliases);
		WriteBool("hidden", c.Hidden, omitWhenFalse: true);
		WriteStringArray("tags", c.Tags);
		WriteDeprecated("deprecated", c.Deprecated);
		WriteObject("intent", c.Intent, WriteIntent);
		WriteObject("output", c.Output, WriteOutput);
		WriteBool("streaming", c.Streaming, omitWhenFalse: true);
		WriteBool("longRunning", c.LongRunning, omitWhenFalse: true);
	}

	private void WriteIntent(CliIntentSchema i)
	{
		WriteNullableBool("destructive", i.Destructive);
		WriteNullableBool("idempotent", i.Idempotent);
		WriteString("scope", i.Scope);
		WriteNullableBool("requiresConfirmation", i.RequiresConfirmation);
		WriteNullableBool("requiresAuth", i.RequiresAuth);
	}

	private void WriteOutput(CliOutputSchema o)
	{
		WriteStringArray("formats", o.Formats);
		WriteString("formatFlag", o.FormatFlag);
	}

	private void WriteDefaultHandler(CliDefaultHandlerSchema d)
	{
		WriteString("kind", d.Kind);
		WriteString("summary", d.Summary);
		WriteString("notes", d.Notes);
		WriteString("usage", d.Usage);
		WriteStringArray("examples", d.Examples);
		WriteObjectArray("parameters", d.Parameters, WriteParameter);
		WriteBool("hidden", d.Hidden, omitWhenFalse: true);
	}

	private void WriteParameter(CliParameterSchema p)
	{
		WriteString("role", p.Role);
		WriteString("name", p.Name);
		WriteString("shortName", p.ShortName);
		WriteString("type", p.Type);
		WriteBool("required", p.Required, omitWhenFalse: false);
		WriteString("summary", p.Summary);
		WriteString("defaultValue", p.DefaultValue);
		WriteBool("repeatable", p.Repeatable, omitWhenFalse: true);
		WriteString("separator", p.Separator);
		WriteStringArray("aliases", p.Aliases);
		WriteStringArray("enumValues", p.EnumValues);
		WriteString("elementType", p.ElementType);
		WriteBool("hidden", p.Hidden, omitWhenFalse: true);
		WriteBool("variadic", p.Variadic, omitWhenFalse: true);
		WriteDeprecated("deprecated", p.Deprecated);
		WriteObjectArray("validations", p.Validations, WriteConstraint);
	}

	private void WriteConstraint(CliConstraintSchema c)
	{
		WriteString("kind", c.Kind);
		WriteString("min", c.Min);
		WriteString("max", c.Max);
		WriteString("pattern", c.Pattern);
		WriteStringArray("values", c.Values);
	}

	/// <summary>
	/// Matches the previous <c>CliDeprecationJsonConverter</c>: <c>true</c> when no structured details
	/// are present, otherwise an object with only the non-null fields.
	/// </summary>
	private void WriteDeprecated(string name, CliDeprecationSchema? d)
	{
		if (d is null)
			return;

		WritePropertyName(name);
		if (d.Message is null && d.Since is null && d.RemovedIn is null)
		{
			_sb.Append("true");
			return;
		}

		BeginObject();
		WriteString("message", d.Message);
		WriteString("since", d.Since);
		WriteString("removedIn", d.RemovedIn);
		EndObject();
	}

	private void WriteEnvironment(CliEnvironmentSchema e)
	{
		WriteObjectArray("variables", e.Variables, WriteEnvVar);
		WriteObjectArray("configFiles", e.ConfigFiles, WriteConfigFile);
	}

	private void WriteEnvVar(CliEnvVarSchema v)
	{
		WriteString("name", v.Name);
		WriteString("description", v.Description);
		WriteBool("required", v.Required, omitWhenFalse: true);
		WriteString("defaultValue", v.DefaultValue);
	}

	private void WriteConfigFile(CliConfigFileSchema c)
	{
		WriteString("path", c.Path);
		WriteString("description", c.Description);
		WriteBool("required", c.Required, omitWhenFalse: true);
	}

	// --- Low-level writer primitives -------------------------------------------------------------

	private void BeginObject() => BeginContainer('{');

	private void EndObject() => EndContainer('}');

	private void BeginContainer(char open)
	{
		_sb.Append(open);
		_isFirstInContainer.Push(true);
		_depth++;
	}

	private void EndContainer(char close)
	{
		_depth--;
		var wasEmpty = _isFirstInContainer.Pop();
		if (!wasEmpty)
		{
			_sb.Append('\n');
			AppendIndent();
		}
		_sb.Append(close);
	}

	private void BeforeElement()
	{
		var isFirst = _isFirstInContainer.Pop();
		if (!isFirst)
			_sb.Append(',');
		_sb.Append('\n');
		AppendIndent();
		_isFirstInContainer.Push(false);
	}

	private void AppendIndent() => _sb.Append(' ', _depth * 2);

	private void WritePropertyName(string name)
	{
		BeforeElement();
		_sb.Append('"').Append(name).Append("\": ");
	}

	private void WriteString(string name, string? value)
	{
		if (value is null)
			return;

		WritePropertyName(name);
		WriteEscapedString(value);
	}

	private void WriteNumber(string name, int value)
	{
		WritePropertyName(name);
		_sb.Append(value.ToString(CultureInfo.InvariantCulture));
	}

	private void WriteBool(string name, bool value, bool omitWhenFalse)
	{
		if (omitWhenFalse && !value)
			return;

		WritePropertyName(name);
		_sb.Append(value ? "true" : "false");
	}

	private void WriteNullableBool(string name, bool? value)
	{
		if (value is null)
			return;

		WritePropertyName(name);
		_sb.Append(value.Value ? "true" : "false");
	}

	private void WriteStringArray(string name, string[]? values)
	{
		if (values is null)
			return;

		WritePropertyName(name);
		BeginContainer('[');
		foreach (var v in values)
		{
			BeforeElement();
			if (v is null)
				_sb.Append("null");
			else
				WriteEscapedString(v);
		}
		EndContainer(']');
	}

	private void WriteObject<T>(string name, T? value, Action<T> writeMembers)
		where T : class
	{
		if (value is null)
			return;

		WritePropertyName(name);
		BeginObject();
		writeMembers(value);
		EndObject();
	}

	private void WriteObjectArray<T>(string name, T[]? values, Action<T> writeMembers)
	{
		if (values is null)
			return;

		WritePropertyName(name);
		BeginContainer('[');
		foreach (var v in values)
		{
			BeforeElement();
			if (v is null)
			{
				_sb.Append("null");
				continue;
			}
			BeginObject();
			writeMembers(v);
			EndObject();
		}
		EndContainer(']');
	}

	private void WriteEscapedString(string value)
	{
		_sb.Append('"');
		foreach (var c in value)
		{
			switch (c)
			{
				case '"': _sb.Append("\\\""); break;
				case '\\': _sb.Append("\\\\"); break;
				case '\b': _sb.Append("\\b"); break;
				case '\f': _sb.Append("\\f"); break;
				case '\n': _sb.Append("\\n"); break;
				case '\r': _sb.Append("\\r"); break;
				case '\t': _sb.Append("\\t"); break;
				default:
					if (c < 0x20)
						_sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
					else
						_sb.Append(c);
					break;
			}
		}
		_sb.Append('"');
	}
}
