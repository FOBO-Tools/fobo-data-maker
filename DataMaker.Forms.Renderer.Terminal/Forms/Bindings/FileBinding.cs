using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Shared binding for <see cref="FieldTypes.Image"/> +
/// <see cref="FieldTypes.Attachment"/>. Both kinds store the same
/// <c>{DataUri?, Url?, FileName, Mime, SizeBytes, Hash, Owned}</c>
/// shape — only the renderer-side display differs (we don't show image
/// previews in the terminal). One class, kind flag.
///
/// <para>Editor row: filename label + "Pick…" button + status label.
/// Pick opens a Terminal.Gui <see cref="FileDialog"/>, computes SHA-256
/// of the bytes, POSTs <c>/submissions/upload-slot</c> on the sync
/// Lambda to get a pre-signed PUT URL, uploads the bytes to S3, and
/// stores an <see cref="ImageRef"/> / <see cref="AttachmentRef"/> with
/// <c>Url + Hash + Owned=true</c> (no inline DataUri — matches the
/// URL-only wire shape from #18a).</para>
///
/// <para>Requires <see cref="UploadContext.Current"/> to be set
/// (FormWindow does this before render). A share-only .dmf (no
/// recipient) renders the binding read-only with a clear error so the
/// user knows why the picker is disabled.</para>
/// </summary>
internal sealed class FileBinding : FieldBinding
{
    private readonly bool _isImage;
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _editor;
    private readonly Label _nameLabel;
    private readonly Label _statusLabel;
    private readonly Label _errorIndicator = CreateErrorIndicator();
    private readonly Button _pickBtn;

    public FileBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        _isImage = definition.Kind == FieldTypes.Image;
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        _editor = new View { Width = Dim.Fill(), Height = 1 };
        _nameLabel = new Label(RenderName(State.Get(definition.Name)))
        {
            X = 0, Y = 0, Width = Dim.Fill() - 28, Height = 1,
        };
        _pickBtn = new Button(_isImage ? "Set image" : "Set file")
        {
            X = Pos.Right(_nameLabel) + 1, Y = 0,
        };
        _pickBtn.Clicked += () => _ = PickAndUploadAsync();
        _statusLabel = new Label("")
        {
            X = 0, Y = 1, Width = Dim.Fill(), Height = 0,
        };
        _editor.Add(_nameLabel, _pickBtn, _statusLabel);

