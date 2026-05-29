using System.Text.Json;

namespace DataMaker.Forms.Renderer.Terminal;

internal enum TrustDecision
{
    /// <summary>Signer is already in the trust store. Render without prompting.</summary>
    Trusted,

    /// <summary>First encounter. Caller must run the TOFU prompt.</summary>
    PromptUser,
}

/// <summary>
/// Stores fingerprints of signers the user has approved. File lives at
/// <c>~/.datamaker-terminal/trusted-publishers.json</c>. Format is a flat dict
/// keyed by fingerprint so lookups are O(1) and the file is human-auditable.
///
/// <para>
/// Trust is rooted in the signer key, not the form id. Once a user approves
/// fingerprint <c>aa:bb:…</c>, any form signed by that key renders without a
/// prompt. This matches SSH's known_hosts model — a user decision per signer,
/// not per resource.
/// </para>
/// </summary>
internal sealed class TrustStore
{
    private readonly string _storePath;
    private Dictionary<string, TrustRecord> _trusted;

    public TrustStore(string? overridePath = null)
    {
        _storePath = overridePath ?? DefaultPath();
        _trusted   = Load();
    }

    /// <summary>
    /// Decide whether to render immediately or prompt. <paramref name="overrideFingerprint"/>
    /// is the <c>--trust</c> CLI flag — if it matches the signer's fingerprint,
    /// the form is trusted just for this invocation (no store write).
    /// </summary>
    public TrustDecision Evaluate(string signerFingerprint, string? overrideFingerprint)
    {
        if (!string.IsNullOrWhiteSpace(overrideFingerprint) &&
            string.Equals(overrideFingerprint, signerFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return TrustDecision.Trusted;
        }

        return _trusted.ContainsKey(signerFingerprint)
            ? TrustDecision.Trusted
            : TrustDecision.PromptUser;
    }

    /// <summary>Persist the user's approval. The first form id is recorded for context only.</summary>
    public void Trust(string signerFingerprint, string firstFormId)
    {
        _trusted[signerFingerprint] = new TrustRecord(
            Fingerprint: signerFingerprint,
            FirstFormId: firstFormId,
            TrustedAt:   DateTimeOffset.UtcNow);
        Save();
    }

    private Dictionary<string, TrustRecord> Load()
    {
        if (!File.Exists(_storePath))
            return new Dictionary<string, TrustRecord>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize(json, TerminalJsonContext.Default.DictionaryStringTrustRecord)
                   ?? new Dictionary<string, TrustRecord>(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            // Corrupt store — start fresh rather than refusing to launch.
            return new Dictionary<string, TrustRecord>(StringComparer.OrdinalIgnoreCase);
        }
    }

    private void Save()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        File.WriteAllText(_storePath, JsonSerializer.Serialize(_trusted, TerminalJsonContext.Default.DictionaryStringTrustRecord));
    }

    private static string DefaultPath()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".datamaker-terminal", "trusted-publishers.json");
    }
}

internal sealed record TrustRecord(string Fingerprint, string FirstFormId, DateTimeOffset TrustedAt);
