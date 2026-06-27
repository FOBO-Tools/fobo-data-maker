using System.Collections.Concurrent;
using System.Text;
using System.Text.Json.Nodes;
using DataMaker.Sdk;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace DataMaker.Sdk.AspNetCore;

/// <summary>
/// Renders a Data Maker form from a signed <c>.dmf</c> by hosting the JS
/// renderer. Usage:
///
/// <code>
/// &lt;datamaker-form dmf-path="forms/contact.dmf" encrypt="client" /&gt;
/// &lt;datamaker-form dmf-path="forms/contact.dmf" encrypt="server"
///                  submit-url="/datamaker/submit" /&gt;
/// </code>
///
/// <para><b>client</b> (default): the browser seals values end-to-end and POSTs
/// the ciphertext to the public /submissions endpoint — your server never sees
/// plaintext. Requires the <c>datamaker.browser.js</c> bundle (default loaded
/// from unpkg; override with <c>browser-bundle-url</c>).</para>
///
/// <para><b>server</b>: the browser POSTs plaintext to <c>submit-url</c> (wire
/// up <c>MapDataMakerSubmit</c>), and the server validates + seals + forwards.
/// No browser crypto.</para>
/// </summary>
[HtmlTargetElement("datamaker-form", TagStructure = TagStructure.WithoutEndTag)]
public sealed class DataMakerFormTagHelper : TagHelper
{
    private const string DefaultBrowserBundleUrl = "https://unpkg.com/@fobo-tools/datamaker/dist/datamaker.browser.js";

    /// <summary>Path to the .dmf file (absolute, or relative to the app working directory).</summary>
    [HtmlAttributeName("dmf-path")]
    public string? DmfPath { get; set; }

    /// <summary>"client" (end-to-end, default) or "server" (server-side seal).</summary>
    [HtmlAttributeName("encrypt")]
    public string Encrypt { get; set; } = "client";

    /// <summary>Server-mode endpoint that seals + forwards (default "/datamaker/submit").</summary>
    [HtmlAttributeName("submit-url")]
    public string SubmitUrl { get; set; } = "/datamaker/submit";

    /// <summary>Base URL the vendored renderer assets are served from.</summary>
    [HtmlAttributeName("asset-base")]
    public string AssetBase { get; set; } = "/_content/DataMaker.Sdk.AspNetCore/";

    /// <summary>Public submissions API base (client mode).</summary>
    [HtmlAttributeName("api-base-url")]
    public string ApiBaseUrl { get; set; } = DataMakerClient.DefaultApiBaseUrl;

    /// <summary>URL of the datamaker.browser.js bundle (client mode).</summary>
    [HtmlAttributeName("browser-bundle-url")]
    public string? BrowserBundleUrl { get; set; }

    /// <summary>false → render structure-only, dropping the .dmf author design.</summary>
    [HtmlAttributeName("apply-form-style")]
    public bool ApplyFormStyle { get; set; } = true;

    // Parsed-bundle cache keyed by path + last-write time. Reading the file,
    // parsing the ZIP, and re-verifying the Ed25519 signature on every request
    // blocks a thread-pool thread and redoes crypto under load; the .dmf only
    // changes when redeployed, so cache by (path, mtime) and the heavy work
    // runs once per file version. #81c.
    private sealed record CachedForm(string FormId, string? RecipientPublicKey, string BundleJson);
    private static readonly ConcurrentDictionary<string, CachedForm> _cache = new();

    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        if (string.IsNullOrWhiteSpace(DmfPath))
            throw new InvalidOperationException("<datamaker-form> requires a dmf-path attribute.");

        var cached = await LoadCachedAsync(DmfPath);

        var server = string.Equals(Encrypt, "server", StringComparison.OrdinalIgnoreCase);
        if (!server && string.IsNullOrEmpty(cached.RecipientPublicKey))
            throw new InvalidOperationException("client encrypt mode needs a recipient in the .dmf; use encrypt=\"server\" or a recipient bundle.");

        var bundleJson = cached.BundleJson;

        var cfg = new JsonObject
        {
            ["encrypt"] = server ? "server" : "client",
            ["formId"] = cached.FormId,
            ["applyFormStyle"] = ApplyFormStyle,
        };
        if (server)
        {
            cfg["submitUrl"] = SubmitUrl;
        }
        else
        {
            cfg["recipientPublicKey"] = cached.RecipientPublicKey;
            cfg["apiBaseUrl"] = ApiBaseUrl;
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("class", "datamaker-form");

        var html = new StringBuilder();
        html.Append("<link rel=\"stylesheet\" href=\"").Append(Asset("layout.css")).Append("\">");
        html.Append("<link rel=\"stylesheet\" href=\"").Append(Asset("styles.css")).Append("\">");
        html.Append("<main id=\"form-root\" class=\"dm-sheet\"></main>");
        html.Append("<pre id=\"boot-error\" hidden></pre>");
        // Escape "</" so a "</script>" inside string content can't close the tag early.
        html.Append("<script type=\"application/json\" id=\"form-bundle\">")
            .Append(bundleJson.Replace("</", "<\\/")).Append("</script>");
        html.Append("<script>window.DataMakerConfig=").Append(cfg.ToJsonString().Replace("</", "<\\/")).Append(";</script>");
        html.Append("<script src=\"").Append(Asset("fn.js")).Append("\"></script>");
        if (!server)
            html.Append("<script src=\"").Append(BrowserBundleUrl ?? DefaultBrowserBundleUrl).Append("\"></script>");
        html.Append("<script src=\"").Append(Asset("dm-submit.js")).Append("\"></script>");
        html.Append("<script src=\"").Append(Asset("renderer.js")).Append("\"></script>");

        output.Content.SetHtmlContent(html.ToString());
    }

    private string Asset(string file) => AssetBase.TrimEnd('/') + "/" + file;

    private static async Task<CachedForm> LoadCachedAsync(string path)
    {
        // mtime in the key means a redeploy that rewrites the .dmf is picked up
        // without a process restart, and an unchanged file is a pure cache hit.
        var key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
        if (_cache.TryGetValue(key, out var hit)) return hit;

        var bytes = await File.ReadAllBytesAsync(path);
        var form = DataMakerClient.ReadForm(bytes, verify: true, includeRenderBundle: true);
        if (form.RenderBundle is null)
            throw new InvalidOperationException(".dmf has no render bundle (not a v3 bundle?).");

        var entry = new CachedForm(form.FormId, form.RecipientPublicKey, FormBundle.Assemble(form.RenderBundle));
        _cache[key] = entry;
        return entry;
    }
}
