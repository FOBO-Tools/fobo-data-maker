using System.Text.Json.Nodes;
using DataMaker.Sdk;

namespace DataMaker.Sdk.AspNetCore;

/// <summary>
/// Assembles the browser form-bundle JSON that renderer.js's <c>mount</c>
/// consumes — <c>{ form, compiled, elementCss, paletteCss }</c> — directly from
/// the entries baked into a .dmf v3, so no server-side FormBundleBuilder is
/// needed.
/// </summary>
public static class FormBundle
{
    /// <summary>Build the <c>#form-bundle</c> JSON from a .dmf's render bundle.</summary>
    public static string Assemble(RenderBundle bundle)
    {
        var node = new JsonObject
        {
            ["form"] = JsonNode.Parse(bundle.FormJson),
            ["compiled"] = bundle.CompiledJson is { } c ? JsonNode.Parse(c) : new JsonObject(),
            ["elementCss"] = bundle.ElementCssJson is { } e ? JsonNode.Parse(e) : new JsonObject(),
            ["paletteCss"] = bundle.PaletteCss ?? "",
        };
        return node.ToJsonString();
    }
}
