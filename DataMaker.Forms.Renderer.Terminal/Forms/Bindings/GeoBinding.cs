using System.Text.Json.Serialization;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Geo field binding for the terminal renderer. Editor row shows the
/// current formatted address (or "lat, lng" fallback) as a read-only
/// label + a "Search…" button that opens a modal
/// <see cref="GeoPickerDialog"/> with address autocomplete (Nominatim /
/// OpenStreetMap), live suggestion list, and manual lat/lng entry.
///
/// <para>Mirrors the desktop records-grid inline editor + the Uno
/// runtime form's GeoFieldEditor — same Nominatim client (350ms
/// debounce, descriptive User-Agent per their policy). Picking a
/// suggestion commits all three fields (lat / lng / formattedAddress);
/// manual lat/lng commit on dialog OK preserves whatever address text
/// the user typed.</para>
/// </summary>
internal sealed class GeoBinding : FieldBinding
{
    /// <summary>Shared HttpClient — Nominatim rate-limits per-IP, pool reuse is fine.</summary>
    internal static readonly HttpClient HttpClient = CreateNominatimClient();

    private static HttpClient CreateNominatimClient()
    {
        var c = new HttpClient();
        c.DefaultRequestHeaders.UserAgent.ParseAdd("DataMaker/1.0 (https://fobo-tools.com)");
        return c;
    }

    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _editor;
    private readonly Label _display;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public GeoBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        // Editor row: address-text label + Search button. The label sizes
        // to fill the remaining width so the button stays right-aligned.
        _editor = new View { Width = Dim.Fill(), Height = 1 };
        _display = new Label(RenderDisplay(State.Get(definition.Name)))
        {
            Width  = Dim.Fill() - 12,
            Height = 1,
        };
        var search = new Button("Search…")
        {
            X        = Pos.Right(_display) + 1,
            Y        = 0,
            Height   = 1,
        };
        search.Clicked += OpenPicker;
        _editor.Add(_display, search);
    }

    private void OpenPicker()
    {
        var current = State.Get(Definition.Name) as Geo? ?? (State.Get(Definition.Name) is Geo g ? g : new Geo(0, 0, null));
        var dialog = new GeoPickerDialog(current);
        Application.Run(dialog);
        if (dialog.Committed is { } picked)
        {
            State.Set(Definition.Name, picked);
            _display.Text = RenderDisplay(picked);
        }
    }

    private static string RenderDisplay(object? raw) => raw switch
    {
        Geo g when !string.IsNullOrWhiteSpace(g.FormattedAddress) => g.FormattedAddress!,
        Geo g                                                     => $"{g.Lat:F4}, {g.Lng:F4}",
        _                                                         => "(no location)",
    };

    public override View Label => _label;
    public override View Editor => _editor;
    public override int EditorHeight => 1;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}

/// <summary>
/// Wire shape for Nominatim search results. Lat/lon come back as strings
/// (their convention), parsed on consumption. Made public so the
/// source-generated <see cref="TerminalJsonContext"/> can resolve it
/// under trim/AOT publishes.
/// </summary>
public sealed record NominatimRaw(
    [property: JsonPropertyName("display_name")] string? DisplayName,
    [property: JsonPropertyName("lat")] string? Lat,
    [property: JsonPropertyName("lon")] string? Lon);
