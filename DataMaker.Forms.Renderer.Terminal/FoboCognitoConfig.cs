using FOBO.Auth;

namespace DataMaker.Forms.Renderer.Terminal;

/// <summary>
/// Cognito OAuth configuration for the terminal. Mirror of
/// <c>DataMaker.Services.DataMakerCognito</c> over in the Uno app — same
/// Cognito pool + App Client ID, same flow. Duplicated deliberately rather
/// than factoring into a cross-solution shared lib for 4 strings; both
/// locations update together if the App Client is ever re-minted. See
/// <c>project_datamaker_cognito</c> memory.
/// </summary>
internal static class FoboCognitoConfig
{
    private const string UserPoolId     = "eu-west-1_sKuDMzzoN";
    private const string ClientId       = "441b6l13meiqteke4q9kcjuhch";
    private const string HostedUiDomain = "https://auth.fobo-tools.com";
    private const string Authority      = "https://cognito-idp.eu-west-1.amazonaws.com/" + UserPoolId;
    private const int    CallbackPort   = 53842;

    public static OAuthOptions CreateOptions() => new()
    {
        Authority      = Authority,
        HostedUiDomain = HostedUiDomain,
        ClientId       = ClientId,
        Scopes         = ["openid", "email", "profile"],
        CallbackPort   = CallbackPort,
        TokenStorePath = DefaultTokenStorePath(),
    };

    /// <summary>
    /// Per-user tokens file at <c>~/.datamaker-terminal/tokens.json</c> —
    /// sibling of the trust store so everything the terminal persists lives
    /// under one easy-to-find folder.
    /// </summary>
    public static string DefaultTokenStorePath() =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".datamaker-terminal",
            "tokens.json");
}
