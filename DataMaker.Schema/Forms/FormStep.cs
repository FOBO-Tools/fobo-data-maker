using DataMaker.Schema.Layout;

namespace DataMaker.Schema.Forms;

/// <summary>
/// One step in a multi-step form (wizard). A form with a single step renders
/// as a flat form; multiple steps render as a wizard with Prev/Next.
/// Step transitions only advance when all fields referenced by the step pass validation.
/// </summary>
public sealed record FormStep
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string? Description { get; init; }
    public List<Section> Sections { get; init; } = new();
}
