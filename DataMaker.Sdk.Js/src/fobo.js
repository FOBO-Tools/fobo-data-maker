'use strict';

// FOBO attestation verification.
//
// A publisher's .dmf may carry a FOBO attestation — an Ed25519-signed binding
// of their signing key to a FOBO-verified email (and, when present, an
// admin-verified company). Verifying it lets a sender show "Verified by FOBO" /
// who a submission's recipient really is. Mirrors the desktop's
// DataMaker.Forms.Signing.FoboTrustRoot; the root pubkey is duplicated here
// intentionally (sender-side, offline-verifiable).

// FOBO root Ed25519 pubkey (base64). Fingerprint 50:be:66:c4:fb:a6:b0:12.
const FOBO_ROOT_PUBKEY_B64 = 'K2XDkrQ3vn5FSzbohodSMGiSrfomXg9/bgfczFxEGh4=';

const MIN_VERSION = 1;
const MAX_VERSION = 2;

// Verify a manifest's signer.foboAttestation against the FOBO root. Returns
// { isVerified, email, company, sub, expiresAt } on success, or null (bad
// signature, unknown version, pubkey mismatch, expired, or malformed). Failure
// is not fatal — the form is still self-signed-usable. `sodium` is an
// initialised libsodium-wrappers instance (parseDmf already has one).
function verifyFoboAttestation(sodium, attestation, signerPublicKeyB64, now) {
  if (!attestation || typeof attestation !== 'object') return null;
  const payloadJson = attestation.payloadJson;
  const sigB64 = attestation.signatureBase64;
  if (typeof payloadJson !== 'string' || typeof sigB64 !== 'string') return null;

  let rootPub;
  let sig;
  try {
    rootPub = sodium.from_base64(FOBO_ROOT_PUBKEY_B64, sodium.base64_variants.ORIGINAL);
    sig = sodium.from_base64(sigB64, sodium.base64_variants.ORIGINAL);
  } catch (e) {
    return null;
  }
  if (sig.length !== sodium.crypto_sign_BYTES) return null;

  const msg = new TextEncoder().encode(payloadJson);
  let ok = false;
  try {
    ok = sodium.crypto_sign_verify_detached(sig, msg, rootPub);
  } catch (e) {
    return null;
  }
  if (!ok) return null;

  let p;
  try {
    p = JSON.parse(payloadJson);
  } catch (e) {
    return null;
  }
  if (!p || typeof p !== 'object') return null;

  const ver = p.attestationVersion;
  if (!Number.isInteger(ver) || ver < MIN_VERSION || ver > MAX_VERSION) return null;

  // FOBO must vouch for the same key that signed the form.
  if (p.subjectPublicKey !== signerPublicKeyB64) return null;

  if (typeof p.expiresAt === 'string') {
    const exp = Date.parse(p.expiresAt);
    if (!Number.isNaN(exp) && (now == null ? Date.now() : now) >= exp) return null;
  }

  return {
    isVerified: true,
    email: p.subjectEmail ?? null,
    company: p.subjectCompany ?? null,
    sub: p.subjectSub ?? null,
    expiresAt: p.expiresAt ?? null,
  };
}

module.exports = { verifyFoboAttestation, FOBO_ROOT_PUBKEY_B64 };
