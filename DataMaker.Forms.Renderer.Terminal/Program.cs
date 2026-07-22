using DataMaker.Forms.Signing;
using DataMaker.Schema.Forms;
using DataMaker.Forms.Renderer.Terminal;
using DataMaker.Forms.Renderer.Terminal.Forms;
using FOBO.Auth;
using Terminal.Gui;

// datamaker-form: TUI form renderer for Data Maker.
// Loads a .dmf bundle from a local file or HTTP URL, verifies its Ed25519
// signature + per-file SHA-256 hashes, prompts the user to trust the signer
// (first-use only), renders the form with full keyboard navigation, and
// submits the filled-in record as a sealed-box envelope to the form owner's
// Data Maker install via the shared /submissions Lambda.

if (args.Length == 0 || args[0] is "-h" or "--help")
{
    PrintUsage();
    return 0;
}

CliOptions options;
try
{
    options = CliOptions.Parse(args);
}
catch (ArgumentException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 1;
}

// Validate the template NAME now (fail fast with clean stderr before the TUI
// grabs the screen), but DON'T build its ColorScheme yet — that makes driver
// attributes and must wait until after Application.Init() below.
if (options.TemplateName is not null && !Templates.IsKnown(options.TemplateName))
{
    Console.Error.WriteLine($"Unknown template '{options.TemplateName}'. Known: {Templates.Known}.");
    return 1;
}

byte[] bundleBytes;
try
{
    bundleBytes = await FormLoader.LoadAsync(options.Source);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Could not load form from '{options.Source}': {ex.Message}");
    return 2;
}

VerifiedForm verified;
try
{
    verified = DmfReader.Read(bundleBytes);
}
catch (InvalidDmfException ex)
{
    Console.Error.WriteLine($"Bundle verification failed: {ex.Message}");
    return 3;
}

// Trust-on-first-use: prompt before rendering if we've never seen this signer.
// Skipped if the user passed --trust <fingerprint> matching what the envelope carries.
var trustStore = new TrustStore();
var trustDecision = trustStore.Evaluate(verified.SignerFingerprint, options.TrustOverride);

if (trustDecision == TrustDecision.PromptUser)
{
    if (!TrustPrompt.Confirm(verified))
    {
        Console.Error.WriteLine("Trust denied. Not rendering form.");
        return 4;
    }
    trustStore.Trust(verified.SignerFingerprint, verified.Form.Id);
}

// Pre-TUI auth gate. If the form's SubmitPolicy requires an authenticated
// submitter, run the FOBO OAuth flow BEFORE Terminal.Gui takes over the
// screen — otherwise the browser launch happens while the TUI owns stdin
// and the user has no feedback on what's happening.
//
// Result: if the form is anonymous we do nothing. If it's gated and tokens
// are cached + fresh, we silently hold them. If it's gated and the user
// needs to sign in, the browser opens here, the user completes sign-in,
// and then we launch the TUI.
TokenSet? bearer = null;
if (verified.Form.SubmitPolicy == SubmitPolicy.Authenticated)
{
    var authOptions = FoboCognitoConfig.CreateOptions();
    var tokenStore  = new TokenStore(authOptions.TokenStorePath);
    var authHttp    = new HttpClient();
    var authClient  = new FoboAuthClient(authOptions, tokenStore, authHttp);

    try
    {
        Console.WriteLine();
        Console.WriteLine("This form requires a FOBO account.");
        Console.WriteLine("Opening your browser to sign in…");
        Console.WriteLine();
        bearer = await authClient.GetOrLoginAsync();
        Console.WriteLine("Signed in. Loading form…");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Sign-in failed: {ex.Message}");
        return 5;
    }
}

