---
title: Submission & encryption
description: The end-to-end-encrypted submission wire contract — sealed-box payload, envelope, and the /submissions endpoint.
---

Submitting a record is **end-to-end encrypted**. You seal the values against
the form owner's X25519 public key (from the `.dmf` manifest) and POST the
ciphertext. The server routes it to the owner, who alone can decrypt it.

## The flow

1. Read + verify the `.dmf` → get `recipientPublicKey`, `recipientUserId`,
   `formId`, `schemaVersion`, and the field schema.
2. **Validate + coerce** the values against the schema (see
   [field kinds](/fobo-data-maker/schema/field-kinds/)).
3. Build a **SubmissionPayload**, JSON-encode it, and **sealed-box encrypt**
   the bytes against `recipientPublicKey`.
4. Wrap the ciphertext in a **SubmissionEnvelope** and `POST /submissions`.

Everything before the POST is offline.

## Encryption

Sealed box = libsodium **`crypto_box_seal`** (X25519 + XSalsa20-Poly1305). The
sender generates an ephemeral keypair, so the ciphertext is anonymous and only
the holder of the recipient private key can open it.

- Input: UTF-8 JSON bytes of the SubmissionPayload.
- Key: the 32-byte X25519 `recipient.publicKey` from the manifest (base64).
- Output: ciphertext → standard base64 → the envelope's `ciphertext` field.

## SubmissionPayload (the plaintext that gets sealed)

```json
{
  "values": { "email": "ada@example.com", "age": 37, "tags": ["a", "c"] },
  "submittedAt": "2026-05-29T12:00:00.000Z",
  "formVersion": 1,
  "formSchema": "",
  "mode": "Create"
}
```

| Field | Type | Notes |
|---|---|---|
| `values` | object | Field **name** → value. Shapes per [field kind](/fobo-data-maker/schema/field-kinds/). |
| `submittedAt` | string | ISO-8601 UTC. |
| `formVersion` | int | The form's `schemaVersion` (per-form, starts at 1). The receiver looks up the archived schema by `(formId, formVersion)`; a mismatch quarantines the row. |
| `formSchema` | string | Legacy; leave `""`. |
| `mode` | string | `"Create"` for a new submission. `"Update"` is used when amending an existing one through the edit-token flow. |

## SubmissionEnvelope (the request body)

```json
{
  "submissionId": "00000000000000000000000000000000",
  "formId": "contact_form",
  "recipientPubkey": "<base64 X25519 public key>",
  "submitterId": null,
  "ciphertext": "<base64 sealed box>"
}
```

| Field | Type | Notes |
|---|---|---|
| `submissionId` | string | Random UUIDv4 with dashes stripped (32 hex chars). Idempotency key. |
| `formId` | string | From `form.json`. |
| `recipientPubkey` | string | **Required.** The recipient destination — the X25519 box public key from `manifest.recipient.publicKey` that the ciphertext is sealed to. The server fingerprints it to route the submission to the right database. |
| `submitterId` | string \| null | `null` for anonymous submissions (the common case). |
| `ciphertext` | string | Base64 sealed-box of the SubmissionPayload. |

A user can own several databases, each its own recipient identity. Routing keys on
`recipientPubkey` (not a user id), so each database receives only its own forms'
submissions. Queue ownership is verified against the server-side identity registry.

Only the envelope's plaintext metadata is visible to the server (for routing,
dedup, timestamping). The `ciphertext` is opaque to it.

## The endpoint

```
POST {apiBaseUrl}/submissions
Content-Type: application/json

<SubmissionEnvelope>
```

- Default `apiBaseUrl`: `https://datamaker-api.fobo-tools.com`
- **200** → `{ "submissionId": "...", "editToken": "..." }`
- **400** → missing required envelope fields
- **413** → ciphertext too large

The endpoint is anonymous (no auth) for public forms. The `editToken` lets a
submitter amend their submission later (not covered by the v1 SDKs).

:::tip
This contract is identical across every sender — the SDKs, the web renderer,
and the WordPress plugin all produce the same bytes. To implement it yourself,
see [Build your own](/fobo-data-maker/build-your-own/).
:::
