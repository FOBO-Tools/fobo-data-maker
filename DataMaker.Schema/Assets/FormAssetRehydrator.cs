using System.Text.Json.Nodes;

namespace DataMaker.Schema.Assets;

/// <summary>
/// Inverse of <see cref="FormAssetExtractor"/>: turns <c>dmf:images/{hash}.{ext}</c>
/// references back into inline <c>data:</c> URIs using the bundle's
/// (already hash-verified) image bytes. Run after reading a <c>.dmf</c> so every
/// renderer receives an ordinary inline-image form and needs no knowledge of the
/// <c>dmf:</c> scheme. <c>http(s)</c> and <c>data:</c> sources are left untouched.
/// </summary>
public static class FormAssetRehydrator
{
    private const string Scheme = "dmf:";
    private const string ImagesPrefix = "images/";

    /// <summary>
    /// Rewrite every <c>dmf:images/…</c> reference in <paramref name="json"/> to a
    /// data URI, resolving bytes from <paramref name="bundleFiles"/> (keyed by
    /// manifest path, e.g. <c>images/abc.png</c> — the shape of
    /// <c>VerifiedForm.Extras</c>). Unknown refs are left as-is. Returns the input
    /// unchanged when there's nothing to do or the JSON can't be parsed.
    /// </summary>
    public static string Rehydrate(string json, IReadOnlyDictionary<string, byte[]> bundleFiles)
    {
        if (string.IsNullOrEmpty(json) || bundleFiles.Count == 0) return json;
        if (!json.Contains(Scheme, StringComparison.Ordinal)) return json;

        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch { return json; }
        if (root is null) return json;

        var changed = Walk(root, bundleFiles);
        return changed ? root.ToJsonString() : json;
    }

    private static bool Walk(JsonNode node, IReadOnlyDictionary<string, byte[]> files)
    {
        var changed = false;
        switch (node)
        {
            case JsonObject obj:
                foreach (var key in obj.Select(kv => kv.Key).ToList())
                {
                    var child = obj[key];
                    if (child is JsonValue v && TryRehydrate(v, files, out var replacement))
                    {
                        obj[key] = replacement;
                        changed = true;
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        changed |= Walk(child, files);
                    }
                }
                break;

            case JsonArray arr:
                for (var i = 0; i < arr.Count; i++)
                {
                    var child = arr[i];
                    if (child is JsonValue v && TryRehydrate(v, files, out var replacement))
                    {
                        arr[i] = replacement;
                        changed = true;
                    }
                    else if (child is JsonObject or JsonArray)
                    {
                        changed |= Walk(child!, files);
                    }
                }
                break;
        }
        return changed;
    }

    private static bool TryRehydrate(JsonValue value, IReadOnlyDictionary<string, byte[]> files, out string? dataUri)
    {
        dataUri = null;
        if (!value.TryGetValue<string>(out var s) || string.IsNullOrEmpty(s)) return false;
        if (!s.StartsWith(Scheme + ImagesPrefix, StringComparison.Ordinal)) return false;

        var path = s[Scheme.Length..]; // "images/{hash}.{ext}"
        if (!files.TryGetValue(path, out var bytes)) return false;

        var ext  = path[(path.LastIndexOf('.') + 1)..];
        dataUri = $"data:{MimeFor(ext)};base64,{Convert.ToBase64String(bytes)}";
        return true;
    }

    private static string MimeFor(string ext) => ext.ToLowerInvariant() switch
    {
        "png"  => "image/png",
        "jpg"  => "image/jpeg",
        "jpeg" => "image/jpeg",
        "gif"  => "image/gif",
        "webp" => "image/webp",
        "svg"  => "image/svg+xml",
        "bmp"  => "image/bmp",
        "ico"  => "image/x-icon",
        "avif" => "image/avif",
        _      => "application/octet-stream",
    };
}
