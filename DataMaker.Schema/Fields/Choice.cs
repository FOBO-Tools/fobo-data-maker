namespace DataMaker.Schema.Fields;

/// <summary>Single option in a Choice / MultiChoice field.</summary>
public sealed record Choice
{
    public required string Value { get; init; }
    public required string Label { get; init; }
    public string? Color { get; init; }
    public string? Icon { get; init; }
}
