namespace DataMaker.Schema.Styling;

/// <summary>
/// Identifies a single styleable property on <see cref="Style"/>. Used by the
/// designer's repaint coordinator to decide, per edit, the cheapest faithful
/// way to push a change into the live preview: mutate the selected element's own
/// container in place, re-apply typography to its text subtree in place, or fall
/// back to a full visual-tree rebuild.
/// </summary>
public enum StyleToken
{
    // Typography — applied to the element's own text subtree. For a leaf element
    // (field, heading) this is a flicker-free in-place re-apply; for a container
    // (section, group) it cascades to descendants with their own overrides and
    // needs a rebuild.
    FontFamily, FontSize, FontWeight, TextColor,
    TextAlignment, LineHeight,

    // Container chrome — owned by the element's own wrapping Border, so a
    // targeted mutation of that one element matches what a rebuild would draw.
    BackgroundColor, BackgroundGradient, BackgroundImage,
    BorderColor, BorderThickness, CornerRadius,
    Padding, PaddingTop, PaddingRight, PaddingBottom, PaddingLeft,
    Margin, MarginTop, MarginRight, MarginBottom, MarginLeft,
    Opacity,

    // No settled in-place path — always rebuild.
    Shadow,
}

/// <summary>
/// Pure (UI-free) diffing + capability rules behind the designer's selective
/// repaint. Lives in Schema so it is unit-testable without Uno.
///
/// <para>
/// The capability table is deliberately conservative: a token is eligible for
/// an in-place mutation only when the renderer can reproduce it exactly without
/// a rebuild. Container chrome re-asserts the value on the element's own Border
/// (works for any element). Typography re-applies to the element's text subtree
/// (in-place only for leaf elements that own their text). Everything else — a
/// token being CLEARED back to null (which needs the per-kind builder baseline),
/// shadow, and any mix of buckets in a single edit — falls back to a rebuild.
/// Promote tokens out of the rebuild bucket one at a time, each behind a test.
/// </para>
/// </summary>
public static class StylePatch
{
    private static readonly Style Empty = new();

    /// <summary>The set of tokens whose value differs between two styles (null treated as an empty style).</summary>
    public static IReadOnlySet<StyleToken> Diff(Style? a, Style? b)
    {
        a ??= Empty;
        b ??= Empty;
        var set = new HashSet<StyleToken>();

        void C(StyleToken t, bool changed) { if (changed) set.Add(t); }

        C(StyleToken.FontFamily,    a.FontFamily    != b.FontFamily);
        C(StyleToken.FontSize,      a.FontSize      != b.FontSize);
        C(StyleToken.FontWeight,    a.FontWeight    != b.FontWeight);
        C(StyleToken.TextColor,     a.TextColor     != b.TextColor);
        C(StyleToken.TextAlignment, a.TextAlignment != b.TextAlignment);
        C(StyleToken.LineHeight,    a.LineHeight    != b.LineHeight);

        C(StyleToken.BackgroundColor,    a.BackgroundColor != b.BackgroundColor);
        C(StyleToken.BackgroundGradient, !Equals(a.BackgroundGradient, b.BackgroundGradient));
        C(StyleToken.BackgroundImage,    !Equals(a.BackgroundImage,    b.BackgroundImage));
        C(StyleToken.BorderColor,        a.BorderColor     != b.BorderColor);
        C(StyleToken.BorderThickness,    a.BorderThickness != b.BorderThickness);
        C(StyleToken.CornerRadius,       a.CornerRadius    != b.CornerRadius);

        C(StyleToken.Padding,       a.Padding       != b.Padding);
        C(StyleToken.PaddingTop,    a.PaddingTop    != b.PaddingTop);
        C(StyleToken.PaddingRight,  a.PaddingRight  != b.PaddingRight);
        C(StyleToken.PaddingBottom, a.PaddingBottom != b.PaddingBottom);
        C(StyleToken.PaddingLeft,   a.PaddingLeft   != b.PaddingLeft);
        C(StyleToken.Margin,        a.Margin        != b.Margin);
        C(StyleToken.MarginTop,     a.MarginTop     != b.MarginTop);
        C(StyleToken.MarginRight,   a.MarginRight   != b.MarginRight);
        C(StyleToken.MarginBottom,  a.MarginBottom  != b.MarginBottom);
        C(StyleToken.MarginLeft,    a.MarginLeft    != b.MarginLeft);
        C(StyleToken.Opacity,       a.Opacity       != b.Opacity);

        C(StyleToken.Shadow, !Equals(a.Shadow, b.Shadow));

        return set;
    }

