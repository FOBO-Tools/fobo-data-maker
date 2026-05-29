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

## Building the `values` object

`values` is keyed by each field's **`name`** (its storage key — *not* the
label). Inspect the form to see the names + kinds you need to fill:

```js
for (const f of form.fields) {
  console.log(f.key, '·', f.kind, f.required ? '(required)' : '');
}
// email · email (required)
// full_name · text
// age · number
// subscribed · boolean
// plan · choice
// interests · multi-choice
// signup_date · date
// budget · money
```

Then map your data to those names, using the value type each kind expects:

```js
const values = {
  email:       'ada@example.com',  // email        → string
  full_name:   'Ada Lovelace',     // text         → string
  age:          37,                // number        → number  (37, not "37")
  subscribed:   true,              // boolean       → boolean
  plan:        'pro',              // choice        → string (one of the choice values)
  interests:   ['news', 'beta'],   // multi-choice  → string[]
  signup_date: '2026-05-29',       // date          → "YYYY-MM-DD"
  budget:       1999.99,           // money         → number
};

await dm.submit({ form, values });
```

For `image` / `attachment` fields, upload the bytes first and pass the returned
ref as the value — see [Files & attachments](/fobo-data-maker/concepts/files/).

The full value type for every kind is in the
[field kinds reference](/fobo-data-maker/schema/field-kinds/).

:::tip[The SDK is forgiving — but be intentional]
By default `submit` **coerces** (`"37"` → `37`, `"yes"` → `true`, a single
choice → `["x"]` for multi-choice) and **validates**: it throws a
`ValidationError` (with `.issues`) for a missing required field, an unknown key
(typo protection), a value for a read-only kind, or a choice that isn't in the
list. Pass `validate: false` to skip, or `allowUnknown: true` to ignore extra
keys.
:::

```js
try {
  await dm.submit({ form, values });
} catch (err) {
  if (err.code === 'VALIDATION_FAILED') {
    for (const i of err.issues) console.error(`${i.field}: ${i.message}`);
  } else throw err;
}
```

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
