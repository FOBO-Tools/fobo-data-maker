"""Sealed-box crypto for Data Maker submissions.

Mirrors the desktop's DataMaker.Sync.Shared.SealedBox (libsodium
crypto_box_seal — X25519 + XSalsa20-Poly1305) so the form owner decrypts with
their X25519 private half. PyNaCl wraps the same libsodium primitive.

base64 here is standard-with-padding, matching libsodium's ``base64_variants.ORIGINAL``.
"""

from __future__ import annotations

import base64
import hashlib

from nacl.public import PublicKey, SealedBox
from nacl.signing import VerifyKey
from nacl.exceptions import BadSignatureError

CRYPTO_BOX_PUBLICKEYBYTES = 32
CRYPTO_SIGN_PUBLICKEYBYTES = 32
CRYPTO_SIGN_BYTES = 64


def b64decode(s: str) -> bytes:
    return base64.b64decode(s)


def b64encode(b: bytes) -> str:
    return base64.b64encode(b).decode("ascii")


def seal(plaintext: bytes, recipient_pubkey_b64: str) -> str:
    """Sealed-box encrypt ``plaintext`` against the recipient X25519 public key.
    Returns std-base64 ciphertext for SubmissionEnvelope.ciphertext."""
    pk = b64decode(recipient_pubkey_b64)
    if len(pk) != CRYPTO_BOX_PUBLICKEYBYTES:
        raise ValueError(
            f"recipient pubkey must be {CRYPTO_BOX_PUBLICKEYBYTES} bytes, got {len(pk)}"
        )
    ciphertext = SealedBox(PublicKey(pk)).encrypt(plaintext)
    return b64encode(ciphertext)


def sha256_hex(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def verify_ed25519(message: bytes, signature: bytes, signer_pubkey_b64: str) -> bool:
    """Verify a detached Ed25519 signature over ``message``."""
    pub = b64decode(signer_pubkey_b64)
    if len(pub) != CRYPTO_SIGN_PUBLICKEYBYTES:
        raise ValueError(
            f"signer.publicKey must be {CRYPTO_SIGN_PUBLICKEYBYTES} bytes, got {len(pub)}"
        )
    if len(signature) != CRYPTO_SIGN_BYTES:
        raise ValueError(f"signature.bin must be {CRYPTO_SIGN_BYTES} bytes, got {len(signature)}")
    try:
        VerifyKey(pub).verify(message, signature)
        return True
    except BadSignatureError:
        return False
