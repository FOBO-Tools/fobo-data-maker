using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DataMaker.Schema.Fields;

namespace DataMaker.Schema.Forms;

/// <summary>
/// Computes a stable SHA-256 hash over the parts of a <see cref="Form"/> that
/// constitute its *structure* — the field set + validation rules + kind-
/// specific option blocks. Used by the form-save pipeline to detect when an
/// edit warrants bumping <see cref="Form.SchemaVersion"/> (#18b: implicit
/// form versioning, see docs/PLAN-STORAGE-V2.md).
///
/// <para>
/// <b>Counted as structural</b> — change bumps the version:
/// Id, Name, Kind, Required, Validation rules, kind-specific option blocks
/// (Text / Number / Money / Choice / Relation / Attachment / Date — Choice
/// option lists matter because adding/removing a choice changes what a
/// "valid" value is).
/// </para>
///
/// <para>
/// <b>Version-neutral</b> — change does NOT bump:
/// Label, Description, Placeholder, Autocomplete, DefaultValue,
/// CalculatedExpression body, VisibleWhen body, Messages, Style,
/// IsPrimaryDisplay, Indexed, plus everything in <see cref="Form"/>
/// outside <see cref="Form.Fields"/> (Steps / Layout, FormStyle, etc.).
/// Reordering fields in the list is also neutral — fields are sorted by
/// <see cref="FieldDefinition.Id"/> before hashing.
/// </para>
/// </summary>
public static class FormStructuralHash
{
    private static readonly JsonSerializerOptions Json = new()
    {
        // Deterministic property order: declared field order on the
        // projection record below is what STJ emits. PropertyNamingPolicy
        // stays default (PascalCase) — only the hash needs to be stable,
        // wire compat isn't a concern here.
        WriteIndented = false,
    };

    public static string Compute(Form form) => Compute(form.Fields);

    public static string Compute(IEnumerable<FieldDefinition> fields)
    {
        var structural = fields
            .Select(Project)
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ToList();
        var bytes  = JsonSerializer.SerializeToUtf8Bytes(structural, Json);
        var digest = SHA256.HashData(bytes);
        return Convert.ToHexString(digest).ToLowerInvariant();
    }

    private static StructuralFieldProjection Project(FieldDefinition f) => new(
        Id:         f.Id,
        Name:       f.Name,
        Kind:       f.Kind,
        Required:   f.Required,
        Validation: f.Validation,
        Text:       f.Text,
        Number:     f.Number,
        Money:      f.Money,
        Choice:     f.Choice,
        Relation:   f.Relation,
        Attachment: f.Attachment,
        Date:       f.Date);

    /// <summary>
    /// Hash input — only the structural slice of a field. Order of
    /// properties here defines the hash input layout; don't reorder
    /// without bumping the algorithm version (would invalidate every
    /// archived hash).
    /// </summary>
    private sealed record StructuralFieldProjection(
        string                  Id,
        string                  Name,
        string                  Kind,
        bool                    Required,
        IReadOnlyList<DataMaker.Schema.Validation.ValidationRule> Validation,
        TextOptions?            Text,
        NumberOptions?          Number,
        MoneyOptions?           Money,
        ChoiceOptions?          Choice,
        RelationOptions?        Relation,
        AttachmentOptions?      Attachment,
        DateOptions?            Date);
}
