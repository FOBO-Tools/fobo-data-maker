using DataMaker.Schema.Layout;

namespace DataMaker.Schema.Styling;

/// <summary>
/// Theme-level defaults for one button variant (Primary / Secondary / Subtle).
/// Lives on <see cref="FormStyle"/>; a <c>ButtonColumn</c> picking that variant
/// inherits these as its starting point, then per-instance Style / HoverStyle
/// / PressedStyle / icon fields override field-by-field.
///
/// <para>
/// All fields nullable — a missing default means "fall back to the renderer's
/// built-in baseline for that variant". That way a form-style author only
/// needs to override what's different about their look.
/// </para>
/// </summary>
public sealed record ButtonDefaults
{
    /// <summary>Base-state visuals (typography + container).</summary>
    public Style? Base { get; init; }

    /// <summary>:hover-state overlay applied on top of <see cref="Base"/>.</summary>
    public Style? Hover { get; init; }

    /// <summary>:active-state overlay applied on top of <see cref="Base"/>.</summary>
    public Style? Pressed { get; init; }

    /// <summary>Default Font Awesome glyph if the button instance doesn't override.</summary>
    public string? IconGlyph { get; init; }

    /// <summary>Default icon side if the button instance doesn't override.</summary>
    public ButtonIconPosition? IconPosition { get; init; }
}
