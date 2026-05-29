namespace DataMaker.Schema.Fields.Predefined;

/// <summary>
/// A pre-configured <see cref="FieldDefinition"/> template the user can add to
/// a form with one click — echoes the old Chivan.Leads.Fields.PredefinedFields
/// concept but without inheritance: here, the preset is a factory that emits
/// a fresh <c>FieldDefinition</c> record with Kind + Label + Description +
/// option blocks + validation rules pre-filled.
///
/// <para>
/// The <see cref="Create"/> factory takes the set of names already on the
/// form so it can auto-suffix when adding a second instance of the same preset
/// (<c>email</c> → <c>email_2</c>).
/// </para>
/// </summary>
public sealed record PredefinedField(
    string Id,
    string Category,
    string Label,
    string Description,
    string Icon,
    Func<IReadOnlyCollection<string>, FieldDefinition> Create);
