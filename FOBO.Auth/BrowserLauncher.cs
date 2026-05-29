using System.Diagnostics;

namespace FOBO.Auth;

/// <summary>
/// Cross-platform "open this URL in the user's default browser" helper.
/// Used to kick off the OAuth authorize step — every OS has a different
/// built-in for this (<c>open</c>, <c>xdg-open</c>, <c>start</c> via shell).
/// </summary>
public static class BrowserLauncher
{
    public static void Open(string url)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        try
        {
            if (OperatingSystem.IsWindows())
            {
                // Go through `cmd /c start` so URLs with &, ?, = survive shelling.
                var psi = new ProcessStartInfo("cmd", $"/c start \"\" \"{url}\"")
                {
                    CreateNoWindow = true,
                    UseShellExecute = false,
                };
                Process.Start(psi);
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("open", url);
            }
            else // Linux, FreeBSD, etc.
            {
                Process.Start("xdg-open", url);
            }
        }
        catch
        {
            // Browser launch failed (no default browser, sandboxed env, etc.).
            // Caller should still have printed the URL so the user can paste it.
        }
    }
}
