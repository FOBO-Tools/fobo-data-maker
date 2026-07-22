using System;
using System.Collections.Generic;
using System.Globalization;

namespace DataMaker.Schema.Fields;

/// <summary>One selectable point of a scale / Likert field: the stored numeric
/// <see cref="Value"/> and the human <see cref="Label"/> shown in a dropdown.</summary>
public sealed record ScalePoint(long Value, string Label);

/// <summary>
/// Builds the discrete points of a scale field (Min..Max) as value+label pairs.
/// The end points fold in the anchor labels ("1 — Strongly disagree") exactly
/// like the PDF / terminal renderers export a scale as a dropdown — so the
/// records-grid inline editor offers the same constrained, valid set of points
/// instead of a free-text number box.
/// </summary>
public static class ScalePoints
{
    /// <summary>Points for <paramref name="options"/> (null = the Likert default 1..5). Max is clamped to ≥ Min + 1, matching the render-time clamp.</summary>
    public static IReadOnlyList<ScalePoint> Build(ScaleOptions? options)
    {
        var sc  = options ?? new ScaleOptions();
        var min = sc.Min;
        var max = Math.Max(min + 1, sc.Max);

        var points = new List<ScalePoint>(max - min + 1);
        for (var n = min; n <= max; n++)
            points.Add(new ScalePoint(n, Label(n, min, max, sc.MinLabel, sc.MaxLabel)));
        return points;
    }

    /// <summary>Display text for a single point — the bare number, with the anchor label folded in at the Min / Max ends when one is set.</summary>
    public static string Label(long n, int min, int max, string? minLabel, string? maxLabel)
    {
        var num = n.ToString(CultureInfo.InvariantCulture);
        if (n == min && !string.IsNullOrWhiteSpace(minLabel)) return $"{num} — {minLabel}";
        if (n == max && !string.IsNullOrWhiteSpace(maxLabel)) return $"{num} — {maxLabel}";
        return num;
    }
}
