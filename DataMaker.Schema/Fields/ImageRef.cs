using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Schema.Fields;

/// <summary>
/// A captured image. Three storage shapes, all serialise as a JSON object:
///
/// <list type="bullet">
///   <item><b>Inline data URI</b> — <c>{"dataUri":"data:image/png;base64,…", …}</c>.
///   Legacy shape; still produced by older clients + by the <c>.dmf</c>
///   exporter for portability. All record-value writes from new code now
///   prefer the URL shape so submission envelopes stay text-only.</item>
///   <item><b>Owned URL</b> — <c>{"url":"https://…","hash":"…","owned":true, …}</c>.
///   Bytes live in FOBO Cloud (private bucket); the URL is a pre-signed
///   GET. Hash is SHA-256 hex of the bytes the URL points at.</item>
///   <item><b>External URL</b> — <c>{"url":"https://…","owned":false, …}</c>.
///   User pasted a CDN link; we don't host the bytes, we don't enforce
///   the hash (may be null), the URL fetches as-is from wherever.</item>
/// </list>
///
/// <para>
/// At least one of <see cref="DataUri"/> or <see cref="Url"/> must be
/// present — the JSON converter rejects refs with neither. Reads stay
/// tolerant of the legacy bare-string shape (just a data URI): it gets
/// wrapped into <c>ImageRef(DataUri: …)</c> with no metadata, so old
/// records keep loading without a migration.
/// </para>
/// </summary>
[JsonConverter(typeof(ImageRefJsonConverter))]
public readonly record struct ImageRef(
    string? DataUri   = null,
    string? FileName  = null,
    string? Mime      = null,
    long?   SizeBytes = null,
    string? Url       = null,
    string? Hash      = null,
    bool    Owned     = false);

/// <summary>
/// JSON shape for <see cref="ImageRef"/>. Accepts: bare string (legacy
/// data URI), inline-only object, owned-URL object, or external-URL
/// object. Refs with neither <c>dataUri</c> nor <c>url</c> throw.
/// </summary>
public sealed class ImageRefJsonConverter : JsonConverter<ImageRef>
{
    public override ImageRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var legacy = reader.GetString();
            if (string.IsNullOrEmpty(legacy))
                throw new JsonException("ImageRef string shape can't be empty.");
            return new ImageRef(DataUri: legacy);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("ImageRef must be a JSON object or a legacy data-URI string.");

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
                throw new JsonException("Unexpected token in ImageRef object.");

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
                default:          reader.Skip(); break;  // tolerate extras
            }
        }

        if (string.IsNullOrEmpty(dataUri) && string.IsNullOrEmpty(url))
            throw new JsonException("ImageRef is missing both 'dataUri' and 'url' — must carry at least one.");
        return new ImageRef(dataUri, fileName, mime, size, url, hash, owned);
    }

    public override void Write(Utf8JsonWriter writer, ImageRef value, JsonSerializerOptions options)
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
