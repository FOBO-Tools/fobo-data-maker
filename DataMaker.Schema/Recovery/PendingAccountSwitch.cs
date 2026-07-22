namespace DataMaker.Schema.Recovery;

/// <summary>What an armed account switch should do to the existing database.</summary>
public enum AccountSwitchMode
{
    /// <summary>Re-encrypt the current database under the new account's key and
    /// adopt it (the new account inherits the forms + records).</summary>
    Keep,

    /// <summary>Don't carry the current database over — after the switch, drop
    /// the user into onboarding (already signed in) to set up a new database or
    /// restore their own. The old database is left untouched until onboarding
    /// replaces it, so the prior account can still recover by signing back in.</summary>
    DontKeep,
}

/// <summary>
/// On-disk marker for an account switch that has been COMMITTED but not yet
/// executed — written at the wizard's final "restart" step and consumed once on
/// the next launch (see the switch executor). Everything here is non-secret: the
/// new database key lives in the OS vault under
/// <c>KeyVaultKeys.PendingSwitchKey</c>, never in this file.
///
/// <para>The marker's mere presence is the resume signal — a kill at any point
/// before the executor finishes leaves it in place, so the switch re-runs (or,
/// if the prior user signs back in first, is discarded) rather than half-applying.</para>
/// </summary>
public sealed record PendingAccountSwitch
{
    /// <summary>Keep-and-rekey vs don't-keep-and-onboard.</summary>
    public AccountSwitchMode Mode { get; init; }

    /// <summary>FOBO user id (Cognito sub) of the account being switched TO —
    /// lets the executor confirm the still-signed-in user matches the one that
    /// armed the switch before applying anything.</summary>
    public string? NewUserSub { get; init; }

    /// <summary>Email of the account being switched TO — written into the stable
    /// account binding when the switch executes (email is the primary match key).</summary>
    public string? NewUserEmail { get; init; }

    /// <summary>Opaque new-identity payload (signing/box public+private halves +
    /// FOBO attestation) to install after a successful re-key, serialized by the
    /// host. Carried verbatim; the host knows how to apply it. Null for
    /// <see cref="AccountSwitchMode.DontKeep"/> (identity is blanked instead).</summary>
    public string? NewIdentityJson { get; init; }

    /// <summary>When the switch was committed (ISO-8601 UTC), for diagnostics.</summary>
    public string? CommittedAtUtc { get; init; }
}
