namespace DataMaker.Forms.Renderer.Terminal.Forms.Bindings;

/// <summary>
/// Ambient context for upload-capable bindings (image / attachment).
/// FormWindow sets <see cref="Current"/> before <see cref="FormRenderer.Render"/>;
/// bindings that need to hit the upload-slot API read it on demand.
///
/// <para>Static singleton because the terminal renderer is strictly one
/// form at a time — there's no second concurrent FormWindow that could
/// collide. Avoids threading a context object through every
/// <see cref="FieldBindingFactory"/> call.</para>
/// </summary>
internal sealed class UploadContext
{
    public required string RecipientUserId { get; init; }
    public required string SubmitEndpointBase { get; init; }   // already trailing-slash-normalised
    public required HttpClient Http { get; init; }

    /// <summary>
    /// Recipient's raw X25519 public key — used to E2E-seal image/attachment
    /// bytes before the direct-to-S3 PUT (#45). Always present when
    /// <see cref="Current"/> is set: share-only bundles (no recipient) leave
    /// Current null entirely, so the upload path never runs without it.
    /// </summary>
    public required byte[] RecipientPublicKey { get; init; }

    public static UploadContext? Current { get; set; }
}
