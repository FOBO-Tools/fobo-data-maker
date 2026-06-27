namespace DataMaker.Schema.Import;

/// <summary>What kind of source an <see cref="ImportMapping"/> belongs to —
/// keeps PDF and spreadsheet mappings from colliding on the same fingerprint.</summary>
public enum ImportSourceKind
{
    Pdf,
    Spreadsheet,
}

/// <summary>
/// One wire in an import mapping: a source field/column name → a DataMaker
/// <see cref="DataMaker.Schema.Fields.FieldDefinition.Name"/>. The inbound
/// mirror of <c>OutegrationFieldMapping</c> (which runs the other way).
/// </summary>
public sealed record ImportFieldMapping(string SourceFieldName, string FormFieldName);

/// <summary>
/// A saved mapping from a specific source layout onto a DataMaker form. Keyed by
/// (<see cref="FormId"/>, <see cref="SourceKind"/>, <see cref="SourceFingerprint"/>)
/// so the next source with the same layout auto-applies without re-opening the
/// dialog. Install-specific operational state — never travels inside a form
/// schema or <c>.dmf</c> bundle.
/// </summary>
public sealed record ImportMapping
{
    public required string FormId { get; init; }

    public ImportSourceKind SourceKind { get; init; } = ImportSourceKind.Pdf;

    /// <summary>Stable hash of the source layout's field-name set.</summary>
    public required string SourceFingerprint { get; init; }

    public List<ImportFieldMapping> Mappings { get; init; } = new();

    public DateTimeOffset Updated { get; init; }
}