// Launch the TUI with the default driver.
Application.Init();
try
{
    // Now that a driver exists, build the template scheme (makes driver
    // attributes — doing this before Init left them uninitialized).
    ColorScheme? templateScheme = null;
    if (options.TemplateName is not null)
        Templates.TryResolve(options.TemplateName, out templateScheme);

    // Brand splash — the embedded ASCII logo, dismissed on any key or after a
    // short beat — before the form window takes over. Coloured by the active
    // template so the logo is green in the green template, etc.
    SplashScreen.Show(templateScheme);

    // Submit endpoint precedence:
    //   1. --submit-endpoint CLI flag (explicit override for this run)
    //   2. DATAMAKER_SYNC_API env var (dev / staging override)
    //   3. Baked default below (normal shipping config — all FOBO users
    //      hit the same production relay Lambda)
    // Live URL of the data-maker-sync SyncFunction Lambda in eu-west-1.
    // Change only when the Lambda is redeployed to a different Function URL.
    const string DefaultSubmitEndpoint = "https://datamaker-api.fobo-tools.com/";
    var submitEndpoint = options.SubmitEndpoint
        ?? Environment.GetEnvironmentVariable("DATAMAKER_SYNC_API")
        ?? DefaultSubmitEndpoint;

    // Force a full redraw on terminal resize. v1 leaves partial artifacts on
    // some drivers (macOS Terminal especially) without an explicit refresh.
    Application.Resized += _ =>
    {
        Application.Top?.SetNeedsDisplay();
        Application.Refresh();
    };

    // Template active = override Terminal.Gui's own default schemes too, so
    // MessageBox / popups / menus blend in instead of flashing their default.
    if (templateScheme is not null)
    {
        Colors.Base   = templateScheme;
        Colors.Dialog = templateScheme;
        Colors.Menu   = templateScheme;
        Colors.Error  = templateScheme;
    }

    var window = new FormWindow(verified, submitEndpoint, templateScheme, bearer);
    Application.Run(window);
}
finally
{
    Application.Shutdown();
}

return 0;

static void PrintUsage()
{
    Console.WriteLine(
        $$"""
        datamaker-form — TUI form renderer for Data Maker

        Usage:
          datamaker-form <path-or-url> [options]

        Arguments:
          path-or-url           Local file or HTTP(S) URL of a signed form envelope

        Options:
          -t, --template <name> Override the form's own colors with a built-in
                                palette. Known: {{Templates.Known}}.
                                Without --template, the form's Style cascade renders.
          --trust <fingerprint> Skip the first-use trust prompt if the signer's
                                fingerprint matches. Typical values look like
                                'a1:b2:c3:d4:e5:f6:07:18'.
          --submit-endpoint <url>
                                Override the submission endpoint for this
                                run. Normally not needed — the production
                                FOBO relay is baked in. Can also be set via
                                the DATAMAKER_SYNC_API environment variable.
          -h, --help            Print this help.

        Examples:
          datamaker-form ./customer-feedback.dmf
          datamaker-form ./customer-feedback.dmf -t green
          datamaker-form https://forms.example.com/customer-feedback.dmf \
                         --trust a1:b2:c3:d4:e5:f6:07:18
        """);
}

namespace DataMaker.Forms.Renderer.Terminal
{
    internal sealed record CliOptions(
        string  Source,
        string? TrustOverride,
        string? SubmitEndpoint,
        string? TemplateName)
    {
        public static CliOptions Parse(string[] args)
        {
            string? source         = null;
            string? trust          = null;
            string? submitEndpoint = null;
            string? template       = null;

            for (var i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--trust" when i + 1 < args.Length:
                        trust = args[++i];
                        break;
                    case "--submit-endpoint" when i + 1 < args.Length:
                        submitEndpoint = args[++i];
                        break;
                    case "-t" or "--template" when i + 1 < args.Length:
                        template = args[++i];
                        break;
                    default:
                        source ??= args[i];
                        break;
                }
            }

            if (string.IsNullOrWhiteSpace(source))
                throw new ArgumentException("A form path or URL is required.");

            return new CliOptions(source, trust, submitEndpoint, template);
        }
    }

    /// <summary>
    /// Loads the raw bytes of a <c>.dmf</c> bundle from a local file or
    /// HTTP(S) URL. Both transports return the same byte stream — trust
    /// is rooted in the signer key inside the bundle, not the transport,
    /// so this loader stays transport-blind by design.
    /// </summary>
    internal static class FormLoader
    {
        private static readonly HttpClient _http = new();

        public static async Task<byte[]> LoadAsync(string source, CancellationToken ct = default)
        {
            if (IsUrl(source))
                return await _http.GetByteArrayAsync(source, ct);

            return await File.ReadAllBytesAsync(source, ct);
        }

        private static bool IsUrl(string s) =>
            Uri.TryCreate(s, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
    }
}
