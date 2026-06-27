using System.Diagnostics;
using System.Runtime.InteropServices;

namespace DataMaker.Services;

/// <summary>
/// Resolves a friendly, human-readable name for the current device — what the
/// user sees in Settings → Devices, sync leases, and recovery bundles.
///
/// <para>
/// <b>Why not just <see cref="Environment.MachineName"/>?</b> On macOS that
/// reads the kernel hostname (<c>gethostname()</c>), which the OS overwrites
/// with the name handed out by DHCP. On routers that assign a UUID hostname
/// (e.g. AVM Fritz!Box: <c>d8a4a977-…-….fritz.box</c>), the kernel hostname
/// becomes that UUID and .NET trims it at the first dot — so the device shows
/// up as a raw GUID instead of "MacBook Air van Chivan". It also changes
/// whenever the DHCP lease refreshes (reboot, network switch), so the same
/// machine churns names. The user-facing name the OS actually owns lives in
/// <c>scutil --get ComputerName</c>, which DHCP never touches.
/// </para>
///
/// <para>
/// Resolved once per process and cached — the computer name effectively never
/// changes during a session, and we don't want a process spawn on every sync
/// lease renewal. Synchronous Process read (no sync-over-async) so it's safe
/// to touch from any thread.
/// </para>
/// </summary>
public static class DeviceName
{
    private static readonly Lazy<string> _friendly = new(Resolve);

    /// <summary>Friendly device name, e.g. "MacBook Air van Chivan".</summary>
    public static string Friendly => _friendly.Value;

    private static string Resolve()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var computerName = ScutilGet("ComputerName");
            if (!string.IsNullOrWhiteSpace(computerName)) return computerName;
        }

        // Windows + Linux: MachineName is the user-set hostname, not a
        // DHCP-assigned one, so it's already friendly. Strip any trailing
        // ".local"/domain suffix just in case.
        var fallback = Environment.MachineName;
        var dot = fallback.IndexOf('.');
        return dot > 0 ? fallback[..dot] : fallback;
    }

    private static string? ScutilGet(string key)
    {
        try
        {
            var psi = new ProcessStartInfo("scutil", $"--get {key}")
            {
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using var p = Process.Start(psi);
            if (p is null) return null;
            var stdout = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return p.ExitCode == 0 ? stdout.Trim() : null;
        }
        catch
        {
            // scutil missing / sandboxed — caller falls back to MachineName.
            return null;
        }
    }
}
