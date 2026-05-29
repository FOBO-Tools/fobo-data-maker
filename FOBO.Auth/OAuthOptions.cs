namespace FOBO.Auth;

/// <summary>
/// Configuration for the FOBO OAuth client. One instance per app — DataMaker
/// (desktop) and the DataMaker Terminal each create their own using the
/// shared Cognito pool + their own Cognito App Client id.
/// </summary>
public sealed record OAuthOptions
{
    /// <summary>Issuer for JWKS + token validation. e.g. <c>https://cognito-idp.eu-west-1.amazonaws.com/eu-west-1_sKuDMzzoN</c>.</summary>
    public required string Authority { get; init; }

    /// <summary>Cognito Hosted UI domain for the /oauth2/authorize + /oauth2/token endpoints. e.g. <c>https://auth.fobo-tools.com</c>.</summary>
    public required string HostedUiDomain { get; init; }

    /// <summary>App-specific Cognito client id. Public — no secret (native/CLI clients shouldn't carry one).</summary>
    public required string ClientId { get; init; }

    /// <summary>Scopes to request. Typically <c>[openid, email, profile]</c>.</summary>
    public required string[] Scopes { get; init; }

    /// <summary>Fixed loopback port the loopback listener binds to. Must be registered on the App Client's callback URL list.</summary>
    public required int CallbackPort { get; init; }

    /// <summary>Path portion of the callback URL. Paired with <see cref="CallbackPort"/>.</summary>
    public string CallbackPath { get; init; } = "/callback";

    /// <summary>Where persisted tokens live. Typically <c>{LocalAppData}/FOBO/{AppName}/tokens.json</c>.</summary>
    public required string TokenStorePath { get; init; }

    /// <summary>How long to wait for the user to complete the browser-side login before abandoning. Default 5 min.</summary>
    public TimeSpan LoginTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Refresh tokens this many seconds BEFORE the access token actually expires, to avoid race-into-401.</summary>
    public TimeSpan RefreshLeeway { get; init; } = TimeSpan.FromSeconds(60);

    /// <summary>Full loopback URL the browser will redirect to after auth.</summary>
    public string RedirectUri => $"http://127.0.0.1:{CallbackPort}{CallbackPath}";
}
