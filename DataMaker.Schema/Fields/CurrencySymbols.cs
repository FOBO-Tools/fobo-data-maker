namespace DataMaker.Schema.Fields;

/// <summary>
/// ISO 4217 currency code → human glyph used by money-field renderers.
/// Designer dropdowns also enumerate <see cref="Supported"/> for picking.
/// Unknown codes fall back to the code itself (e.g. <c>"BHD"</c>).
/// </summary>
public static class CurrencySymbols
{
    private static readonly IReadOnlyDictionary<string, string> _map =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["EUR"] = "€",
            ["USD"] = "$",
            ["GBP"] = "£",
            ["JPY"] = "¥",
            ["CHF"] = "Fr",
            ["CAD"] = "C$",
            ["AUD"] = "A$",
            ["NZD"] = "NZ$",
            ["SEK"] = "kr",
            ["NOK"] = "kr",
            ["DKK"] = "kr",
            ["PLN"] = "zł",
            ["CZK"] = "Kč",
            ["HUF"] = "Ft",
            ["CNY"] = "¥",
            ["INR"] = "₹",
            ["BRL"] = "R$",
            ["MXN"] = "$",
            ["ZAR"] = "R",
            ["TRY"] = "₺",
            ["KRW"] = "₩",
            ["SGD"] = "S$",
            ["HKD"] = "HK$",
        };

    public static IReadOnlyCollection<string> Supported => _map.Keys.ToList();

    public static string SymbolFor(string? currencyCode)
    {
        if (string.IsNullOrWhiteSpace(currencyCode)) return string.Empty;
        return _map.TryGetValue(currencyCode.Trim(), out var glyph) ? glyph : currencyCode.Trim().ToUpperInvariant();
    }
}
