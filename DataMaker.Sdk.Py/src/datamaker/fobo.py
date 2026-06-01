"""FOBO attestation verification.

A publisher's `.dmf` may carry a FOBO attestation — an Ed25519-signed binding
of their signing key to a FOBO-verified email (and, when present, an
admin-verified company). Verifying it lets a sender show "Verified by FOBO" /
who a submission's recipient really is. Mirrors the desktop's
DataMaker.Forms.Signing.FoboTrustRoot; the root pubkey is duplicated here
intentionally (sender-side, offline-verifiable).
"""

from __future__ import annotations

import base64
import json
from datetime import datetime, timezone
from typing import Optional

from .crypto import verify_ed25519

# FOBO root Ed25519 pubkey (base64). Fingerprint 50:be:66:c4:fb:a6:b0:12.
FOBO_ROOT_PUBKEY_B64 = "K2XDkrQ3vn5FSzbohodSMGiSrfomXg9/bgfczFxEGh4="

_MIN_VERSION = 1
_MAX_VERSION = 2


def verify_fobo_attestation(
    attestation: Optional[dict],
    signer_pubkey_b64: str,
    now: Optional[datetime] = None,
) -> Optional[dict]:
    """Verify a manifest's `signer.foboAttestation` against the FOBO root.

    Returns a dict ``{is_verified, email, company, sub, expires_at}`` on success,
    or ``None`` (bad signature, unknown version, pubkey mismatch, expired, or
    malformed). Failure is not fatal — the form is still self-signed-usable.
    """
    if not isinstance(attestation, dict):
        return None
    payload_json = attestation.get("payloadJson")
    sig_b64 = attestation.get("signatureBase64")
    if not isinstance(payload_json, str) or not isinstance(sig_b64, str):
        return None

    try:
        sig = base64.b64decode(sig_b64)
    except Exception:
        return None

    try:
        if not verify_ed25519(payload_json.encode("utf-8"), sig, FOBO_ROOT_PUBKEY_B64):
            return None
    except Exception:
        return None

    try:
        p = json.loads(payload_json)
    except ValueError:
        return None
    if not isinstance(p, dict):
        return None

    ver = p.get("attestationVersion")
    if not isinstance(ver, int) or ver < _MIN_VERSION or ver > _MAX_VERSION:
        return None

    # FOBO must vouch for the same key that signed the form.
    if p.get("subjectPublicKey") != signer_pubkey_b64:
        return None

    expires_at = p.get("expiresAt")
    exp_dt = _parse_iso(expires_at)
    if exp_dt is not None:
        now_dt = now or datetime.now(timezone.utc)
        if now_dt >= exp_dt:
            return None

    return {
        "is_verified": True,
        "email": p.get("subjectEmail"),
        "company": p.get("subjectCompany"),
        "sub": p.get("subjectSub"),
        "expires_at": expires_at,
    }


def _parse_iso(value) -> Optional[datetime]:
    """Best-effort ISO-8601 parse. Unparseable expiry -> no expiry check
    (matches the .NET SDK: a missing/garbled expiry doesn't fail verification)."""
    if not isinstance(value, str) or not value:
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None
