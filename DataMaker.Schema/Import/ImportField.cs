namespace DataMaker.Schema.Import;

/// <summary>
/// A source field/column being imported — one cell-or-widget worth of data,
/// independent of where it came from (a PDF AcroForm field, a CSV/Excel column,
/// a Google Sheet column). The mapper wires these onto DataMaker form fields.
/// </summary>
public sealed record ImportField(
    string          Name,
    string?         Value,        // text value, or a recovered signature data URI
    ImportFieldKind Kind,
    bool            IsSignature);

/// <summary>Source field type, normalised across importers. Spreadsheet columns
/// are always <see cref="Text"/> (cells are strings); PDF widgets carry their
/// real <c>/FT</c> kind.</summary>
public enum ImportFieldKind
{
    Unknown,
    Text,
    Checkbox,
    Choice,
    Signature,
}
