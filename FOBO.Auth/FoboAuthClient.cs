using System.Net.Http.Json;
using Microsoft.Extensions.Logging;

namespace FOBO.Auth;

/// <summary>
/// The orchestrator a consumer talks to. Three entry points:
/// <list type="bullet">
///   <item><see cref="GetOrLoginAsync"/> — give me a valid access token, running
///         the interactive browser flow only if necessary (no saved tokens,
///         or refresh failed).</item>
///   <item><see cref="LoginInteractiveAsync"/> — force the browser flow, even
///         if tokens exist. Used by an explicit "sign in" button.</item>
///   <item><see cref="Logout"/> — delete stored tokens.</item>
/// </list>
///
/// <para>
/// Implements OAuth 2.0 Authorization Code + PKCE per RFC 7636 against the
/// Cognito Hosted UI. No client secret — native/CLI apps cannot protect one,
/// PKCE is the protection instead.
/// </para>
/// </summary>
public sealed class FoboAuthClient(
    OAuthOptions options,
    TokenStore store,
    HttpClient http,
    ILogger<FoboAuthClient>? logger = null)
{
    /// <summary>
    /// Returns a usable <see cref="TokenSet"/> — refreshes if expired, runs
    /// the interactive flow if no refresh token works. Writes fresh tokens
    /// back to the store on every successful refresh/login.
    /// </summary>
    public async Task<TokenSet> GetOrLoginAsync(CancellationToken ct = default)
    {
        var cached = store.Load();

        if (cached is not null && !cached.IsExpired(options.RefreshLeeway))
        {
            logger?.LogDebug("[FoboAuthClient] Using cached access token (expires {ExpiresAt})", cached.ExpiresAt);
            return cached;
        }

        if (cached is not null)
        {
            try
            {
                var refreshed = await RefreshAsync(cached.RefreshToken, ct);
                store.Save(refreshed);
                return refreshed;
            }
            catch (Exception ex)
            {
                logger?.LogWarning(ex, "[FoboAuthClient] Refresh failed — falling through to interactive login.");
            }
        }

        var fresh = await LoginInteractiveAsync(ct);
        store.Save(fresh);
        return fresh;
    }

    /// <summary>
    /// Always run the browser flow. Returns fresh tokens WITHOUT saving —
    /// caller decides whether to persist (useful for "add another account"
    /// flows that don't replace the default session).
    /// </summary>
    public async Task<TokenSet> LoginInteractiveAsync(CancellationToken ct = default)
    {
        var (verifier, challenge) = PkceHelper.Generate();
        var state = PkceHelper.GenerateState();

        var authorizeUrl = BuildAuthorizeUrl(challenge, state);
        logger?.LogInformation("[FoboAuthClient] Opening browser for sign-in: {Url}", authorizeUrl);

        using var listener = new LoopbackListener(options.CallbackPort);
        using var timeout  = new CancellationTokenSource(options.LoginTimeout);
        using var linked   = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        var callbackTask = listener.WaitForCallbackAsync(linked.Token);

        BrowserLauncher.Open(authorizeUrl);

        var query = await callbackTask;

        var returnedState = query["state"];
        if (!string.Equals(returnedState, state, StringComparison.Ordinal))
            throw new InvalidOperationException("OAuth state mismatch — possible CSRF, ignoring callback.");

        var error = query["error"];
        if (!string.IsNullOrEmpty(error))
            throw new InvalidOperationException($"Authorization denied: {error} — {query["error_description"]}");

        var code = query["code"]
                   ?? throw new InvalidOperationException("Authorization callback missing 'code' parameter.");

        return await ExchangeCodeForTokensAsync(code, verifier, ct);
    }

    /// <summary>
    /// Use an existing refresh token to get a new access + id token without
    /// any UI. Cognito rotates the refresh token only if refresh-token
    /// rotation is enabled on the app client; otherwise we reuse the incoming one.
    /// </summary>
    public async Task<TokenSet> RefreshAsync(string refreshToken, CancellationToken ct = default)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "refresh_token",
            ["client_id"]     = options.ClientId,
            ["refresh_token"] = refreshToken,
        });

        using var response = await http.PostAsync($"{options.HostedUiDomain}/oauth2/token", form, ct);
        await EnsureTokenEndpointOkAsync(response, ct);

        var body = await response.Content.ReadFromJsonAsync(AuthJsonContext.Default.TokenEndpointResponse, ct)
                   ?? throw new InvalidOperationException("Empty response from token endpoint.");

        return new TokenSet(
            AccessToken:  body.access_token,
            IdToken:      body.id_token,
            RefreshToken: body.refresh_token ?? refreshToken, // Cognito omits when no rotation
            ExpiresAt:    DateTimeOffset.UtcNow.AddSeconds(body.expires_in));
    }

    /// <summary>Delete the stored session. Does not revoke the token on Cognito's side.</summary>
    public void Logout() => store.Clear();

    // ── internals ─────────────────────────────────────────────────────

    private string BuildAuthorizeUrl(string codeChallenge, string state)
    {
        var qs = new Dictionary<string, string>
        {
            ["client_id"]             = options.ClientId,
            ["response_type"]         = "code",
            ["scope"]                 = string.Join(' ', options.Scopes),
            ["redirect_uri"]          = options.RedirectUri,
            ["code_challenge"]        = codeChallenge,
            ["code_challenge_method"] = "S256",
            ["state"]                 = state,
        };

        var encoded = string.Join('&',
            qs.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{options.HostedUiDomain}/oauth2/authorize?{encoded}";
    }

    private async Task<TokenSet> ExchangeCodeForTokensAsync(string code, string verifier, CancellationToken ct)
    {
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"]    = "authorization_code",
            ["client_id"]     = options.ClientId,
            ["code"]          = code,
            ["redirect_uri"]  = options.RedirectUri,
            ["code_verifier"] = verifier,
        });

        using var response = await http.PostAsync($"{options.HostedUiDomain}/oauth2/token", form, ct);
        await EnsureTokenEndpointOkAsync(response, ct);

        var body = await response.Content.ReadFromJsonAsync(AuthJsonContext.Default.TokenEndpointResponse, ct)
                   ?? throw new InvalidOperationException("Empty response from token endpoint.");

        return new TokenSet(
            AccessToken:  body.access_token,
            IdToken:      body.id_token,
            RefreshToken: body.refresh_token
                          ?? throw new InvalidOperationException("Token endpoint returned no refresh_token."),
            ExpiresAt:    DateTimeOffset.UtcNow.AddSeconds(body.expires_in));
    }

    private static async Task EnsureTokenEndpointOkAsync(HttpResponseMessage response, CancellationToken ct)
    {
        if (response.IsSuccessStatusCode) return;

        var detail = await response.Content.ReadAsStringAsync(ct);
        throw new InvalidOperationException(
            $"Token endpoint returned {(int)response.StatusCode} {response.ReasonPhrase}: {detail}");
    }

}

// Shape returned by /oauth2/token — loose on refresh_token since Cognito
// omits it when rotation is disabled. Internal so the source-gen
// JsonSerializerContext (AuthJsonContext) can reach it; not part of the
// public API.
// ReSharper disable InconsistentNaming
internal sealed record TokenEndpointResponse(
    string access_token,
    string id_token,
    string? refresh_token,
    int expires_in,
    string token_type);
// ReSharper restore InconsistentNaming
