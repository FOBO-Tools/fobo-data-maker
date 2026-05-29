using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataMaker.Schema.Fields;

/// <summary>
/// JSON converter for <see cref="FieldDefinition.Kind"/>. Migrates pre-registry
/// forms that persisted the old <c>FieldKind</c> enum names ("Text", "LongText",
/// "MultiChoice", …) to the new lowercase kebab-case ids ("text", "long-text",
/// "multi-choice", …). Unknown values pass through untouched so third-party
/// type ids keep working.
///
/// <para>
/// Only the <b>read</b> path does migration; writes use the id verbatim so new
/// persisted JSON is always canonical. Once old forms are resaved, the legacy
/// mapping is dead weight — leaving it in is cheap insurance.
/// </para>
/// </summary>
public sealed class FieldKindJsonConverter : JsonConverter<string>
{
    private static readonly Dictionary<string, string> LegacyMap = new(StringComparer.Ordinal)
    {
        ["Text"]        = FieldTypes.Text,
        ["LongText"]    = FieldTypes.LongText,
        ["Number"]      = FieldTypes.Number,
        ["Decimal"]     = FieldTypes.Decimal,
        ["Money"]       = FieldTypes.Money,
        ["Date"]        = FieldTypes.Date,
        ["DateTime"]    = FieldTypes.DateTime,
        ["Boolean"]     = FieldTypes.Boolean,
        ["Choice"]      = FieldTypes.Choice,
        ["MultiChoice"] = FieldTypes.MultiChoice,
        ["List"]        = FieldTypes.List,
        ["Email"]       = FieldTypes.Email,
        ["Phone"]       = FieldTypes.Phone,
        ["Url"]         = FieldTypes.Url,
        ["Geo"]         = FieldTypes.Geo,
        ["Image"]       = FieldTypes.Image,
        ["Attachment"]  = FieldTypes.Attachment,
        ["Relation"]    = FieldTypes.Relation,
    };

    public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var raw = reader.GetString() ?? "";
        return LegacyMap.TryGetValue(raw, out var migrated) ? migrated : raw;
    }

    public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value);
}
