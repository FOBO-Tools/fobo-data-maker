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

module.exports = { initSodium, sealPayload, sha256Hex };
