using System.Text.Json.Serialization;

namespace DataMaker.Schema.Forms;

/// <summary>
/// Who is allowed to submit this form. Set by the publisher at form-design
/// time and covered by the publisher's Ed25519 signature on the
/// <c>SignedForm</c> envelope — so it can't be tampered with in transit.
///
/// <para>
/// <b>Enforcement</b> lives in two places:
/// <list type="bullet">
///   <item><b>Submitter</b> (Terminal, future WASM host) — reads the signed
///         policy and, if <see cref="Authenticated"/>, runs the FOBO OAuth
///         flow before submitting so a Cognito access token can ride on
///         <c>Authorization</c>. If the user declines, the submission never
///         happens.</item>
///   <item><b>Publisher's local DataMaker</b> — on decrypt, cross-checks the
///         signed policy against the stamped <c>SubmitterId</c> in the
///         received envelope. Anonymous submits to an authenticated-only
///         form get rejected at ingest. Tamper-proof because the policy is
///         under the publisher's signature.</item>
/// </list>
/// The Lambda stays a dumb relay — it only stamps <c>SubmitterId</c> from
/// whatever Bearer token is present (or leaves it null), never inspects the
/// policy field directly.
/// </para>
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SubmitPolicy>))]
public enum SubmitPolicy
{
    /// <summary>No auth required. Matches the original M0 survey semantics.</summary>
    Anonymous = 0,

    /// <summary>Submitter must sign in with a FOBO Cognito account. Submission carries their sub as <c>SubmitterId</c>.</summary>
    Authenticated = 1,
}
