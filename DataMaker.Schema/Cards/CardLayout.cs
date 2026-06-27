using DataMaker.Schema.Layout;

namespace DataMaker.Schema.Cards;

/// <summary>
/// Display layout for the <b>card view</b> — the single-record, page-through
/// surface (the "Card view" toggle on the record list, the spiritual successor
/// to MS Access's form view). A card is one record laid out as a panel the
/// user flips through with a record navigator.
///
/// <para>
/// Deliberately reuses the form-builder's <see cref="Section"/> →
/// <see cref="Row"/> → <see cref="Column"/> model rather than inventing a
/// parallel one. A card layout is just "a different arrangement of the same
/// fields", so the existing column kinds, the tolerant
/// <see cref="ColumnJsonConverter"/>, and (eventually) the
/// <c>LayoutEditor</c> designer all apply unchanged — a future card designer
/// is a retargeted layout editor that writes into <see cref="Forms.Form.CardLayout"/>.
/// </para>
///
/// <para>
/// <b>Today there is exactly one layout: the built-in FOBO default.</b>
/// <see cref="Forms.Form.CardLayout"/> is left <c>null</c> and
/// <see cref="CardLayoutResolver.Resolve"/> synthesizes the default from the
/// form's fields. The type exists now so that "users design their own cards"
/// becomes <i>add an editor + persist a CardLayout</i> instead of reworking
/// the renderer.
/// </para>
/// </summary>
public sealed record CardLayout
{
    /// <summary>
    /// Sections that make up the card, top to bottom. A card is a single
    /// surface (no wizard paging), so it carries <see cref="Section"/>s
    /// directly rather than wrapping them in <see cref="Forms.FormStep"/>s.
    /// Empty ⇒ <see cref="CardLayoutResolver"/> falls back to the synthesized
    /// default, same escape hatch <see cref="LayoutResolver"/> gives forms
    /// with no authored steps.
    /// </summary>
    public List<Section> Sections { get; init; } = new();
}
