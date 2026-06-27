namespace DataMaker.Schema.Fields;

/// <summary>
/// The rendering surfaces that DataMaker authors a single form for. Each
/// surface has its own native widget vocabulary, so a field type that's
/// trivially editable in one (e.g. an image picker in the Uno app) might
/// be unsupported in another (e.g. PDF can't host an image-upload action).
/// </summary>
public enum RendererSurface
{
    /// <summary>The in-app form rendered by <c>DataMaker.Forms.Renderer.Uno.FormRenderer</c>.</summary>
    UnoApp,

    /// <summary>Fillable AcroForm PDF produced by <c>DataMaker.Forms.Pdf.PdfFormGenerator</c>.</summary>
    PdfExport,

    /// <summary>TUI form rendered by <c>DataMaker.Forms.Renderer.Terminal.Forms.FormRenderer</c>.</summary>
    Terminal,

    /// <summary>Browser form rendered by <c>DataMaker.Forms.Renderer.Web</c> (wwwroot/renderer.js) — the web embed / WordPress / recipient-URL surface.</summary>
    Web,
}

/// <summary>
/// How well a given renderer supports a given field kind. The four levels
/// trade fidelity against effort for the publisher: <see cref="Native"/> is
/// "captures exactly the value the field models", <see cref="Unsupported"/>
/// is "skip this field entirely on this surface."
/// </summary>
public enum FieldSupport
{
    /// <summary>First-class widget that captures the typed value losslessly.</summary>
    Native,

    /// <summary>Renders, but with reduced fidelity (e.g. PDF stores a List as newline-joined text).</summary>
    Limited,

    /// <summary>Visible but read-only — the user must fill it in another renderer.</summary>
    Placeholder,

    /// <summary>Hidden / not rendered at all on this surface.</summary>
    Unsupported,
}

/// <summary>
/// One cell of the renderer × field-kind matrix. <see cref="Note"/> is the
/// short, user-facing reason — it's surfaced in PDF placeholder text, in
/// Terminal "unsupported binding" rows, and (when we add it) in the form
/// designer's per-field warning chips.
/// </summary>
public sealed record RendererCapability(string FieldKind, FieldSupport Support, string Note);

