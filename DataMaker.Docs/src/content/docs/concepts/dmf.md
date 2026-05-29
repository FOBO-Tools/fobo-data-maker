---
title: The .dmf bundle
description: On-disk format of a signed DataMaker form bundle — ZIP layout, manifest, signature, and the render bundle.
---

A `.dmf` (DataMakerForm) is a **ZIP archive** containing a signed form
definition plus everything a renderer needs. The signature covers a manifest,
and the manifest covers every other entry by SHA-256 — so any tampering is
detectable.

## Archive entries

| Entry | Required | Contents |
|---|---|---|
| `manifest.json` | yes | The signed payload: signer + recipient keys, and a SHA-256 of every other entry. |
| `signature.bin` | yes | 64 raw bytes — a detached **Ed25519** signature over the exact bytes of `manifest.json`. |
| `form.json` | yes | UTF-8 JSON of the [Form](/fobo-data-maker/schema/form-model/). |
| `compiled.json` | v3+ | Expression-key → compiled JS string (VisibleWhen / Calculated / rules). |
| `elementCss.json` | v3+ | Element-key → resolved CSS string. |
| `palette.css` | v3+ | Theme palette CSS (`:root` light/dark variables). |
| `fonts.css` | v3+ | `@font-face` rules for embedded fonts. |

The signing scheme signs the **manifest**, not the ZIP's central directory: ZIP
layout isn't byte-stable across producers, but the manifest is a single
canonical artifact. Every other file is bound by its SHA-256 in the manifest.

- MIME type: `application/vnd.datamaker.form`
- Current envelope version: **3**

## manifest.json

```json
{
  "envelopeVersion": 3,
  "signedAt": "2026-05-29T12:00:00Z",
  "signer": {
    "publicKey": "<base64 Ed25519 public key, 32 bytes>",
    "identity": { "email": "pub@example.com", "name": "Acme", "company": "Acme Inc" },
    "foboAttestation": { /* optional FOBO trust voucher */ }
  },
  "recipient": {
    "publicKey": "<base64 X25519 public key, 32 bytes>",
    "userId": "<form owner id>"
  },
  "files": [
    { "path": "form.json", "sha256": "<lowercase hex>", "size": 1234 }
  ]
}
```

- `signer.publicKey` — the Ed25519 key the signature verifies against.
- `recipient` — present only when the form **accepts submissions**. Its
  `publicKey` is the X25519 key you encrypt submissions against; `userId`
  identifies the form owner. A `null`/absent `recipient` means a share-only
  bundle (no submissions).
- `files[]` — every covered entry with its SHA-256 and byte size.

All keys are standard base64 (with padding), matching libsodium's `ORIGINAL`
variant.

## Verifying a bundle

1. Unzip; read `manifest.json`, `signature.bin`, `form.json`.
2. **Verify the signature**: Ed25519 `verify_detached(signature.bin,
   manifest.json bytes, signer.publicKey)`.
3. **Check hashes**: SHA-256 of `form.json` (and any entry you load) must equal
   its `files[]` entry.
4. Only then trust `recipient.publicKey` and the form definition.

:::caution
Always verify before trusting the recipient public key — it's what submissions
are encrypted to. A tampered key would let an attacker read submissions.
:::

Every SDK does this for you (`readForm` / `read_form` / `ReadForm`). To do it
yourself, see [Build your own](/fobo-data-maker/build-your-own/).

## FOBO attestation (optional trust)

The manifest may carry a `foboAttestation` — an Ed25519 signature from the FOBO
root key vouching that the signer's public key belongs to a verified identity
(e.g. an email). Clients verify it against the published FOBO root public key
and, when valid, treat the signer's claimed email as authoritative. It's an
extra trust signal layered on the publisher's own signature.
