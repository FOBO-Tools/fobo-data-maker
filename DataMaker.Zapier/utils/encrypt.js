// Sealed-box crypto for Data Maker submissions. This is now a thin re-export
// of @fobo-tools/datamaker so the desktop, the published SDK, and this Zapier
// app all share one libsodium crypto_box_seal implementation — no second copy
// of the wire contract to keep in sync.

const { initSodium, sealPayload, sha256Hex, sealBlob } = require('@fobo-tools/datamaker');

module.exports = { sealPayload, sha256Hex, initSodium, sealBlob };
