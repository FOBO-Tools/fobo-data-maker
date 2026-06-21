using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Schema.Fields;

/// <summary>
/// A captured signature or set of initials. Backs the <see cref="FieldTypes.Signature"/>
/// and <see cref="FieldTypes.Initials"/> field kinds.
///
/// <para>
/// <b>Ink-drawn first (today).</b> The signer draws on an ink surface (the Uno
/// app's pointer canvas, the web canvas pad) and we rasterise the strokes to a
/// transparent PNG. That PNG rides the same storage shapes as
/// <see cref="ImageRef"/>: an inline <see cref="DataUri"/> for portability, or
/// an owned cloud <see cref="Url"/> + <see cref="Hash"/> when uploaded. On
/// surfaces with no drawing primitive (Terminal) the signer may instead pick a
/// signature image file (→ <see cref="DataUri"/>/<see cref="Url"/>) or type
/// their name (→ <see cref="TypedName"/>).
/// </para>
///
/// <para>
/// <b>Digital-signature upgrade (later, additive).</b> The <see cref="Pkcs7"/>,
/// <see cref="SignerCertSubject"/> and <see cref="SignedAt"/> slots are present
/// from day one so a future PKI / PKCS#7 flow is a purely additive write — an
/// ink-only ref carries them as null. Existing ink-signed records keep loading
/// unchanged; the digital layer just populates the extra fields. See the
/// "DataMaker PDF — signature + paraaf field plan" design note for the upgrade
/// caveats (seal-last ordering, trust UI, cert source).
/// </para>
///
/// <para>
/// At least one of <see cref="DataUri"/>, <see cref="Url"/> or
/// <see cref="TypedName"/> must be present — the converter rejects refs with
/// none. Reads tolerate a legacy bare-string shape: a <c>data:</c> URI becomes
/// <see cref="DataUri"/>, anything else becomes <see cref="TypedName"/>.
/// </para>
/// </summary>
[JsonConverter(typeof(SignatureRefJsonConverter))]
public readonly record struct SignatureRef(
    string? DataUri   = null,
    string? Mime      = null,
    long?   SizeBytes = null,
    string? Url       = null,
    string? Hash      = null,
    bool    Owned     = false,
    string? TypedName = null,
    // ── digital-signature upgrade path (all null today = ink/typed only) ──
    byte[]?         Pkcs7             = null,
    string?         SignerCertSubject = null,
    DateTimeOffset? SignedAt          = null)
{
    /// <summary>True when the ref carries no drawn image, no URL and no typed name.</summary>
    [JsonIgnore]
    public bool IsBlank =>
        string.IsNullOrEmpty(DataUri) &&
        string.IsNullOrEmpty(Url) &&
        string.IsNullOrEmpty(TypedName);
}

/// <summary>
/// JSON shape for <see cref="SignatureRef"/>. Accepts: bare string (legacy —
/// a <c>data:</c> URI or a typed name), or an object with any of the keys
/// below. <see cref="SignatureRef.Pkcs7"/> serialises as base64. Refs with
/// neither <c>dataUri</c>, <c>url</c> nor <c>typedName</c> throw.
/// </summary>
public sealed class SignatureRefJsonConverter : JsonConverter<SignatureRef>
{
    public override SignatureRef Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var legacy = reader.GetString();
            if (string.IsNullOrEmpty(legacy))
                throw new JsonException("SignatureRef string shape can't be empty.");
            return legacy.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? new SignatureRef(DataUri: legacy)
                : new SignatureRef(TypedName: legacy);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("SignatureRef must be a JSON object or a legacy string.");

        string?         dataUri   = null;
        string?         mime      = null;
        long?           size      = null;
        string?         url       = null;
        string?         hash      = null;
        bool            owned     = false;
        string?         typedName = null;
        byte[]?         pkcs7     = null;
        string?         certSubj  = null;
        DateTimeOffset? signedAt  = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in SignatureRef object.");

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "dataUri":           dataUri   = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();    break;
                case "mime":              mime      = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();    break;
                case "sizeBytes":         size      = reader.TokenType == JsonTokenType.Null ? null : reader.GetInt64();     break;
                case "url":               url       = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();    break;
                case "hash":              hash      = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();    break;
                case "owned":             owned     = reader.TokenType != JsonTokenType.Null && reader.GetBoolean();         break;
                case "typedName":         typedName = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();    break;
                case "pkcs7":             pkcs7     = reader.TokenType == JsonTokenType.Null ? null : reader.GetBytesFromBase64(); break;
                case "signerCertSubject": certSubj  = reader.TokenType == JsonTokenType.Null ? null : reader.GetString();    break;
                case "signedAt":          signedAt  = reader.TokenType == JsonTokenType.Null ? null : reader.GetDateTimeOffset(); break;
                default:                  reader.Skip(); break;  // tolerate extras
            }
        }

        if (string.IsNullOrEmpty(dataUri) && string.IsNullOrEmpty(url) && string.IsNullOrEmpty(typedName))
            throw new JsonException("SignatureRef is missing 'dataUri', 'url' and 'typedName' — must carry at least one.");
        return new SignatureRef(dataUri, mime, size, url, hash, owned, typedName, pkcs7, certSubj, signedAt);
    }

    public override void Write(Utf8JsonWriter writer, SignatureRef value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        if (value.DataUri           is not null) writer.WriteString("dataUri",           value.DataUri);
        if (value.Mime              is not null) writer.WriteString("mime",              value.Mime);
        if (value.SizeBytes         is not null) writer.WriteNumber("sizeBytes",         value.SizeBytes.Value);
        if (value.Url               is not null) writer.WriteString("url",               value.Url);
        if (value.Hash              is not null) writer.WriteString("hash",              value.Hash);
        if (value.Owned)                         writer.WriteBoolean("owned",            value.Owned);
        if (value.TypedName         is not null) writer.WriteString("typedName",         value.TypedName);
        if (value.Pkcs7             is not null) writer.WriteBase64String("pkcs7",       value.Pkcs7);
        if (value.SignerCertSubject is not null) writer.WriteString("signerCertSubject", value.SignerCertSubject);
        if (value.SignedAt          is not null) writer.WriteString("signedAt",          value.SignedAt.Value);
        writer.WriteEndObject();
    }
}
