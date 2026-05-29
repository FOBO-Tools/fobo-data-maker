---
title: JavaScript / Node SDK
description: Submit records to a DataMaker form from Node with @fobo-tools/datamaker.
---

`@fobo-tools/datamaker` reads a `.dmf`, validates, sealed-box encrypts, and
posts. Node ≥ 18.20 (uses global `fetch` + Web Crypto). Ships TypeScript types.

## Install

```sh
npm install @fobo-tools/datamaker
```

## Submit

```js
import fs from 'node:fs';
import * as dm from '@fobo-tools/datamaker';

const dmf  = fs.readFileSync('contact.dmf');
const form = await dm.readForm(dmf); // verifies signature + form hash

const result = await dm.submit({
  form,
  values: { email: 'ada@example.com', full_name: 'Ada' },
});
// → { submissionId, editToken, formId }
```

Or pass the bundle straight in: `await dm.submit({ dmf, values })`.

## API

- **`readForm(dmfBytes, { verify = true })`** → form descriptor (`formId`,
  `name`, `schemaVersion`, `submitPolicy`, `recipientUserId`,
  `recipientPublicKey`, `signer`, `fields`, `verified`). Throws `DmfError` on
  tampering.
- **`submit({ form | dmf, values, ...opts })`** → `{ submissionId, editToken,
  formId }`. Options: `apiBaseUrl`, `submitterId` (default `null`), `validate`
  (default `true`), `allowUnknown`, `verify`, `fetch`.
- **`buildSubmission(form, values, opts)`** → seal without sending
  (`{ submissionId, envelope, payload, values }`).
- **`postSubmission(envelope, opts)`** → post a pre-built envelope.
- **`validateValues(fields, input, opts)`** → `{ values, issues }`.

Validation throws `ValidationError` (with `.issues`) for missing required
fields, unknown keys, read-only kinds, and bad choices. `SubmissionError`
carries a non-2xx `.status`.

## CLI

```sh
datamaker inspect contact.dmf
datamaker submit contact.dmf --field email=ada@example.com --field full_name=Ada
datamaker submit contact.dmf --data-file answers.json --dry-run
```

## Browser

A bundled build (`dist/datamaker.browser.js`, global `DataMaker`) powers the
[web embed](/fobo-data-maker/renderers/web-embed/). `createSubmitHandler(config)` adapts the web
renderer's `onSubmit` to a sealed POST.
