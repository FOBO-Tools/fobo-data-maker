using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace DataMaker.Schema.Assets;

/// <summary>
/// A design-time image lifted out of a form's inline <c>data:</c> URIs.
/// Keyed by content hash so the same image used in several places stores once.
/// </summary>
public sealed record FormImageAsset(string Sha256Hex, string Mime, string Extension, byte[] Bytes)
{
    /// <summary>The hash-keyed file name, e.g. <c>9f2c….png</c>.</summary>
    public string FileName => $"{Sha256Hex}.{Extension}";
}

/// <summary>
/// Lifts inline <c>data:image/…;base64,…</c> URIs out of a serialized form (or
/// hosted config) and rewrites each to a caller-supplied reference — an S3 URL
/// for hosted forms, a <c>dmf:images/…</c> path for <c>.dmf</c> bundles.
///
/// <para>
/// Works on the JSON, NOT the typed tree: a recursive <see cref="JsonNode"/>
/// walk replaces every string value that is a base64 image data URI. This
/// catches every image location — <c>ImageColumn.Source</c>,
/// <c>ButtonColumn.InlineImageSrc</c>, the header <c>LogoDataUri</c>, and
/// <c>StyleImage.BackgroundImage.Source</c> on the form, sections, every column
/// kind, nested groups, and button hover/pressed states — without enumerating
/// them, so a new image-bearing property is covered automatically.
/// </para>
///
/// <para>
/// Non-base64 data URIs and <c>http(s)</c> / <c>dmf:</c> sources are left
/// untouched. Idempotent: re-running on already-rewritten JSON finds nothing.
/// </para>
/// </summary>
public static class FormAssetExtractor
{
    /// <summary>
    /// Rewrite every inline image data URI in <paramref name="json"/> to
    /// <paramref name="refBuilder"/>(asset). Returns the rewritten JSON and the
    /// distinct (hash-deduped) assets the caller must persist to its sink.
    /// Returns the input unchanged (no assets) when there are no inline images
    /// or the JSON can't be parsed.
    /// </summary>
    public static (string Json, IReadOnlyList<FormImageAsset> Assets) Extract(
        string json, Func<FormImageAsset, string> refBuilder)
    {
        if (string.IsNullOrWhiteSpace(json)) return (json, []);

        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return (json, []); }
        if (root is null) return (json, []);

        var assets = new Dictionary<string, FormImageAsset>(StringComparer.Ordinal);
        var changed = Walk(root, assets, refBuilder);
        if (!changed) return (json, []);

        return (root.ToJsonString(), assets.Values.ToList());
    }

    // Returns true if any value was rewritten. Recurses objects + arrays;
    // replaces base64 image data-URI string leaves in place.
    private static bool Walk(JsonNode node, Dictionary<string, FormImageAsset> assets, Func<FormImageAsset, string> refBuilder)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                // Snapshot keys — we mutate values during iteration.
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue v && TryRewrite(v, assets, refBuilder, out var replacement))
                    {
                        obj[key] = replacement;
                        changed = true;
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        changed |= Walk(child, assets, refBuilder);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue v && TryRewrite(v, assets, refBuilder, out var replacement))
                    {
                        arr[i] = replacement;
                        changed = true;
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        changed |= Walk(child!, assets, refBuilder);
                    }
                }
                break;
        }
        return changed;
    }

    private static bool TryRewrite(
        JsonValue value, Dictionary<string, FormImageAsset> assets,
        Func<FormImageAsset, string> refBuilder, out string? replacement)
    {
        replacement = null;
        if (!value.TryGetValue<string>(out var s) || string.IsNullOrEmpty(s)) return false;
        if (TryParse(s, out var asset) is false) return false;

        assets.TryAdd(asset!.Sha256Hex, asset);
        replacement = refBuilder(asset);
        return true;
    }

    // Parse a `data:<mime>;base64,<payload>` image URI. Anything else (non-data,
    // non-base64, non-image) returns false and is left untouched.
    private static bool TryParse(string s, out FormImageAsset? asset)
    {
        asset = null;
        if (!s.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase)) return false;

        var comma = s.IndexOf(',');
        if (comma < 0) return false;
        var header = s.AsSpan(5, comma - 5); // after "data:"
        var semi = header.IndexOf(';');
        if (semi < 0 || !header[(semi + 1)..].Trim().SequenceEqual("base64")) return false;

        var mime = header[..semi].ToString();
        byte[] bytes;
        try { bytes = Convert.FromBase64String(s[(comma + 1)..]); }
        catch { return false; }
        if (bytes.Length == 0) return false;

        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        asset = new FormImageAsset(hash, mime, ExtensionFor(mime), bytes);
        return true;
    }

    private static string ExtensionFor(string mime) => mime.ToLowerInvariant() switch
    {
        "image/png"     => "png",
        "image/jpeg"    => "jpg",
        "image/jpg"     => "jpg",
        "image/gif"     => "gif",
        "image/webp"    => "webp",
        "image/svg+xml" => "svg",
        "image/bmp"     => "bmp",
        "image/x-icon"  => "ico",
        "image/avif"    => "avif",
        _               => "bin",
    };
}
