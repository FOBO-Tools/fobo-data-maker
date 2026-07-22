using System.Text.Json.Nodes;

namespace DataMaker.Schema.Assets;

/// <summary>
/// Publish-time guard: hosted forms may only carry <b>uploaded, inlined</b>
/// images (<c>data:image/…;base64,…</c>), never external URLs.
///
/// <para>
/// The designer is upload-only, so every image becomes a <c>data:</c> URI that
/// <see cref="FormAssetExtractor"/> lifts to our scanned, self-hosted S3 asset.
/// An external <c>http(s)</c> image source would hotlink straight onto the
/// hosted page — bypassing the publish-time content gates (porn/CSAM) and
/// leaking visitor IPs to a third-party host. A form that still carries one
/// (legacy, hand-edited JSON, or a tampered SDK payload) is rejected at publish.
/// </para>
///
/// <para>
/// Image-bearing fields are matched by their JSON key (<c>source</c> on image
/// columns and style background images, <c>inlineImageSrc</c> on buttons,
/// <c>logoDataUri</c> on the hosted config) — none of which is reused by a
/// non-image URL field in the schema. Empty values and <c>data:</c> URIs pass.
/// </para>
/// </summary>
public static class ImageSourcePolicy
{
    private static readonly HashSet<string> ImageKeys =
        new(StringComparer.OrdinalIgnoreCase) { "source", "inlineImageSrc", "logoDataUri" };

    /// <summary>
    /// True if any image-source field in <paramref name="json"/> holds a value
    /// that isn't an inline <c>data:</c> URI (i.e. an external reference). Returns
    /// false for empty / unparseable JSON.
    /// </summary>
    public static bool HasExternalImageRef(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return false;
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return false; }
        return root is not null && Walk(root);
    }

    private static bool Walk(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (key, child) in obj)
                {
                    if (child is JsonValue v && ImageKeys.Contains(key) && IsExternal(v))
                        return true;
                    if (child is JsonObject or JsonArray && Walk(child))
                        return true;
                }
                break;

            case JsonArray arr:
                foreach (var child in arr)
                    if (child is not null && Walk(child))
                        return true;
                break;
        }
        return false;
    }

    private static bool IsExternal(JsonValue value)
    {
        if (!value.TryGetValue<string>(out var s) || string.IsNullOrWhiteSpace(s)) return false;
        return !s.StartsWith("data:", StringComparison.OrdinalIgnoreCase);
    }
}
