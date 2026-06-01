'use strict';

// Sealed-box crypto for Data Maker submission payloads. Mirrors the desktop's
// DataMaker.Sync.Shared.SealedBox (libsodium crypto_box_seal — X25519 +
// XSalsa20-Poly1305) so the form owner decrypts with their X25519 private half
// via the same SealedBox.Open path the web/WP/.dmf renderers already use.
//
// libsodium-wrappers-sumo is the "sumo" build — it exposes the full API
// surface (base64 helpers + crypto_box_seal + crypto_sign_verify_detached).
// Init is async; everything here awaits initSodium() first.

const sodium = require('libsodium-wrappers-sumo');

let _ready;
async function initSodium() {
  if (!_ready) _ready = sodium.ready;
  await _ready;
  return sodium;
}

// Sealed-box encrypt a JS object against the recipient's X25519 public key.
// Returns std-base64 ciphertext for SubmissionEnvelope.ciphertext.
async function sealPayload(payload, recipientPubkeyBase64) {
  await initSodium();
  const pubkey = sodium.from_base64(recipientPubkeyBase64, sodium.base64_variants.ORIGINAL);
  if (pubkey.length !== sodium.crypto_box_PUBLICKEYBYTES) {
    throw new Error(
      `recipient pubkey must be ${sodium.crypto_box_PUBLICKEYBYTES} bytes, got ${pubkey.length}`
    );
  }
  const message = sodium.from_string(JSON.stringify(payload));
  const ciphertext = sodium.crypto_box_seal(message, pubkey);
  return sodium.to_base64(ciphertext, sodium.base64_variants.ORIGINAL);
}

// Lowercase-hex SHA-256 of a Buffer/Uint8Array. Used to verify .dmf file
// hashes against the manifest.
async function sha256Hex(bytes) {
  await initSodium();
  return sodium.to_hex(sodium.crypto_hash_sha256(bytes));
}

// 8-byte ASCII magic prepended to every sealed blob. Mirrors
// DataMaker.Sync.Shared.SubmissionBlobCrypto.Magic ("DMSBLOB1"); the
// trailing digit is the format version. The desktop receiver peeks these
// bytes to tell a sealed blob from a legacy plaintext one.
const BLOB_MAGIC = Uint8Array.from([0x44, 0x4d, 0x53, 0x42, 0x4c, 0x4f, 0x42, 0x31]); // "DMSBLOB1"

// Sealed-box encrypt raw blob bytes (image/attachment file) against the
// recipient's X25519 public key, prefixed with BLOB_MAGIC. Mirrors
// SubmissionBlobCrypto.Wrap (C#). Returns a Uint8Array the caller PUTs to S3
// in place of the plaintext bytes. The S3 key still uses SHA-256(plaintext).
async function sealBlob(plaintextBytes, recipientPubkeyBase64) {
  await initSodium();
  const pubkey = sodium.from_base64(recipientPubkeyBase64, sodium.base64_variants.ORIGINAL);
  if (pubkey.length !== sodium.crypto_box_PUBLICKEYBYTES) {
    throw new Error(
      `recipient pubkey must be ${sodium.crypto_box_PUBLICKEYBYTES} bytes, got ${pubkey.length}`
    );
  }
  const plaintext = plaintextBytes instanceof Uint8Array ? plaintextBytes : new Uint8Array(plaintextBytes);
  const sealed = sodium.crypto_box_seal(plaintext, pubkey);
  const out = new Uint8Array(BLOB_MAGIC.length + sealed.length);
  out.set(BLOB_MAGIC, 0);
  out.set(sealed, BLOB_MAGIC.length);
  return out;
}

module.exports = { initSodium, sealPayload, sha256Hex, sealBlob, BLOB_MAGIC };
