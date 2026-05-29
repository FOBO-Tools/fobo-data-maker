using System.Text.Json;

namespace FOBO.Auth;


/// <summary>
/// Loads / saves a <see cref="TokenSet"/> to a plain JSON file on disk.
/// Location is configured on <see cref="OAuthOptions.TokenStorePath"/>.
/// On Unix the file is <c>chmod 0600</c> after write; on Windows the default
/// user-scoped ACL on <c>LocalApplicationData</c> already restricts access.
///
/// <para>
/// M0 deliberately stores plain JSON — the machine is trusted, and users
/// who care about at-rest protection run full-disk encryption. Later
/// hardening pass: migrate to <c>Microsoft.AspNetCore.DataProtection</c>
/// (DPAPI on Windows, file-keys on Unix) or the platform keychain.
/// </para>
/// </summary>
public sealed class TokenStore(string path)
{
    public TokenSet? Load()
    {
        if (!File.Exists(path)) return null;
        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize(json, AuthJsonContext.Default.TokenSet);
        }
        catch
        {
            return null; // corrupt — caller will treat as "no session", re-login
        }
    }

    public void Save(TokenSet tokens)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, JsonSerializer.Serialize(tokens, AuthJsonContext.Default.TokenSet));

        if (!OperatingSystem.IsWindows())
        {
            try
            {
                File.SetUnixFileMode(path,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* best-effort */ }
        }
    }

    public void Clear()
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch { /* best-effort */ }
    }
}
