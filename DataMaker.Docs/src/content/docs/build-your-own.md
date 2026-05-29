---
title: Build your own client or renderer
description: Implement a DataMaker submit client or form renderer in any language, from the .dmf format and submission contract.
---

Everything a client needs is documented and standard-crypto based, so you can
build one in any stack. There are two things you might build: a **submit
client** (no UI) or a **renderer** (UI + submit).

## A submit client

Goal: take a `.dmf` + a values map → a sealed submission.

1. **Read + verify the `.dmf`** ([format](/fobo-data-maker/concepts/dmf/)):
   - Unzip; read `manifest.json`, `signature.bin`, `form.json`.
   - Ed25519 `verify_detached(signature.bin, manifest.json bytes,
     signer.publicKey)`.
   - SHA-256 of `form.json` must equal its `files[]` entry.
   - Read `recipient.publicKey`, `recipient.userId`, and from `form.json` the
     `id` + `schemaVersion` + `fields`.
2. **Validate + coerce** values against the [field kinds](/fobo-data-maker/schema/field-kinds/):
   required present, unknown keys rejected, choice membership, per-kind value
   shapes.
3. **Build the [SubmissionPayload](/fobo-data-maker/concepts/submission/)**, JSON-encode (UTF-8).
4. **Sealed-box encrypt** the bytes against `recipient.publicKey`
   (`crypto_box_seal`), base64 the ciphertext.
5. **POST** the [SubmissionEnvelope](/fobo-data-maker/concepts/submission/) as JSON to
   the public submissions endpoint:

   ```http
   POST https://datamaker-api.fobo-tools.com/submissions
   Content-Type: application/json
   ```

   It's anonymous (no auth) for public forms. **200** → `{ submissionId,
   editToken }`; **400** → bad/missing envelope fields; **413** → ciphertext too
   large. See [Submission & encryption](/fobo-data-maker/concepts/submission/)
   for the exact envelope + payload shapes.

If the form has `image` / `attachment` fields, upload the bytes **before**
step 3 and put the resulting ref into `values` — see
[Files & attachments](/fobo-data-maker/concepts/files/). Bytes never go in the
sealed payload.

You need a libsodium binding (sealed box + Ed25519 verify), a ZIP reader,
SHA-256, and JSON. That's it.

```text
crypto you need:
  crypto_box_seal(plaintext, recipientPubKey)   → submission encryption
  Ed25519 verify_detached(sig, msg, signerPub)  → .dmf signature check
  SHA-256                                        → .dmf hash check
```

The official SDKs ([JS](/fobo-data-maker/sdks/javascript/), [Python](/fobo-data-maker/sdks/python/),
[.NET](/fobo-data-maker/sdks/dotnet/)) are reference implementations — read their source if you
get stuck.

## A renderer

A renderer additionally turns `form.json` into UI and collects values.

1. **Read the form** (as above) — you also want the `fields` + `steps` layout
   ([form model](/fobo-data-maker/schema/form-model/)) and, from a `.dmf v3`, the styling
   entries (`elementCss.json`, `palette.css`, `fonts.css`).
2. **Render** the layout: walk `steps → sections → rows → columns`; render each
   `field` column with the editor for its [kind](/fobo-data-maker/schema/field-kinds/); render
   decorative columns (richtext/image/divider/heading/button/group).
3. **Evaluate expressions** for `visibleWhen` / `calculatedExpression` /
   validation — either run the bundle's compiled JS or implement the
   [DSL + Fn library](/fobo-data-maker/schema/expressions/). Fail open when you can't.
4. **Validate on submit**, then hand the values to the submit-client steps
   above.

:::tip
Don't want to write a web renderer from scratch? The JS renderer is ready to
host — see [Web embed](/fobo-data-maker/renderers/web-embed/). It reads the `.dmf v3` bundle,
renders, validates, and submits.
:::

## Trust & safety checklist

- **Always verify the signature and hashes before trusting anything** in the
  bundle — especially `recipient.publicKey`.
- Treat the signer's claimed identity as unverified unless a valid **FOBO
  attestation** is present.
- Never log or persist plaintext values beyond what your client needs; the
  whole point is the server can't read them.
- Match the wire contract exactly (camelCase keys, base64 `ORIGINAL`, UUID
  without dashes, `formVersion = schemaVersion`).
