using DataMaker.Forms.Signing;

namespace DataMaker.Forms.Renderer.Terminal;

/// <summary>
/// Plain-console first-use trust prompt. Intentionally runs before
/// <c>Application.Init()</c> — we want the user's fingerprint confirmation to
/// read like SSH's "authenticity of host..." prompt, not a modal inside the
/// form UI. If they reject, we never start the TUI.
///
/// <para>
/// When a <see cref="FoboAttestation"/> chains the signer's pubkey to a
/// FOBO-verified email, the prompt promotes that email to the load-bearing
/// label and shows a "Verified by FOBO" badge. Without an attestation we
/// fall back to "self-signed" semantics — name/company/email are still
/// shown if claimed, but always tagged as such.
/// </para>
/// </summary>
internal static class TrustPrompt
{
    public static bool Confirm(VerifiedForm verified)
    {
        Console.WriteLine();
        Console.WriteLine("This form is signed by a publisher you have not trusted before.");
        Console.WriteLine();

        Console.WriteLine($"    Form:        {verified.Form.Name}  ({verified.Form.Id})");
        Console.WriteLine($"    Signed at:   {verified.SignedAt:yyyy-MM-dd HH:mm} UTC");

        if (verified.IsFoboVerified)
        {
            // FoboVerification non-null is guaranteed by IsFoboVerified.
            var att = verified.FoboVerification!;

            // Lead with the guarantee a filler actually cares about: where the
            // submission goes, not who signed. The strong "only they can read
            // it" line is earned ONLY when this form is a sealed-box recipient
            // (AcceptsSubmissions). A share-only / no-recipient form drops it.
            Console.WriteLine();
            if (verified.AcceptsSubmissions)
                Console.WriteLine("    Encrypted delivery — verified by FOBO ✓");
            else
                Console.WriteLine("    Verified by FOBO ✓");

            Console.WriteLine(verified.AcceptsSubmissions
                ? "    Your submission can be read only by:"
                : "    Published by:");

            WriteIdentityRows(verified);

            if (verified.AcceptsSubmissions)
                Console.WriteLine("    Only they hold the key — not even FOBO can read it.");

            Console.WriteLine();
            Console.WriteLine($"    Key fingerprint: {verified.SignerFingerprint}");
            Console.WriteLine($"    Attestation exp: {att.ExpiresAt:yyyy-MM-dd}");
        }
        else if (!verified.SignerIdentity.IsEmpty)
        {
            // Self-signed but with claimed identity — show it but make
            // clear it isn't verified. Fingerprint is load-bearing here.
            var name    = verified.SignerIdentity.Name    ?? "—";
            var company = verified.SignerIdentity.Company ?? "—";
            var email   = verified.SignerIdentity.Email   ?? "—";
            Console.WriteLine($"    ⚠ Self-signed — NOT verified by FOBO.");
            Console.WriteLine($"    Claimed identity (unverified):");
            Console.WriteLine($"      Name:    {name}");
            Console.WriteLine($"      Company: {company}");
            Console.WriteLine($"      Email:   {email}");
            Console.WriteLine($"    Fingerprint: {verified.SignerFingerprint}");
        }
        else
        {
            Console.WriteLine($"    ⚠ Self-signed: no identity claimed.");
            Console.WriteLine($"    Fingerprint: {verified.SignerFingerprint}");
        }

        Console.WriteLine();
        if (verified.IsFoboVerified)
        {
            // FOBO already vouched for the pubkey↔account chain, so Enter = trust.
            Console.WriteLine("Accepting trusts this publisher for every form they publish in future.");
            Console.WriteLine();
            Console.Write("Trust this publisher? [Y/n]: ");
        }
        else
        {
            // Self-signed: make the user do the out-of-band work explicitly.
            Console.WriteLine("Verify this fingerprint with the form's publisher through an");
            Console.WriteLine("out-of-band channel before continuing. Accepting trusts this");
            Console.WriteLine("signer for every form they publish in the future.");
            Console.WriteLine();
            Console.Write("Trust this signer? [y/N]: ");
        }

        var line = Console.ReadLine()?.Trim();
        // Enter (empty) takes the default: trust when FOBO-verified, refuse otherwise.
        if (string.IsNullOrEmpty(line)) return verified.IsFoboVerified;
        return line.Equals("y", StringComparison.OrdinalIgnoreCase) ||
               line.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }

    // Email + company rows for the FOBO-verified card. Email shows a ✓ only when
    // FOBO actually bound it (SubjectEmail); a fallback to the publisher's own
    // ID-token email is shown plain (IdP-verified, but not FOBO-attested).
    // Company shows ✓ only when admin-verified (SubjectCompany), else "(claimed)".
    private static void WriteIdentityRows(VerifiedForm verified)
    {
        var att = verified.FoboVerification!;

        var email = att.SubjectEmail ?? verified.SignerIdentity.Email;
        if (!string.IsNullOrWhiteSpace(email))
        {
            var tag = string.IsNullOrWhiteSpace(att.SubjectEmail) ? "(account email)" : "✓ verified";
            Console.WriteLine($"      {email,-34} {tag}");
        }

        if (!string.IsNullOrWhiteSpace(att.SubjectCompany))
            Console.WriteLine($"      {att.SubjectCompany,-34} ✓ verified");
        else if (!string.IsNullOrWhiteSpace(verified.SignerIdentity.Company))
            Console.WriteLine($"      {verified.SignerIdentity.Company,-34} (claimed)");
    }
}
