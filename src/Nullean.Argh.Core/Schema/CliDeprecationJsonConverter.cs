using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nullean.Argh.Schema;

/// <summary>
/// Serializes <see cref="CliDeprecationSchema"/> as <c>true</c> when no structured details are present,
/// or as an object with non-null fields when details exist. Matches the cli-schema v1 deprecation oneOf.
/// </summary>
internal sealed class CliDeprecationJsonConverter : JsonConverter<CliDeprecationSchema>
{
	public override CliDeprecationSchema? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
	{
		if (reader.TokenType == JsonTokenType.True)
			return new CliDeprecationSchema();

		if (reader.TokenType == JsonTokenType.Null)
			return null;

		string? message = null, since = null, removedIn = null;
		while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
		{
			if (reader.TokenType != JsonTokenType.PropertyName)
				continue;
			var prop = reader.GetString();
			reader.Read();
			switch (prop)
			{
				case "message": message = reader.GetString(); break;
				case "since": since = reader.GetString(); break;
				case "removedIn": removedIn = reader.GetString(); break;
				default: reader.Skip(); break;
			}
		}
		return new CliDeprecationSchema(message, since, removedIn);
	}

	public override void Write(Utf8JsonWriter writer, CliDeprecationSchema value, JsonSerializerOptions options)
	{
		if (value.Message is null && value.Since is null && value.RemovedIn is null)
		{
			writer.WriteBooleanValue(true);
			return;
		}
		writer.WriteStartObject();
		if (value.Message is not null) writer.WriteString("message", value.Message);
		if (value.Since is not null) writer.WriteString("since", value.Since);
		if (value.RemovedIn is not null) writer.WriteString("removedIn", value.RemovedIn);
		writer.WriteEndObject();
	}
}
