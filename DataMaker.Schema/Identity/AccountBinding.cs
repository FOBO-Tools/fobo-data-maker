namespace DataMaker.Schema.Identity;

/// <summary>
/// The FOBO account this install belongs to — the STABLE identity binding that
/// drives account-switch detection, independent of the server-issued
/// attestation (which the per-poll publisher-key upsert can refresh/overwrite).
///
/// <para>Set ONCE when the install is first claimed (first sign-in / onboarding)
/// and only ever changed by the account-switch wizard's committed flow — never by
/// the background re-attest. That's what stops a stray sign-in or a half-finished
/// switch from silently rebinding the install to the wrong account.</para>
/// </summary>
public sealed record AccountBinding
{
    /// <summary>FOBO user id (Cognito sub) the install is bound to. Null = unbound.</summary>
    public string? UserSub { get; init; }

    /// <summary>Email of the bound account — the PRIMARY match key (a federated
    /// user can have two subs for one email, so compare email-first).</summary>
    public string? Email { get; init; }
}
