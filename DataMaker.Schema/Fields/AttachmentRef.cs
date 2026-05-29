using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Schema.Fields;

/// <summary>
/// A captured attachment (any file). Parallel of <see cref="ImageRef"/> —
/// same three storage shapes (inline data URI / owned URL / external
/// URL). Distinct type because semantics differ at render-time
/// (attachment cells just show metadata; image cells render inline).
///
/// <para>
/// At least one of <see cref="DataUri"/> or <see cref="Url"/> must be
/// present. Reads stay tolerant of the legacy bare-string shape (just
/// a data URI) for back-compat with pre-URL records.
/// </para>
/// </summary>
[JsonConverter(typeof(AttachmentRefJsonConverter))]
public readonly record struct AttachmentRef(
    string? DataUri   = null,
    string? FileName  = null,
    string? Mime      = null,
    long?   SizeBytes = null,
    string? Url       = null,
    string? Hash      = null,
    bool    Owned     = false);

/// <summary>
/// JSON shape for <see cref="AttachmentRef"/>. Accepts: bare string
/// (legacy data URI), inline-only object, owned-URL object, or
/// external-URL object. Refs with neither <c>dataUri</c> nor <c>url</c>
/// throw.
/// </summary>
public sealed class AttachmentRefJsonConverter : JsonConverter<AttachmentRef>
{
    public override AttachmentRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var legacy = reader.GetString();
            if (string.IsNullOrEmpty(legacy))
                throw new JsonException("AttachmentRef string shape can't be empty.");
            return new AttachmentRef(DataUri: legacy);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("AttachmentRef must be a JSON object or a legacy data-URI string.");

        string? dataUri  = null;
        string? fileName = null;
        string? mime     = null;
        long?   size     = null;
        string? url      = null;
        string? hash     = null;
        bool    owned    = false;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in AttachmentRef object.");

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "dataUri":   dataUri  = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "fileName":  fileName = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "mime":      mime     = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "sizeBytes": size     = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt64();  break;
                case "url":       url      = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "hash":      hash     = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                case "owned":     owned    = reader.TokenType != JsonTokenType.Null && reader.GetBoolean();      break;
                default:          reader.Skip(); break;
            }
        }

        if (string.IsNullOrEmpty(dataUri) && string.IsNullOrEmpty(url))
            throw new JsonException("AttachmentRef is missing both 'dataUri' and 'url' — must carry at least one.");
        return new AttachmentRef(dataUri, fileName, mime, size, url, hash, owned);
    }

    public override void Write(Utf8JsonWriter writer, AttachmentRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.DataUri   is not null) writer.WriteString("dataUri",  value.DataUri);
        if (value.FileName  is not null) writer.WriteString("fileName", value.FileName);
        if (value.Mime      is not null) writer.WriteString("mime",     value.Mime);
        if (value.SizeBytes is not null) writer.WriteNumber("sizeBytes", value.SizeBytes.Value);
        if (value.Url       is not null) writer.WriteString("url",      value.Url);
        if (value.Hash      is not null) writer.WriteString("hash",     value.Hash);
        if (value.Owned)                 writer.WriteBoolean("owned",   value.Owned);
        writer.WriteEndObject();
    }
}
