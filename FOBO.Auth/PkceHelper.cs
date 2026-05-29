using System.Security.Cryptography;
using System.Text;

namespace FOBO.Auth;

/// <summary>
/// PKCE (RFC 7636) verifier + challenge generation. Native/CLI apps cannot
/// protect a client secret, so they use PKCE instead: client mints a random
/// verifier, sends <c>SHA256(verifier)</c> as the challenge on the authorize
/// request, and includes the verifier in the token exchange — preventing
/// code-interception attacks even on public clients.
/// </summary>
public static class PkceHelper
{
    /// <summary>
    /// Generates a fresh verifier + its challenge. Pass both to
    /// <see cref="FoboAuthClient"/> (verifier held locally, challenge sent
    /// to Cognito).
    /// </summary>
    public static (string Verifier, string Challenge) Generate()
    {
        // RFC 7636 recommends 43–128 chars for the verifier; 32 random bytes
        // base64url-encoded produces 43 chars, the minimum that still carries
        // a full 256 bits of entropy.
        var bytes = RandomNumberGenerator.GetBytes(32);
        var verifier = Base64Url(bytes);

        var challengeBytes = SHA256.HashData(Encoding.ASCII.GetBytes(verifier));
        var challenge = Base64Url(challengeBytes);

        return (verifier, challenge);
    }

    /// <summary>Random URL-safe state token for CSRF protection on the auth request.</summary>
    public static string GenerateState() => Base64Url(RandomNumberGenerator.GetBytes(16));

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
