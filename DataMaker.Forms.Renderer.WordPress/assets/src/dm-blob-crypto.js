// Browser sealed-box for E2E-encrypting image/attachment blob bytes before
// the direct-to-S3 PUT (#45). Bundled (tweetnacl + tweetnacl-sealedbox-js)
// into assets/vendor/dm-blob-crypto.min.js via `make vendor-crypto`, then
// enqueued ahead of wp-bridge.js. Wire-compatible with libsodium
// crypto_box_seal — a blob sealed here opens with the desktop's
// DataMaker.Sync.Shared.SubmissionBlobCrypto.Unwrap.
//
// We use tweetnacl rather than libsodium-wrappers here on purpose: ~10 KB of
// pure JS vs ~150 KB+ of wasm, keeping the embed bundle lean (#9/#69).

import sealedbox from 'tweetnacl-sealedbox-js';

// 8-byte ASCII magic, mirrors SubmissionBlobCrypto.Magic ("DMSBLOB1").
const MAGIC = Uint8Array.from([0x44, 0x4d, 0x53, 0x42, 0x4c, 0x4f, 0x42, 0x31]);

function base64ToBytes(b64) {
  const bin = atob(b64);
  const out = new Uint8Array(bin.length);
  for (let i = 0; i < bin.length; i++) out[i] = bin.charCodeAt(i);
  return out;
}

const api = {
  /**
   * Seal raw bytes against the recipient's base64 X25519 public key.
   * Returns MAGIC + crypto_box_seal(plaintext) as a Uint8Array.
   */
  seal(plaintextBytes, recipientPubkeyBase64) {
    const pk = base64ToBytes(recipientPubkeyBase64);
    if (pk.length !== 32) {
      throw new Error('recipient pubkey must be 32 bytes, got ' + pk.length);
    }
    const plaintext = plaintextBytes instanceof Uint8Array
      ? plaintextBytes
      : new Uint8Array(plaintextBytes);
    const sealed = sealedbox.seal(plaintext, pk);
    const out = new Uint8Array(MAGIC.length + sealed.length);
    out.set(MAGIC, 0);
    out.set(sealed, MAGIC.length);
    return out;
  },
};

// Expose as a global the bridge reads. (esbuild --format=iife wraps this;
// the assignment is what wp-bridge.js looks for.)
if (typeof window !== 'undefined') window.DataMakerBlobCrypto = api;
export default api;