/// <summary>
/// Single source of truth for "which field kinds work in which renderer."
/// Each renderer queries this matrix instead of hard-coding its own
/// supported / unsupported lists, so adding a new field type forces an
/// explicit declaration of its support level on every surface (the test
/// in <c>DataMaker.Tests.Schema.RendererCapabilitiesTests</c> guards drift).
///
/// <para>
/// When you add a new <see cref="FieldTypes"/> constant, you MUST also
/// add a <see cref="RendererCapability"/> entry for it under every
/// <see cref="RendererSurface"/>. The build still compiles if you forget,
/// but the matrix test will fail loudly.
/// </para>
/// </summary>
public static class RendererCapabilities
{
    /// <summary>
    /// Per-surface support tables. Order within a surface mirrors the
    /// declaration order in <see cref="FieldTypes"/> so visual diffs of
    /// this file map cleanly to additions/removals there.
    /// </summary>
    public static IReadOnlyDictionary<RendererSurface, IReadOnlyList<RendererCapability>> Matrix { get; } =
        new Dictionary<RendererSurface, IReadOnlyList<RendererCapability>>
        {
            // ── Uno desktop app — the reference renderer. Everything is
            //    Native except Relation, which currently routes to a
            //    single-line TextBox until the relation picker lands.
            [RendererSurface.UnoApp] = new RendererCapability[]
            {
                new(FieldTypes.Text,        FieldSupport.Native,      ""),
                new(FieldTypes.LongText,    FieldSupport.Native,      ""),
                new(FieldTypes.RichText,    FieldSupport.Native,      ""),
                new(FieldTypes.Number,      FieldSupport.Native,      ""),
                new(FieldTypes.Decimal,     FieldSupport.Native,      ""),
                new(FieldTypes.Money,       FieldSupport.Native,      ""),
                new(FieldTypes.Date,        FieldSupport.Native,      ""),
                new(FieldTypes.DateTime,    FieldSupport.Native,      ""),
                new(FieldTypes.Boolean,     FieldSupport.Native,      ""),
                new(FieldTypes.Scale,       FieldSupport.Native,      ""),
                new(FieldTypes.Choice,      FieldSupport.Native,      ""),
                new(FieldTypes.MultiChoice, FieldSupport.Native,      ""),
                new(FieldTypes.List,        FieldSupport.Native,      ""),
                new(FieldTypes.Email,       FieldSupport.Native,      ""),
                new(FieldTypes.Phone,       FieldSupport.Native,      ""),
                new(FieldTypes.Url,         FieldSupport.Native,      ""),
                new(FieldTypes.Geo,         FieldSupport.Native,      ""),
                new(FieldTypes.Image,       FieldSupport.Native,      ""),
                new(FieldTypes.Attachment,  FieldSupport.Native,      ""),
                new(FieldTypes.Signature,   FieldSupport.Native,      ""),
                new(FieldTypes.Initials,    FieldSupport.Native,      ""),
                new(FieldTypes.Relation,    FieldSupport.Limited,     "shown as plain text — relation picker not yet implemented"),
            },

            // ── PDF export. AcroForm widgets cover text/number/checkbox/
            //    combo cleanly; uploads and pickers obviously can't work
            //    on paper, so they ride along as read-only placeholder
            //    text fields prompting the user to fill that value in
            //    the app.
            [RendererSurface.PdfExport] = new RendererCapability[]
            {
                new(FieldTypes.Text,        FieldSupport.Native,      ""),
                new(FieldTypes.LongText,    FieldSupport.Native,      ""),
                new(FieldTypes.RichText,    FieldSupport.Limited,     "AcroForm /Tx with the RichText flag is emitted, but the PDF generator currently writes plain text — formatting is lost on export"),
                new(FieldTypes.Number,      FieldSupport.Native,      ""),
                new(FieldTypes.Decimal,     FieldSupport.Native,      ""),
                new(FieldTypes.Money,       FieldSupport.Native,      ""),
                new(FieldTypes.Date,        FieldSupport.Native,      ""),
                new(FieldTypes.DateTime,    FieldSupport.Native,      ""),
                new(FieldTypes.Boolean,     FieldSupport.Native,      ""),
                new(FieldTypes.Scale,       FieldSupport.Limited,     "the interactive figure row (stars / shapes) can't be a PDF widget — exported as a dropdown of its numeric points, with the anchor labels folded into the end options"),
                new(FieldTypes.Choice,      FieldSupport.Native,      ""),
                new(FieldTypes.MultiChoice, FieldSupport.Native,      ""),
                new(FieldTypes.List,        FieldSupport.Limited,     "stored as newline-joined text — array structure is reconstructed on import"),
                new(FieldTypes.Email,       FieldSupport.Native,      ""),
                new(FieldTypes.Phone,       FieldSupport.Native,      ""),
                new(FieldTypes.Url,         FieldSupport.Native,      ""),
                new(FieldTypes.Geo,         FieldSupport.Limited,     "captured as a writable address line — the map / lat-lng pin can't render on the PDF"),
                new(FieldTypes.Image,       FieldSupport.Placeholder, "image upload isn't possible on paper — fill in the app"),
                new(FieldTypes.Attachment,  FieldSupport.Placeholder, "file attachments aren't possible on paper — fill in the app"),
                new(FieldTypes.Signature,   FieldSupport.Native,      ""),
                new(FieldTypes.Initials,    FieldSupport.Native,      ""),
                new(FieldTypes.Relation,    FieldSupport.Placeholder, "relation lookups aren't possible on paper — fill in the app"),
            },

            // ── Terminal (Terminal.Gui v1). Anything that needs a file
            //    picker or map widget is unsupported — there's no UI
            //    primitive for it. The field still appears in tab order
            //    so it's discoverable, but is read-only.
            [RendererSurface.Terminal] = new RendererCapability[]
            {
                new(FieldTypes.Text,        FieldSupport.Native,      ""),
                new(FieldTypes.LongText,    FieldSupport.Native,      ""),
                new(FieldTypes.RichText,    FieldSupport.Limited,     "raw Markdown editor in the terminal — formatting (bold / italic / headings) renders only on web / uno / pdf consumers"),
                new(FieldTypes.Number,      FieldSupport.Native,      ""),
                new(FieldTypes.Decimal,     FieldSupport.Native,      ""),
                new(FieldTypes.Money,       FieldSupport.Native,      ""),
                new(FieldTypes.Date,        FieldSupport.Native,      ""),
                new(FieldTypes.DateTime,    FieldSupport.Native,      ""),
                new(FieldTypes.Boolean,     FieldSupport.Native,      ""),
                new(FieldTypes.Scale,       FieldSupport.Limited,     "captured as a radio list of the numeric points (anchor labels folded into the ends) — the star / shape figure row doesn't render in the terminal, but the value is identical"),
                new(FieldTypes.Choice,      FieldSupport.Native,      ""),
                new(FieldTypes.MultiChoice, FieldSupport.Native,      ""),
                new(FieldTypes.List,        FieldSupport.Native,      ""),
                new(FieldTypes.Email,       FieldSupport.Native,      ""),
                new(FieldTypes.Phone,       FieldSupport.Native,      ""),
                new(FieldTypes.Url,         FieldSupport.Native,      ""),
                new(FieldTypes.Geo,         FieldSupport.Native,      ""),
                new(FieldTypes.Image,       FieldSupport.Native,      ""),
                new(FieldTypes.Attachment,  FieldSupport.Native,      ""),
                new(FieldTypes.Signature,   FieldSupport.Native,      ""),
                new(FieldTypes.Initials,    FieldSupport.Native,      ""),
                new(FieldTypes.Relation,    FieldSupport.Unsupported, "relation picker not supported in terminal"),
            },

            // ── Web (renderer.js). Captures every input kind natively,
            //    including choice allow-custom (editable datalist) and number
            //    min/max bounds. Relation columns are dropped entirely — there
            //    is no record-lookup picker in the browser yet. NOTE: multi-step
            //    forms render as a single flat page (no wizard navigation), and
            //    per-column StackBelowPx is honored; both are layout behaviours,
            //    not field-kind support, so they aren't expressed in this matrix.
            [RendererSurface.Web] = new RendererCapability[]
            {
                new(FieldTypes.Text,        FieldSupport.Native,      ""),
                new(FieldTypes.LongText,    FieldSupport.Native,      ""),
                new(FieldTypes.RichText,    FieldSupport.Native,      ""),
                new(FieldTypes.Number,      FieldSupport.Native,      ""),
                new(FieldTypes.Decimal,     FieldSupport.Native,      ""),
                new(FieldTypes.Money,       FieldSupport.Native,      ""),
                new(FieldTypes.Date,        FieldSupport.Native,      ""),
                new(FieldTypes.DateTime,    FieldSupport.Native,      ""),
                new(FieldTypes.Boolean,     FieldSupport.Native,      ""),
                new(FieldTypes.Scale,       FieldSupport.Native,      ""),
                new(FieldTypes.Choice,      FieldSupport.Native,      ""),
                new(FieldTypes.MultiChoice, FieldSupport.Native,      ""),
                new(FieldTypes.List,        FieldSupport.Native,      ""),
                new(FieldTypes.Email,       FieldSupport.Native,      ""),
                new(FieldTypes.Phone,       FieldSupport.Native,      ""),
                new(FieldTypes.Url,         FieldSupport.Native,      ""),
                new(FieldTypes.Geo,         FieldSupport.Native,      ""),
                new(FieldTypes.Image,       FieldSupport.Native,      ""),
                new(FieldTypes.Attachment,  FieldSupport.Native,      ""),
                new(FieldTypes.Signature,   FieldSupport.Native,      ""),
                new(FieldTypes.Initials,    FieldSupport.Native,      ""),
                new(FieldTypes.Relation,    FieldSupport.Unsupported, "relation columns are dropped — the web renderer has no record-lookup picker; capture in the app"),
            },
        };

