using System.Text.Json.Serialization;

namespace FOBO.Auth;

/// <summary>
/// Tokens returned by Cognito on successful authorization-code exchange.
/// Persisted to disk and reloaded across app restarts. <see cref="IsExpired"/>
/// checks access-token expiry with a configurable leeway to avoid racing
/// into 401s right at the boundary.
/// </summary>
public sealed record TokenSet(
    [property: JsonPropertyName("accessToken")]  string AccessToken,
    [property: JsonPropertyName("idToken")]      string IdToken,
    [property: JsonPropertyName("refreshToken")] string RefreshToken,
    [property: JsonPropertyName("expiresAt")]    DateTimeOffset ExpiresAt)
{
    public bool IsExpired(TimeSpan leeway) =>
        DateTimeOffset.UtcNow >= ExpiresAt - leeway;
}
