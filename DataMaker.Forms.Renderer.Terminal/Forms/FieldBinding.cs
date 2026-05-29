using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Connects one <see cref="FieldDefinition"/> to a Terminal.Gui widget.
/// The binding owns the editor view, pushes the current value back into
/// <see cref="FormState"/> on every change, and exposes a
/// <see cref="LabelHeight"/> + <see cref="EditorHeight"/> so the walker can
/// pack bindings vertically without asking the widget.
///
/// <para>
/// Bindings are fire-and-forget on field edits — every keystroke / toggle
/// writes through to state. That keeps the walker simple and matches how the
/// Uno renderer works (no explicit commit on tab).
/// </para>
/// </summary>
internal abstract class FieldBinding
{
    protected FieldBinding(FieldDefinition definition, FormState state)
    {
        Definition = definition;
        State      = state;
    }

    public FieldDefinition Definition { get; }
    public FormState State { get; }

    /// <summary>The label view rendered above the editor. Null if the editor owns its own label.</summary>
    public abstract View? Label { get; }

    /// <summary>The editor widget the user interacts with.</summary>
    public abstract View Editor { get; }

    /// <summary>Rows consumed by the label (0 if <see cref="Label"/> is null).</summary>
    public virtual int LabelHeight => Label is null ? 0 : 1;

    /// <summary>Rows consumed by the editor.</summary>
    public abstract int EditorHeight { get; }

    /// <summary>
    /// Red <c>(*)</c> marker attached to the label when the field is required.
    /// Returned so <see cref="FormRenderer"/> can re-colour it with the field's
    /// resolved scheme (keeps the inherited background, flips fg to red).
    /// Null for non-required fields or bindings that own their own label
    /// (e.g. <see cref="Bindings.BooleanBinding"/>).
    /// </summary>
    public virtual Label? RequiredAsterisk => null;

    /// <summary>Description line (shown as a single hint row below the editor). Null = no hint.</summary>
    public virtual string? HintLine =>
        string.IsNullOrWhiteSpace(Definition.Description) ? null : Definition.Description;

    /// <summary>
    /// Called by <see cref="FormRuntime"/> when the field's computed value
    /// changes (calculated fields). Default no-op — regular bindings ignore
    /// it since their display is user-driven.
    /// </summary>
    public virtual void OnComputedValueUpdated(object? value) { }

    /// <summary>
    /// Optional 1-cell "invalid" marker (white <c>!</c> on red) that
    /// <see cref="FormRenderer"/> overlays on the last column of the editor.
    /// <see cref="FormRuntime"/> flips <see cref="View.Visible"/> based on
    /// per-field validation after the user tabs out. Null for kinds where
    /// "last cell of the editor" doesn't make visual sense (CheckBox,
    /// RadioGroup) — those fall back to the status-bar message only.
    /// </summary>
    public virtual Label? ErrorIndicator => null;

    /// <summary>Helper for bindings that want the default red-bang indicator.</summary>
    protected static Label CreateErrorIndicator() => new("!")
    {
        Width       = 1,
        Height      = 1,
        Visible     = false,
        ColorScheme = new ColorScheme
        {
            Normal    = new global::Terminal.Gui.Attribute(Color.White, Color.Red),
            Focus     = new global::Terminal.Gui.Attribute(Color.White, Color.Red),
            HotNormal = new global::Terminal.Gui.Attribute(Color.White, Color.Red),
            HotFocus  = new global::Terminal.Gui.Attribute(Color.White, Color.Red),
            Disabled  = new global::Terminal.Gui.Attribute(Color.White, Color.Red),
        },
    };
}
