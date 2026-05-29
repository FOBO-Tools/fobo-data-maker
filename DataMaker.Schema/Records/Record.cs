namespace DataMaker.Schema.Records;

/// <summary>
/// A single saved record for a form. <see cref="Values"/> holds per-field data
/// keyed by <c>FieldDefinition.Name</c>; the storage layer serializes Values
/// to JSONB and projects each key into a generated column.
/// </summary>
public sealed record Record
{
    public required string Id { get; init; }
    public required string FormId { get; init; }
    public int SchemaVersion { get; init; }

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }

    public Dictionary<string, object?> Values { get; init; } = new();
}
