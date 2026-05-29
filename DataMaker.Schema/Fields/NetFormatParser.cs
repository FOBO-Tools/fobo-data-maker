namespace DataMaker.Schema.Fields;

/// <summary>
/// Tiny parser for the subset of .NET numeric format strings the form
/// renderers honour. The schema's <see cref="NumberOptions.Format"/> is
/// declared as a "standard .NET numeric format string" but renderers can't
/// faithfully cover the full surface across Uno, JS, and PDF — so we pin
/// down a small subset whose semantics translate cleanly to all three:
///
/// <list type="bullet">
///   <item><c>F</c>, <c>F0</c>..<c>Fn</c> — fixed N fraction digits, no grouping (default 2 if N omitted).</item>
///   <item><c>N</c>, <c>N0</c>..<c>Nn</c> — fixed N fraction digits, with thousand grouping (default 2).</item>
///   <item><c>D</c>, <c>D0</c>..<c>Dn</c> — integer (treated as F0).</item>
///   <item>Custom <c>0.00</c> / <c>0.000</c> / <c>#,##0.00</c> — fraction digits = trailing 0/# count after the dot; grouped iff a comma appears before the dot.</item>
/// </list>
///
/// Anything outside this subset returns <c>null</c> for both methods —
/// callers fall back to <see cref="NumberOptions.DecimalPlaces"/> alone.
/// </summary>
public static class NetFormatParser
{
    public static int? FractionDigits(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return null;
        var fmt = format.Trim();

        if (fmt.Length >= 1 && fmt[0] is 'F' or 'f' or 'N' or 'n' or 'D' or 'd')
        {
            var head = fmt[0];
            if (fmt.Length == 1) return head is 'D' or 'd' ? 0 : 2;
            if (int.TryParse(fmt[1..], out var n)) return Math.Max(0, n);
            return null;
        }

        var dot = fmt.IndexOf('.');
        if (dot < 0) return 0;

        int count = 0;
        for (int i = dot + 1; i < fmt.Length; i++)
            if (fmt[i] is '0' or '#') count++;
            else break;
        return count;
    }

    public static bool? IsGrouped(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return null;
        var fmt = format.Trim();

        if (fmt.Length >= 1 && fmt[0] is 'N' or 'n') return true;
        if (fmt.Length >= 1 && fmt[0] is 'F' or 'f' or 'D' or 'd') return false;

        var dot = fmt.IndexOf('.');
        var head = dot < 0 ? fmt : fmt[..dot];
        return head.Contains(',');
    }
}
