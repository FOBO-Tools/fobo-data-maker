'use strict';

// Reader/verifier for the .dmf signed-bundle format
// (DataMaker.Forms.Signing.DmfFormat). A .dmf is a ZIP archive of:
//   manifest.json  — UTF-8 JSON, the exact bytes the publisher Ed25519-signs.
//                    Lists signer/recipient keys + every other entry's SHA-256.
//   signature.bin  — 64 raw bytes, detached Ed25519 signature over manifest.json
//   form.json      — UTF-8 JSON of the Form (fields, layout, style, version)
//   compiled.json / palette.css / fonts.css / … — bound by hash in manifest.files[]
//
// We verify (a) the Ed25519 signature over manifest.json with signer.publicKey,
// and (b) that form.json's SHA-256 matches its manifest entry — proving the
// recipient public key and field schema we hand back are exactly what the
// publisher signed and haven't been tampered with in transit. Other entries
// aren't loaded, so their hashes aren't re-checked.

const JSZip = require('jszip');
const { initSodium, sha256Hex } = require('./encrypt');
const { DmfError } = require('./errors');
const { verifyFoboAttestation } = require('./fobo');

const MANIFEST_ENTRY = 'manifest.json';
const SIGNATURE_ENTRY = 'signature.bin';
const FORM_ENTRY = 'form.json';

// Parse + verify a .dmf bundle into a form descriptor the SDK can submit
// against. `opts.verify` (default true) gates the Ed25519 + hash checks; only
// disable it for trusted local fixtures.
async function parseDmf(bytes, opts = {}) {
  const verify = opts.verify !== false;
  const sodium = await initSodium();

  let zip;
  try {
    zip = await JSZip.loadAsync(bytes);
  } catch (e) {
    throw new DmfError(`not a readable .dmf archive: ${e.message}`);
  }

  const manifestEntry = zip.file(MANIFEST_ENTRY);
  const signatureEntry = zip.file(SIGNATURE_ENTRY);
  const formEntry = zip.file(FORM_ENTRY);
  if (!manifestEntry) throw new DmfError('.dmf is missing manifest.json');
  if (!formEntry) throw new DmfError('.dmf is missing form.json');
  if (verify && !signatureEntry) throw new DmfError('.dmf is missing signature.bin');

  const manifestBytes = await manifestEntry.async('uint8array');
  const formBytes = await formEntry.async('uint8array');

  const manifest = parseJson(manifestBytes, 'manifest.json');

  if (verify) {
    const signatureBytes = await signatureEntry.async('uint8array');
    verifySignature(sodium, manifest, manifestBytes, signatureBytes);
    await verifyFormHash(manifest, formBytes);
  }

  const form = parseJson(formBytes, 'form.json');
  const recipient = manifest.recipient || null;

  // FOBO attestation (optional) — its own chain, verified against the root
  // pubkey regardless of `verify`. null when absent or it fails to verify.
  const fobo =
    manifest.signer && manifest.signer.publicKey && manifest.signer.foboAttestation
      ? verifyFoboAttestation(sodium, manifest.signer.foboAttestation, manifest.signer.publicKey)
      : null;

  return {
    formId: form.id,
    name: form.name,
    description: form.description ?? null,
    schemaVersion: form.schemaVersion ?? 1,
    submitPolicy: form.submitPolicy ?? null,
    recipientUserId: recipient ? recipient.userId : null,
    recipientPublicKey: recipient ? recipient.publicKey : null,
    signer: manifest.signer
      ? { publicKey: manifest.signer.publicKey, identity: manifest.signer.identity ?? null }
      : null,
    envelopeVersion: manifest.envelopeVersion ?? 0,
    fields: extractFields(form),
    verified: verify,
    foboVerification: fobo,
    isFoboVerified: !!fobo,
  };
}

function parseJson(bytes, label) {
  try {
    return JSON.parse(new TextDecoder('utf-8').decode(bytes));
  } catch (e) {
    throw new DmfError(`.dmf ${label} is not valid JSON: ${e.message}`);
  }
}

function verifySignature(sodium, manifest, manifestBytes, signatureBytes) {
  if (!manifest.signer || !manifest.signer.publicKey) {
    throw new DmfError('.dmf manifest.signer.publicKey is missing — cannot verify');
  }
  const signerPub = sodium.from_base64(manifest.signer.publicKey, sodium.base64_variants.ORIGINAL);
  if (signerPub.length !== sodium.crypto_sign_PUBLICKEYBYTES) {
    throw new DmfError(
      `signer.publicKey must be ${sodium.crypto_sign_PUBLICKEYBYTES} bytes, got ${signerPub.length}`
    );
  }
  if (signatureBytes.length !== sodium.crypto_sign_BYTES) {
    throw new DmfError(
      `signature.bin must be ${sodium.crypto_sign_BYTES} bytes, got ${signatureBytes.length}`
    );
  }
  if (!sodium.crypto_sign_verify_detached(signatureBytes, manifestBytes, signerPub)) {
    throw new DmfError('.dmf signature is invalid — bundle was tampered with or signer key is wrong');
  }
}

async function verifyFormHash(manifest, formBytes) {
  const hashHex = await sha256Hex(formBytes);
  const entry = (manifest.files || []).find((f) => f.path === FORM_ENTRY);
  if (!entry) throw new DmfError('.dmf manifest does not list form.json — bundle is malformed');
  if (entry.sha256 !== hashHex) {
    throw new DmfError('.dmf form.json hash mismatch — bundle was tampered with');
  }
}

// Flatten form.fields[] into the descriptor the validator + CLI consume.
// `key` is the storage name (what the receiver indexes on); `label` is the
// human-facing caption.
function extractFields(form) {
  const out = [];
  for (const f of form.fields || []) {
    const entry = {
      id: f.id,
      key: f.name,
      name: f.name,
      label: f.label || f.name,
      kind: f.kind,
      required: !!f.required,
    };
    if (f.description) entry.description = f.description;
    if (f.placeholder) entry.placeholder = f.placeholder;
    const choices = f.choice && f.choice.choices;
    if (Array.isArray(choices) && choices.length) {
      entry.choices = choices.map((c) => ({ value: c.value, label: c.label || c.value }));
      entry.allowCustom = !!f.choice.allowCustom;
    }
    out.push(entry);
  }
  return out;
}

module.exports = { parseDmf, extractFields };
