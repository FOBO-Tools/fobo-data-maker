using DataMaker.Schema.Fields;
using Terminal.Gui;

namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Binding for <see cref="FieldTypes.Signature"/> + <see cref="FieldTypes.Initials"/>.
/// A terminal has no ink surface, so the signer either <b>types their name</b>
/// (stored as <see cref="SignatureRef.TypedName"/>) or <b>picks a signature
/// image file</b> (uploaded exactly like <see cref="FileBinding"/>, stored as
/// a <see cref="SignatureRef"/> with <c>Url + Hash + Owned</c>). Whichever the
/// user does last wins.
///
/// <para>The image upload reuses <see cref="FileBinding"/>'s sealed-upload
/// helpers and needs <see cref="UploadContext.Current"/>; a share-only .dmf
/// disables the picker but the typed-name field still works.</para>
/// </summary>
internal sealed class SignatureBinding : FieldBinding
{
    private readonly View _label;
    private readonly Label? _asterisk;
    private readonly View _editor;
    private readonly TextField _nameField;
    private readonly Button _pickBtn;
    private readonly Label _statusLabel;
    private readonly Label _errorIndicator = CreateErrorIndicator();

    public SignatureBinding(FieldDefinition definition, FormState state) : base(definition, state)
    {
        (_label, _asterisk) = RequiredLabel.Build(definition.Label, definition.Required);

        var initial = State.Get(definition.Name) as SignatureRef?;
        _editor = new View { Width = Dim.Fill(), Height = 1 };
        _nameField = new TextField(initial?.TypedName ?? "")
        {
            X = 0, Y = 0, Width = Dim.Fill() - 20, Height = 1,
        };
        _nameField.TextChanged += _ =>
        {
            var typed = _nameField.Text.ToString() ?? "";
            State.Set(definition.Name, string.IsNullOrWhiteSpace(typed)
                ? null
                : new SignatureRef(TypedName: typed));
        };

        _pickBtn = new Button("Use file")
        {
            X = Pos.Right(_nameField) + 1, Y = 0,
        };
        _pickBtn.Clicked += () => _ = PickAndUploadAsync();

        _statusLabel = new Label("")
        {
            X = 0, Y = 1, Width = Dim.Fill(), Height = 0,
        };
        _editor.Add(_nameField, _pickBtn, _statusLabel);

        if (initial is { } sig && (sig.Url is not null || sig.DataUri is not null))
            ShowStatus("(signature image set — type a name to replace)", isError: false);

        if (UploadContext.Current is null)
        {
            _pickBtn.Enabled = false;
            // Typed-name still works share-only; the image picker can't reach a slot.
        }
    }

    private async Task PickAndUploadAsync()
    {
        var ctx = UploadContext.Current;
        if (ctx is null) return;

        var dlg = new OpenDialog("Pick signature image", "Choose a signature image to upload")
        {
            CanChooseDirectories = false,
            CanChooseFiles       = true,
            AllowsMultipleSelection = false,
            AllowedFileTypes     = new[] { ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp" },
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
            var bytes = await File.ReadAllBytesAsync(path);
            var hash  = FileBinding.ComputeSha256Hex(bytes);
            var mime  = FileBinding.GuessMime(path);
            var name  = Path.GetFileName(path);
            var size  = bytes.LongLength;

            var sealed_ = DataMaker.Sdk.SealedBox.SealBlob(bytes, ctx.RecipientPublicKey);
            ShowStatus($"Uploading ({FileBinding.FormatBytes(size)})…", isError: false);

            var slot = await FileBinding.PostUploadSlotAsync(ctx, hash, mime, sealed_.LongLength);
            if (slot is null)
            {
                ShowStatus("Upload-slot request failed.", isError: true);
                SetUploading(false, "");
                return;
            }

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

            var canonicalUrl = slot.Url.Split('?')[0];
            State.Set(Definition.Name, new SignatureRef(
                DataUri: null, Mime: mime, SizeBytes: size,
                Url: canonicalUrl, Hash: hash, Owned: true));

            _nameField.Text = "";   // image wins over any typed name
            ShowStatus($"Signature uploaded ✓ ({name})", isError: false);
            SetUploading(false, _statusLabel.Text.ToString() ?? "");
        }
        catch (Exception ex)
        {
            ShowStatus($"Upload failed: {ex.Message}", isError: true);
            SetUploading(false, "");
        }
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
    }

    public override View Label => _label;
    public override View Editor => _editor;
    public override int EditorHeight => 2;
    public override Label? RequiredAsterisk => _asterisk;
    public override Label? ErrorIndicator => _errorIndicator;
}
