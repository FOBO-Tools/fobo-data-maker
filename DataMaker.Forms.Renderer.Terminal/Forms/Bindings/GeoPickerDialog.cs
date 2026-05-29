using System.Net.Http.Json;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Modal address-picker for <see cref="GeoBinding"/>. Three rows:
/// <list type="number">
///   <item>Address TextField — debounced Nominatim autocomplete.</item>
///   <item>Suggestion ListView — pick a result to fill all three fields.</item>
///   <item>Latitude / Longitude TextFields — manual fine-tune.</item>
/// </list>
/// OK commits the working state into <see cref="Committed"/>; Cancel
/// leaves it null. Caller (<see cref="GeoBinding"/>) reads
/// <see cref="Committed"/> after <c>Application.Run(dialog)</c> returns.
/// </summary>
internal sealed class GeoPickerDialog : Dialog
{
    private readonly TextField _addrField;
    private readonly ListView  _suggestions;
    private readonly TextField _latField;
    private readonly TextField _lngField;

    private List<NominatimResult> _currentResults = new();
    private CancellationTokenSource? _debounceCts;

    /// <summary>The Geo picked when the user clicks OK; null if Cancel.</summary>
    public Geo? Committed { get; private set; }

    public GeoPickerDialog(Geo current)
        : base("Address picker", 70, 18)
    {
        var addrLabel = new Label("Address:") { X = 1, Y = 1 };
        _addrField = new TextField(current.FormattedAddress ?? "")
        {
            X = Pos.Right(addrLabel) + 1,
            Y = 1,
            Width = Dim.Fill(2),
        };
        _addrField.TextChanged += _ => OnAddressChanged();

        _suggestions = new ListView(new List<string>())
        {
            X = 1, Y = 3,
            Width  = Dim.Fill(2),
            Height = 8,
        };
        _suggestions.OpenSelectedItem += OnSuggestionActivated;

        var latLabel = new Label("Lat:") { X = 1, Y = 12 };
        _latField = new TextField(double.IsNaN(current.Lat) ? "" : current.Lat.ToString("G", System.Globalization.CultureInfo.InvariantCulture))
        {
            X = Pos.Right(latLabel) + 1, Y = 12, Width = 20,
        };
        var lngLabel = new Label("Lng:") { X = Pos.Right(_latField) + 2, Y = 12 };
        _lngField = new TextField(double.IsNaN(current.Lng) ? "" : current.Lng.ToString("G", System.Globalization.CultureInfo.InvariantCulture))
        {
            X = Pos.Right(lngLabel) + 1, Y = 12, Width = 20,
        };

        var ok = new Button("OK", is_default: true);
        ok.Clicked += OnOk;
        var cancel = new Button("Cancel");
        cancel.Clicked += () => { Committed = null; Application.RequestStop(); };

        AddButton(ok);
        AddButton(cancel);

        Add(addrLabel, _addrField, _suggestions, latLabel, _latField, lngLabel, _lngField);
    }

    private void OnAddressChanged()
    {
        var q = _addrField.Text.ToString()?.Trim() ?? "";
        if (q.Length < 3)
        {
            _suggestions.SetSource(new List<string>());
            _currentResults.Clear();
            return;
        }

        _debounceCts?.Cancel();
        _debounceCts = new CancellationTokenSource();
        var ct = _debounceCts.Token;

        Task.Run(async () =>
        {
            try
            {
                await Task.Delay(350, ct);
                var results = await SearchAsync(q, ct);
                if (ct.IsCancellationRequested) return;
                Application.MainLoop.Invoke(() =>
                {
                    _currentResults = results;
                    _suggestions.SetSource(results.Select(r => r.DisplayName).ToList());
                });
            }
            catch (OperationCanceledException) { }
            catch
            {
                // Network / quota / rate-limit — silently swallow; manual
                // lat/lng entry still works.
                Application.MainLoop.Invoke(() =>
                {
                    _currentResults.Clear();
                    _suggestions.SetSource(new List<string>());
                });
            }
        }, ct);
    }

    private void OnSuggestionActivated(ListViewItemEventArgs args)
    {
        if (args.Item < 0 || args.Item >= _currentResults.Count) return;
        var r = _currentResults[args.Item];
        _addrField.Text = r.DisplayName;
        _latField.Text  = r.Lat.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
        _lngField.Text  = r.Lng.ToString("G", System.Globalization.CultureInfo.InvariantCulture);
    }

    private void OnOk()
    {
        // Parse manual lat/lng; reject the commit if either is missing or
        // malformed — Geo struct requires both halves.
        if (!double.TryParse(_latField.Text.ToString(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var lat) ||
            !double.TryParse(_lngField.Text.ToString(), System.Globalization.NumberStyles.Float,
                             System.Globalization.CultureInfo.InvariantCulture, out var lng))
        {
            MessageBox.ErrorQuery("Bad coordinates", "Latitude and longitude must both be valid numbers.", "OK");
            return;
        }
        var addr = _addrField.Text.ToString()?.Trim();
        Committed = new Geo(lat, lng, string.IsNullOrWhiteSpace(addr) ? null : addr);
        Application.RequestStop();
    }

    private static async Task<List<NominatimResult>> SearchAsync(string query, CancellationToken ct)
    {
        var url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=6&addressdetails=0";
        // Use the source-generated context so this resolves under
        // trim/AOT publishes (Nominatim DTO array registered there).
        var raw = await GeoBinding.HttpClient.GetFromJsonAsync(url, DataMaker.Forms.Renderer.Terminal.TerminalJsonContext.Default.NominatimRawArray, ct);
        if (raw is null) return new();
        var list = new List<NominatimResult>(raw.Length);
        foreach (var r in raw)
        {
            if (double.TryParse(r.Lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat) &&
                double.TryParse(r.Lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lng))
                list.Add(new NominatimResult(r.DisplayName ?? "", lat, lng));
        }
        return list;
    }

    private sealed record NominatimResult(string DisplayName, double Lat, double Lng);
}
