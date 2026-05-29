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
        Console.WriteLine($"    Fingerprint: {verified.SignerFingerprint}");

        if (verified.IsFoboVerified)
        {
            // FoboVerification non-null is guaranteed by IsFoboVerified.
            var att = verified.FoboVerification!;
            Console.WriteLine($"    Verified:    by FOBO ✓ as {att.SubjectEmail}");
            if (!string.IsNullOrWhiteSpace(verified.SignerIdentity.Name) ||
                !string.IsNullOrWhiteSpace(verified.SignerIdentity.Company))
            {
                var name    = verified.SignerIdentity.Name    ?? "(no name)";
                var company = verified.SignerIdentity.Company ?? "(no company)";
                Console.WriteLine($"    Display:     {name} / {company}  (claimed)");
            }
            Console.WriteLine($"    Attest. exp: {att.ExpiresAt:yyyy-MM-dd}");
        }
        else if (!verified.SignerIdentity.IsEmpty)
        {
            // Self-signed but with claimed identity — show it but make
            // clear it isn't verified.
            var name    = verified.SignerIdentity.Name    ?? "—";
            var company = verified.SignerIdentity.Company ?? "—";
            var email   = verified.SignerIdentity.Email   ?? "—";
            Console.WriteLine($"    Self-signed identity (CLAIMED, not verified):");
            Console.WriteLine($"      Name:    {name}");
            Console.WriteLine($"      Company: {company}");
            Console.WriteLine($"      Email:   {email}");
        }
        else
        {
            Console.WriteLine($"    Self-signed: no identity claimed.");
        }

        Console.WriteLine();
        Console.WriteLine("Verify this fingerprint with the form's publisher through an");
        Console.WriteLine("out-of-band channel before continuing. Accepting trusts this");
        Console.WriteLine("signer for every form they publish in the future.");
        Console.WriteLine();
        Console.Write("Trust this signer? [y/N]: ");

        var line = Console.ReadLine()?.Trim();
        return line is not null &&
               (line.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("yes", StringComparison.OrdinalIgnoreCase));
    }
}
