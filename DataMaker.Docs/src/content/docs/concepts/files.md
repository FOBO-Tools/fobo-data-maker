---
title: Files & attachments
description: How to upload images and attachments for image / attachment fields — the pre-signed upload-slot flow and the value ref shape.
---

The SDKs seal **text** (and the JSON metadata around a file), never the file
bytes. So `image` and `attachment` fields aren't submitted inline — you upload
the bytes to FOBO Cloud first, then put a small **reference** (a URL + hash)
into the field's value.

Every official renderer runs this on submit (web, WordPress, terminal, Zapier).
The SDKs don't — they pass the ref you build straight through. Build the ref
with the three steps below, then submit it as the field's value.

## Separate upload path

Files are large, so they take a separate path from the rest of the values. The
bytes go **straight to storage** over a pre-signed PUT URL — the API never sees
them — and the sealed payload carries only the URL and a content hash. The
endpoint is anonymous, the same trust model as a submission; the
`(recipientUserId, hash)` pair is the capability.

## The three steps

```text
1. hash    → SHA-256 the file bytes (64-char lowercase hex)
2. slot    → POST /submissions/upload-slot { recipientUserId, hash, mime, sizeBytes }
                                              → { url, key, expiresAtUtc }
3. upload  → PUT the bytes to that url  (Content-Type must match the mime you sent)
```

Then set the field value to the ref:

```json
{ "url": "<the slot url>", "hash": "<sha256 hex>", "fileName": "photo.jpg",
  "mime": "image/jpeg", "sizeBytes": 18234, "owned": true }
```

### upload-slot request

`POST https://datamaker-api.fobo-tools.com/submissions/upload-slot`

| Field | Type | Notes |
|---|---|---|
| `recipientUserId` | string | **Required.** Same value as the envelope — `manifest.recipient.userId`. |
| `hash` | string | **Required.** SHA-256 of the bytes, 64-char lowercase hex. Must match the bytes you PUT. |
| `mime` | string \| null | Optional. Binds into the PUT signature — send the same `Content-Type` on the PUT. |
| `sizeBytes` | number \| null | Optional. `1 .. 52428800` (50 MB). Out of range → **400**. |

Response `{ url, key, expiresAtUtc }` — `url` is a short-lived pre-signed PUT.
`400` → bad/missing `recipientUserId` or `hash`, or `sizeBytes` over the 50 MB
cap.

### the PUT

PUT the raw bytes to `url`. Set `Content-Type` to the same `mime` you declared
(the signature binds it; a mismatch is rejected by storage). No auth header —
the signature is in the URL.

## Example (JavaScript)

```js
import { createHash } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import * as dm from '@fobo-tools/datamaker';

const API = 'https://datamaker-api.fobo-tools.com';

// Upload one file, return the ImageRef / AttachmentRef the form field wants.
async function uploadFile(path, mime, recipientUserId) {
  const bytes = await readFile(path);
  const hash  = createHash('sha256').update(bytes).digest('hex'); // step 1

  // step 2 — reserve a slot
  const slotRes = await fetch(`${API}/submissions/upload-slot`, {
    method:  'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      recipientUserId,
      hash,
      mime,
      sizeBytes: bytes.length,
    }),
  });
  if (!slotRes.ok) throw new Error(`upload-slot ${slotRes.status}`);
  const { url } = await slotRes.json();

  // step 3 — PUT bytes straight to storage
  const put = await fetch(url, {
    method:  'PUT',
    headers: { 'content-type': mime },
    body:    bytes,
  });
  if (!put.ok) throw new Error(`upload PUT ${put.status}`);

  return {
    url, hash,
    fileName:  path.split('/').pop(),
    mime,
    sizeBytes: bytes.length,
    owned:     true,
  };
}

const form = await dm.readForm(await readFile('contact.dmf'));

// recipientUserId is on the verified form descriptor.
const photo = await uploadFile('./id-photo.jpg', 'image/jpeg', form.recipientUserId);

await dm.submit({
  form,
  values: {
    full_name: 'Ada Lovelace',
    id_photo:  photo,        // <-- the ref goes in as the field value
  },
});
```

Python (`hashlib.sha256(b).hexdigest()` + `requests`/`httpx`) and .NET
(`SHA256.HashData` + `HttpClient`) follow the same three calls — the only
SDK-specific part is reading `recipientUserId` off the verified form.

## The value ref shapes

An `image` / `attachment` value is a JSON object. Three shapes, all valid:

| Shape | Looks like | When |
|---|---|---|
| **Owned URL** | `{ "url": "...", "hash": "...", "owned": true, "fileName": ..., "mime": ..., "sizeBytes": ... }` | You uploaded the bytes (the flow above). The normal case. |
| **External URL** | `{ "url": "https://cdn.example.com/x.png", "owned": false }` | You're pointing at a file you host elsewhere. No upload, no hash, FOBO doesn't store the bytes. |
| **Inline data URI** | `{ "dataUri": "data:image/png;base64,..." }` | Legacy / fully-portable. Bloats the envelope — avoid for anything but tiny files. |

`fileName`, `mime`, `sizeBytes` are optional metadata on any shape. At least one
of `url` or `dataUri` must be present. `image` and `attachment` use the **same**
ref shape — they differ only in how a renderer displays them (image renders
inline; attachment shows a download chip).

## Reading the bytes back

Reading the bytes is the receiver's job (the DataMaker app), not a sender's.
The owned URL in a stored ref is transient; the app re-derives a fresh
pre-signed **GET** with `GET /submissions/blob/{recipientUserId}/{hash}` →
`{ url, expiresAtUtc }`. Keep the `hash` accurate — it's what the bytes are
addressed by.