        if (UploadContext.Current is null)
        {
            // Share-only .dmf — no recipient ⇒ no upload-slot target.
            _pickBtn.Enabled = false;
            _statusLabel.Text   = "(share-only form — uploads disabled)";
            _statusLabel.Height = 1;
        }
    }

    private async Task PickAndUploadAsync()
    {
        var ctx = UploadContext.Current;
        if (ctx is null) return;

        // Terminal.Gui FileDialog. OpenDialog auto-restricts to a single
        // selection; AllowedFileTypes narrows by extension when set.
        var allowedExts = _isImage
            ? new List<string> { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" }
            : new List<string>();   // empty = any file (FileDialog default)
        var dlg = new OpenDialog(_isImage ? "Pick image" : "Pick file", "Choose a file to upload")
        {
            CanChooseDirectories = false,
            CanChooseFiles       = true,
            AllowsMultipleSelection = false,
            AllowedFileTypes     = allowedExts.Count > 0 ? allowedExts.ToArray() : null,
        };
        Application.Run(dlg);
        if (dlg.Canceled || dlg.FilePath is null) return;

        var path = dlg.FilePath.ToString();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ShowStatus("File not found.", isError: true);
            return;
        }

        try
        {
            SetUploading(true, $"Reading {Path.GetFileName(path)}…");
            var bytes  = await File.ReadAllBytesAsync(path);
            var hash   = ComputeSha256Hex(bytes);   // over PLAINTEXT — stays the S3 key
            var mime   = GuessMime(path);
            var name   = Path.GetFileName(path);
            var size   = bytes.LongLength;          // plaintext size (UX / saved ref)

            // E2E-seal the bytes against the recipient pubkey before they
            // leave for S3 (#45). Magic + crypto_box_seal — only the form
            // owner's desktop opens it. The slot size below is the ciphertext
            // length (what's actually PUT).
            var sealed_ = DataMaker.Sdk.SealedBox.SealBlob(bytes, ctx.RecipientPublicKey);

            ShowStatus($"Uploading ({FormatBytes(size)})…", isError: false);

            // POST /submissions/upload-slot → { url, key, expiresAtUtc }.
            var slot = await PostUploadSlotAsync(ctx, hash, mime, sealed_.LongLength);
            if (slot is null)
            {
                ShowStatus("Upload-slot request failed.", isError: true);
                SetUploading(false, "");
                return;
            }

            // PUT the sealed bytes to the pre-signed URL.
            using var content = new ByteArrayContent(sealed_);
            if (!string.IsNullOrEmpty(mime))
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mime);
            using var put = new HttpRequestMessage(HttpMethod.Put, slot.Url) { Content = content };
            var resp = await ctx.Http.SendAsync(put);
            if (!resp.IsSuccessStatusCode)
            {
                ShowStatus($"S3 PUT failed: {(int)resp.StatusCode}", isError: true);
                SetUploading(false, "");
                return;
            }

            // Build the URL-only ref. No DataUri — bytes live in S3.
            // Strip the pre-signed query string so the saved URL is the
            // canonical, stable object URL (mirrors what the web
            // renderer does — see DataMaker.Forms.Renderer.Web/wwwroot/
            // renderer.js uploadFileToSlot canonicalUrl). The receiver
            // mints a fresh pre-signed GET via the sync Lambda's
            // /submissions/blob/{userId}/{hash} endpoint when it needs
            // to actually fetch the bytes.
            var canonicalUrl = slot.Url.Split('?')[0];
            object value = _isImage
                ? (object)new ImageRef(
                    DataUri: null, FileName: name, Mime: mime,
                    SizeBytes: size, Url: canonicalUrl, Hash: hash, Owned: true)
                : new AttachmentRef(
                    DataUri: null, FileName: name, Mime: mime,
                    SizeBytes: size, Url: canonicalUrl, Hash: hash, Owned: true);
            State.Set(Definition.Name, value);

            _nameLabel.Text = RenderName(value);
            ShowStatus($"Uploaded ✓ ({FormatBytes(size)})", isError: false);
            SetUploading(false, _statusLabel.Text.ToString() ?? "");
        }
        catch (Exception ex)
        {
            ShowStatus($"Upload failed: {ex.Message}", isError: true);
            SetUploading(false, "");
        }
    }

    private static async Task<UploadSlotResponse?> PostUploadSlotAsync(
        UploadContext ctx, string hash, string? mime, long size)
    {
        var req = new UploadSlotRequest(ctx.RecipientUserId, hash, mime, size);
        var url = new Uri(new Uri(ctx.SubmitEndpointBase), "submissions/upload-slot");
        // Use source-generated JsonContext under trim/AOT — see
        // TerminalJsonContext for the registered types.
        using var resp = await ctx.Http.PostAsJsonAsync(
            url, req, DataMaker.Forms.Renderer.Terminal.TerminalJsonContext.Default.UploadSlotRequest);
        if (!resp.IsSuccessStatusCode) return null;
        return await resp.Content.ReadFromJsonAsync(
            DataMaker.Forms.Renderer.Terminal.TerminalJsonContext.Default.UploadSlotResponse);
    }

    private void SetUploading(bool busy, string status)
    {
        _pickBtn.Enabled    = !busy;
        _statusLabel.Text   = status;
        _statusLabel.Height = string.IsNullOrEmpty(status) ? 0 : 1;
    }

    private void ShowStatus(string text, bool isError)
    {
        _statusLabel.Text   = text;
        _statusLabel.Height = 1;
        // No special colour scheme; errors read fine in the default
        // dialog scheme. Future polish: red foreground for isError.
    }

    private static string RenderName(object? raw) => raw switch
    {
        ImageRef      i when !string.IsNullOrWhiteSpace(i.FileName) => i.FileName!,
        AttachmentRef a when !string.IsNullOrWhiteSpace(a.FileName) => a.FileName!,
        ImageRef                                                    => "(image)",
        AttachmentRef                                               => "(file)",
        _                                                           => "(no file)",
    };

    private static string ComputeSha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        var sb = new System.Text.StringBuilder(64);
        foreach (var b in hash) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }

    private static string? GuessMime(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png"  => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif"  => "image/gif",
        ".webp" => "image/webp",
        ".bmp"  => "image/bmp",
        ".pdf"  => "application/pdf",
        ".txt"  => "text/plain",
        ".json" => "application/json",
        ".csv"  => "text/csv",
        ".zip"  => "application/zip",
        _       => "application/octet-stream",
    };

    private static string FormatBytes(long b) => b switch
    {
        < 1024            => $"{b} B",
        < 1024L * 1024    => $"{b / 1024.0:0.#} KB",
        < 1024L * 1024 * 1024 => $"{b / 1024.0 / 1024.0:0.#} MB",
        _                 => $"{b / 1024.0 / 1024.0 / 1024.0:0.#} GB",
    };

    public override View Label => _label;
    public override View Editor => _editor;
    public override int EditorHeight => 2;   // row + optional status line
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}

/// <summary>
/// Wire shape for <c>POST /submissions/upload-slot</c>. Public for the
/// source-generated <see cref="TerminalJsonContext"/> to resolve.
/// Mirrors <c>SubmissionBlobsController.UploadSlotRequest</c>.
/// </summary>
public sealed record UploadSlotRequest(
    [property: JsonPropertyName("recipientUserId")] string RecipientUserId,
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("mime")] string? Mime,
    [property: JsonPropertyName("sizeBytes")] long? SizeBytes);

/// <summary>Response shape — pre-signed PUT URL + S3 key + ISO expiry.</summary>
public sealed record UploadSlotResponse(
    [property: JsonPropertyName("url")] string Url,
    [property: JsonPropertyName("key")] string Key,
    [property: JsonPropertyName("expiresAtUtc")] string ExpiresAtUtc);
