namespace DataMaker.Schema.Fields;

/// <summary>
/// Canonical string identifiers for the built-in field types. These are the
/// values serialized into <see cref="FieldDefinition.Kind"/> in form JSON and
/// looked up in <see cref="FieldTypeRegistry"/>.
///
/// <para>
/// Identifiers follow lowercase kebab-case (<c>long-text</c>, <c>multi-choice</c>) —
/// the convention used by HTML input types and most form-builder ecosystems.
/// Third-party field types should use a scoped prefix (e.g. <c>com.acme.signature</c>)
/// to avoid collisions with future built-ins.
/// </para>
///
/// <para>
/// Legacy forms persisted the old <c>FieldKind</c> enum names (<c>"Text"</c>,
/// <c>"LongText"</c>, …). <see cref="FieldKindJsonConverter"/> remaps those on
/// read, so old JSON still loads; new writes use the lowercase form.
/// </para>
/// </summary>
public static class FieldTypes
{
    public const string Text        = "text";
    public const string LongText    = "long-text";
    public const string RichText    = "rich-text";
    public const string Number      = "number";
    public const string Decimal     = "decimal";
    public const string Money       = "money";
    public const string Date        = "date";
    public const string DateTime    = "datetime";
    public const string Boolean     = "boolean";
    public const string Choice      = "choice";
    public const string MultiChoice = "multi-choice";
    public const string List        = "list";
    public const string Email       = "email";
    public const string Phone       = "phone";
    public const string Url         = "url";
    public const string Geo         = "geo";
    public const string Image       = "image";
    public const string Attachment  = "attachment";
    public const string Relation    = "relation";
}