    /// <summary>Container-chrome tokens the renderer's ApplyContainer can re-assert on the element's own Border.</summary>
    private static readonly HashSet<StyleToken> ContainerTokens = new()
    {
        StyleToken.BackgroundColor, StyleToken.BackgroundGradient, StyleToken.BackgroundImage,
        StyleToken.BorderColor, StyleToken.BorderThickness, StyleToken.CornerRadius,
        StyleToken.Padding, StyleToken.PaddingTop, StyleToken.PaddingRight, StyleToken.PaddingBottom, StyleToken.PaddingLeft,
        StyleToken.Margin, StyleToken.MarginTop, StyleToken.MarginRight, StyleToken.MarginBottom, StyleToken.MarginLeft,
        StyleToken.Opacity,
    };

    /// <summary>Typography tokens the renderer re-applies to the element's text subtree.</summary>
    private static readonly HashSet<StyleToken> TypographyTokens = new()
    {
        StyleToken.FontFamily, StyleToken.FontSize, StyleToken.FontWeight, StyleToken.TextColor,
        StyleToken.TextAlignment, StyleToken.LineHeight,
    };

    public static bool IsContainer(StyleToken token)  => ContainerTokens.Contains(token);
    public static bool IsTypography(StyleToken token) => TypographyTokens.Contains(token);

    /// <summary>True if <paramref name="token"/>'s value is null on <paramref name="style"/> (i.e. the edit clears it).</summary>
    public static bool IsCleared(StyleToken token, Style? style)
    {
        style ??= Empty;
        return token switch
        {
            StyleToken.FontFamily         => style.FontFamily is null,
            StyleToken.FontSize           => style.FontSize is null,
            StyleToken.FontWeight         => style.FontWeight is null,
            StyleToken.TextColor          => style.TextColor is null,
            StyleToken.TextAlignment      => style.TextAlignment is null,
            StyleToken.LineHeight         => style.LineHeight is null,
            StyleToken.BackgroundColor    => style.BackgroundColor is null,
            StyleToken.BackgroundGradient => style.BackgroundGradient is null,
            StyleToken.BackgroundImage    => style.BackgroundImage is null,
            StyleToken.BorderColor        => style.BorderColor is null,
            StyleToken.BorderThickness    => style.BorderThickness is null,
            StyleToken.CornerRadius       => style.CornerRadius is null,
            StyleToken.Padding            => style.Padding is null,
            StyleToken.PaddingTop         => style.PaddingTop is null,
            StyleToken.PaddingRight       => style.PaddingRight is null,
            StyleToken.PaddingBottom      => style.PaddingBottom is null,
            StyleToken.PaddingLeft        => style.PaddingLeft is null,
            StyleToken.Margin             => style.Margin is null,
            StyleToken.MarginTop          => style.MarginTop is null,
            StyleToken.MarginRight        => style.MarginRight is null,
            StyleToken.MarginBottom       => style.MarginBottom is null,
            StyleToken.MarginLeft         => style.MarginLeft is null,
            StyleToken.Opacity            => style.Opacity is null,
            StyleToken.Shadow             => style.Shadow is null,
            _                             => true,
        };
    }

    /// <summary>
    /// Decide how a style edit should repaint the preview. An all-container edit
    /// is a <see cref="StyleRepaint.Container"/> in-place mutation — including a
    /// CLEAR, because the in-place applier resets the element's Border to its
    /// per-kind builder baseline before re-applying (see
    /// <c>SelectableElement.RestyleContainer</c>). An all-typography SET is a
    /// <see cref="StyleRepaint.Typography"/> in-place re-apply; a typography
    /// clear rebuilds (the resolved cascade has to recompute). Anything mixed, or
    /// touching a non-in-place token (shadow), rebuilds.
    /// </summary>
    public static StyleRepaint Classify(Style? oldStyle, Style? newStyle)
    {
        var changed = Diff(oldStyle, newStyle);
        if (changed.Count == 0) return StyleRepaint.None;

        var allContainer    = true;
        var allTypography   = true;
        var typographyClear = false;
        foreach (var t in changed)
        {
            if (!IsContainer(t))  allContainer  = false;
            if (!IsTypography(t)) allTypography = false;
            if (IsTypography(t) && IsCleared(t, newStyle)) typographyClear = true;
        }

        if (allContainer)                    return StyleRepaint.Container;
        if (allTypography && !typographyClear) return StyleRepaint.Typography;
        return StyleRepaint.Rebuild;
    }
}

/// <summary>Outcome of <see cref="StylePatch.Classify"/> — the cheapest faithful repaint for an edit.</summary>
public enum StyleRepaint
{
    /// <summary>Nothing visual changed — no repaint needed.</summary>
    None,
    /// <summary>Mutate the selected element's own container Border in place.</summary>
    Container,
    /// <summary>Re-apply typography to the selected element's text subtree in place (leaf elements only).</summary>
    Typography,
    /// <summary>Rebuild the visual tree (clears, cascading container typography, shadow, mixed edits, form palette).</summary>
    Rebuild,
}
