using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Schema.Fields;

/// <summary>
/// A geographic point. Serializes as <c>{"lat":&lt;n&gt;,"lng":&lt;n&gt;,"formattedAddress":&lt;str?&gt;}</c>
/// — a proper JSON object so JSONB path operators (Postgres) and
/// <c>json_extract</c> (SQLite) can reach individual members directly at
/// query time. The storage layer stores the object as-is in the record's
/// <c>data</c> column; no stringification.
///
/// <para>
/// <see cref="FormattedAddress"/> is an optional human-readable address
/// for the point. It is the form renderer's responsibility to populate
/// it (e.g. via reverse-geocoding when the user picks a location). The
/// display layer falls back to a localised "Unknown address" string when
/// it's null, so a record with only lat/lng still renders sensibly.
/// </para>
/// </summary>
[JsonConverter(typeof(GeoJsonConverter))]
public readonly record struct Geo(double Lat, double Lng, string? FormattedAddress = null);

/// <summary>
/// Strict JSON shape for <see cref="Geo"/>: exactly <c>{"lat":…, "lng":…}</c>
/// with numeric values. Anything else throws, which is what we want — a
/// malformed default surfaces as a design-time issue instead of silently
/// decoding to a broken point.
/// </summary>
public sealed class GeoJsonConverter : JsonConverter<Geo>
{
    public override Geo Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
            throw new JsonException("Geo must be a JSON object with 'lat' and 'lng'.");

        double? lat = null, lng = null;
        string? formattedAddress = null;

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                throw new JsonException("Unexpected token in Geo object.");

            var name = reader.GetString();
            reader.Read();

            switch (name)
            {
                case "lat":              lat              = reader.GetDouble();                                               break;
                case "lng":              lng              = reader.GetDouble();                                               break;
                case "formattedAddress": formattedAddress = reader.TokenType == JsonTokenType.Null ? null : reader.GetString(); break;
                default:                 reader.Skip();                                                                       break;  // tolerate extras (e.g. future altitude)
            }
        }

        if (lat is null || lng is null)
            throw new JsonException("Geo is missing 'lat' or 'lng'.");
        return new Geo(lat.Value, lng.Value, formattedAddress);
    }

    public override void Write(Utf8JsonWriter writer, Geo value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteNumber("lat", value.Lat);
        writer.WriteNumber("lng", value.Lng);
        if (value.FormattedAddress is not null)
            writer.WriteString("formattedAddress", value.FormattedAddress);
        writer.WriteEndObject();
    }
}
