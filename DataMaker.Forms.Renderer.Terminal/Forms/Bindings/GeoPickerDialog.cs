using System.Net.Http.Json;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Modal address-picker for <see cref="GeoBinding"/>. Two rows:
/// <list type="number">
///   <item>Address TextField — debounced Nominatim autocomplete.</item>
///   <item>Suggestion ListView — pick a result to set the location.</item>
/// </list>
/// Like the other renderers, the filler only picks an address; the lat/lng come
/// from the chosen suggestion (never typed by hand). Typing in the address box
/// invalidates a previous pick, so OK requires a real selection. OK commits the
/// working state into <see cref="Committed"/>; Cancel leaves it null.
/// </summary>
internal sealed class GeoPickerDialog : Dialog
{
    private readonly TextField _addrField;
    private readonly ListView  _suggestions;

    private List<NominatimResult> _currentResults = new();
    private CancellationTokenSource? _debounceCts;

    // Coordinates come only from a picked suggestion (or the seeded existing
    // value); null until something is selected.
    private double? _lat;
    private double? _lng;
    private bool _picking;   // suppress the clear-on-type while we set text programmatically

    /// <summary>The Geo picked when the user clicks OK; null if Cancel.</summary>
    public Geo? Committed { get; private set; }

    public GeoPickerDialog(Geo current)
        : base("Address picker", 70, 15)
    {
        _lat = double.IsNaN(current.Lat) ? null : current.Lat;
        _lng = double.IsNaN(current.Lng) ? null : current.Lng;

        var addrLabel = new Label("Address:") { X = 1, Y = 1 };
        _addrField = new TextField(current.FormattedAddress ?? "")
        {
            X = Pos.Right(addrLabel) + 1,
            Y = 1,
            Width = Dim.Fill(2),
        };
        _addrField.TextChanged += _ =>
        {
            if (_picking) return;
            // Typing invalidates any prior pick — coords only come from a chosen
            // suggestion, so OK will require the user to select again.
            _lat = null;
            _lng = null;
            OnAddressChanged();
        };

        _suggestions = new ListView(new List<string>())
        {
            X = 1, Y = 3,
            Width  = Dim.Fill(2),
            Height = 9,
        };
        _suggestions.OpenSelectedItem += OnSuggestionActivated;

        var ok = new Button("OK", is_default: true);
        ok.Clicked += OnOk;
        var cancel = new Button("Cancel");
        cancel.Clicked += () => { Committed = null; Application.RequestStop(); };

        AddButton(ok);
        AddButton(cancel);

        Add(addrLabel, _addrField, _suggestions);

        // Focus the address field as soon as the dialog lays out so the user can
        // type immediately — without it, focus lands on the default OK button and
        // a click is needed before the field accepts input.
        Loaded += () => _addrField.SetFocus();
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
                // Network / quota / rate-limit — silently swallow; the list just
                // stays empty and the user can refine the query.
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
        _picking = true;
        _addrField.Text = r.DisplayName;   // would otherwise clear the pick
        _picking = false;
        _lat = r.Lat;
        _lng = r.Lng;
    }

    private void OnOk()
    {
        // Require a real selection — the Geo struct needs both halves, and they
        // only come from a picked suggestion (or the seeded existing value).
        if (_lat is not { } lat || _lng is not { } lng)
        {
            MessageBox.ErrorQuery("No place selected", "Pick an address from the suggestions list.", "OK");
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
