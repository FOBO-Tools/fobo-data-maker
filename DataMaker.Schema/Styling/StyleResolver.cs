namespace DataMaker.Schema.Styling;

/// <summary>
/// Flattens an ancestor chain of optional <see cref="Style"/>s into a single
/// resolved <see cref="Style"/> per the cascade rule: later entries in the
/// chain override earlier ones per-property, null = inherit. Each style in
/// the chain is treated as "most-specific last" — callers pass them
/// root-to-leaf, e.g. <c>form, section, group, field</c>.
///
/// <para>
/// Produces an immutable <see cref="Style"/> where every property is the
/// effective value (still nullable — <c>null</c> means "nothing specified
/// anywhere in the chain; runtime should fall back to the application
/// default"). Rendering code reads each property and applies it only when
/// non-null; missing values keep the default element appearance.
/// </para>
/// </summary>
public static class StyleResolver
{
    /// <summary>
    /// Merge a chain of styles root-to-leaf. Returns a new <see cref="Style"/>
    /// whose properties are the last non-null value in the chain.
    /// </summary>
    public static Style Resolve(params Style?[] chain)
    {
        var result = new Style();
        foreach (var s in chain)
        {
            if (s is null) continue;

            // Padding / margin cascade: a higher level that sets the UNIFORM
            // value supersedes a lower level's per-side entirely (uniform is a
            // complete spec — "all sides this", so the inherited per-side is
            // dropped). A higher level that sets only per-side layers over a
            // lower uniform (per-side overrides that side; uniform fills the
            // rest). Without this, a uniform value authored above an inherited
            // per-side default (e.g. a button's themed 16/8 padding) was
            // shadowed at apply time, since per-side wins within a level.
            var padUniform = s.Padding is not null;
            var marUniform = s.Margin  is not null;

            result = result with
            {
                // Tier 1
                FontFamily         = s.FontFamily         ?? result.FontFamily,
                FontSize           = s.FontSize           ?? result.FontSize,
                FontWeight         = s.FontWeight         ?? result.FontWeight,
                TextColor          = s.TextColor          ?? result.TextColor,

                // Tier 2
                BackgroundColor    = s.BackgroundColor    ?? result.BackgroundColor,
                BorderColor        = s.BorderColor        ?? result.BorderColor,
                BorderThickness    = s.BorderThickness    ?? result.BorderThickness,
                CornerRadius       = s.CornerRadius       ?? result.CornerRadius,
                Padding            = s.Padding            ?? result.Padding,
                Margin             = s.Margin             ?? result.Margin,
                PaddingTop         = padUniform ? s.PaddingTop    : (s.PaddingTop    ?? result.PaddingTop),
                PaddingRight       = padUniform ? s.PaddingRight  : (s.PaddingRight  ?? result.PaddingRight),
                PaddingBottom      = padUniform ? s.PaddingBottom : (s.PaddingBottom ?? result.PaddingBottom),
                PaddingLeft        = padUniform ? s.PaddingLeft   : (s.PaddingLeft   ?? result.PaddingLeft),
                MarginTop          = marUniform ? s.MarginTop     : (s.MarginTop     ?? result.MarginTop),
                MarginRight        = marUniform ? s.MarginRight   : (s.MarginRight   ?? result.MarginRight),
                MarginBottom       = marUniform ? s.MarginBottom  : (s.MarginBottom  ?? result.MarginBottom),
                MarginLeft         = marUniform ? s.MarginLeft    : (s.MarginLeft    ?? result.MarginLeft),

                // Tier 3
                BackgroundImage    = s.BackgroundImage    ?? result.BackgroundImage,

                // Tier 4
                BackgroundGradient = s.BackgroundGradient ?? result.BackgroundGradient,
                Shadow             = s.Shadow             ?? result.Shadow,
                TextAlignment      = s.TextAlignment      ?? result.TextAlignment,
                LineHeight         = s.LineHeight         ?? result.LineHeight,
                Opacity            = s.Opacity            ?? result.Opacity,
            };
        }
        return result;
    }
}
