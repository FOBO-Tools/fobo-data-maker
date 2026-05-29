using DataMaker.Schema.Fields;
using DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

namespace DataMaker.Forms.Renderer.Terminal.Forms;

/// <summary>
/// Dispatches a <see cref="FieldDefinition"/> to the correct
/// <see cref="FieldBinding"/> implementation. Kinds without first-class terminal
/// support fall through to <see cref="UnsupportedBinding"/> with a reason —
/// the field still appears in tab order so it's discoverable, just not
/// editable from the TUI.
/// </summary>
internal static class FieldBindingFactory
{
    public static FieldBinding Create(FieldDefinition def, FormState state)
    {
        // Calculated fields route to a read-only display regardless of kind —
        // the runtime owns their value, the user never types in them.
        if (!string.IsNullOrWhiteSpace(def.CalculatedExpression))
            return new CalculatedFieldBinding(def, state);

        // Capability matrix is the single source of truth for what works in
        // which renderer. Anything flagged Unsupported here gets the same
        // discoverable-but-readonly treatment, with the matrix's own note
        // text as the tooltip — no need for a parallel switch in this file.
        var capability = RendererCapabilities.For(RendererSurface.Terminal, def.Kind);
        if (capability.Support == FieldSupport.Unsupported)
            return new UnsupportedBinding(def, state, capability.Note);

        return def.Kind switch
        {
        FieldTypes.Text     => new TextBinding(def, state),
        FieldTypes.Email    => new TextBinding(def, state),
        FieldTypes.Phone    => new TextBinding(def, state),
        FieldTypes.Url      => new TextBinding(def, state),
        FieldTypes.LongText => new LongTextBinding(def, state),
        FieldTypes.RichText => new RichTextBinding(def, state),

        FieldTypes.Number   => new NumberBinding(def, state),
        FieldTypes.Decimal  => new NumberBinding(def, state),
        FieldTypes.Money    => new NumberBinding(def, state),

        FieldTypes.Boolean  => new BooleanBinding(def, state),

        FieldTypes.Choice       => new ChoiceBinding(def, state),
        FieldTypes.MultiChoice  => new MultiChoiceBinding(def, state),

        FieldTypes.List     => new ListBinding(def, state),

        FieldTypes.Date     => new DateBinding(def, state),
        FieldTypes.DateTime => new DateTimeBinding(def, state),

        FieldTypes.Geo      => new GeoBinding(def, state),

        FieldTypes.Image      => new FileBinding(def, state),
        FieldTypes.Attachment => new FileBinding(def, state),

        _ => new UnsupportedBinding(def, state, $"unknown field kind '{def.Kind}'"),
        };
    }
}