    /// <summary>
    /// Look up the support level for a (surface, field-kind) pair. Unknown
    /// kinds (custom field types not in <see cref="FieldTypes"/>) return
    /// <see cref="FieldSupport.Native"/> with no note — registered custom
    /// editors are the host's responsibility, not the matrix's.
    /// </summary>
    public static RendererCapability For(RendererSurface surface, string fieldKind)
    {
        if (Matrix.TryGetValue(surface, out var entries))
            foreach (var c in entries)
                if (c.FieldKind == fieldKind) return c;

        return new RendererCapability(fieldKind, FieldSupport.Native, "");
    }

    /// <summary>Convenience: just the support level.</summary>
    public static FieldSupport Support(RendererSurface surface, string fieldKind) =>
        For(surface, fieldKind).Support;

    /// <summary>
    /// Every field kind whose support level is anything other than
    /// <see cref="FieldSupport.Native"/> on the given surface — the set
    /// the form designer should warn the publisher about when a form is
    /// targeted at that surface.
    /// </summary>
    public static IReadOnlyList<RendererCapability> NonNative(RendererSurface surface) =>
        Matrix.TryGetValue(surface, out var entries)
            ? entries.Where(c => c.Support != FieldSupport.Native).ToArray()
            : Array.Empty<RendererCapability>();
}
