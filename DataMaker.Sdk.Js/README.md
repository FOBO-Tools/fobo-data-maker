# @fobo-tools/datamaker

Submit records to a [Data Maker](https://datamaker.fobo-tools.com/) form from
anywhere — Node, a serverless function, a build script, or the CLI.

A Data Maker form is distributed as a signed `.dmf` bundle. This SDK reads that
bundle, validates your values against its field schema, **sealed-box encrypts
them client-side against the form owner's public key**, and posts the ciphertext
to the public submissions endpoint. Everything except the final POST runs
offline; the server never sees your data in the clear.

## Install

```sh
npm install @fobo-tools/datamaker
```

Requires Node ≥ 18.20 (uses the global `fetch` and `crypto.randomUUID`).

## Library

```js
const fs = require('node:fs');
const dm = require('@fobo-tools/datamaker');

const dmf  = fs.readFileSync('contact.dmf');
const form = await dm.readForm(dmf); // verifies signature + form hash

const result = await dm.submit({
  form,
  values: { email: 'ada@example.com', full_name: 'Ada' },
});
// → { submissionId, editToken, formId }
```

You can also pass the raw bundle straight to `submit`:

```js
await dm.submit({ dmf, values: { email: 'ada@example.com' } });
```

### API

- **`readForm(dmfBytes, { verify = true })`** → form descriptor
  `{ formId, name, description, schemaVersion, submitPolicy, recipientUserId,
  recipientPublicKey, signer, envelopeVersion, fields, verified }`.
  Verifies the publisher's Ed25519 signature over the manifest and that
  `form.json` matches its signed hash. Throws `DmfError` on tampering. Pass
  `verify: false` only for trusted local fixtures.

- **`submit({ form | dmf, values, ... })`** → `{ submissionId, editToken, formId }`.
  Validates, seals, and posts. Options: `apiBaseUrl`, `submitterId`
  (default `null` = anonymous), `validate` (default `true`), `allowUnknown`,
  `verify`, `fetch` (inject a custom implementation).

- **`buildSubmission(form, values, opts)`** → `{ submissionId, envelope, payload, values }`.
  Validate + seal without sending — inspect, persist, or post it yourself.

- **`postSubmission(envelope, opts)`** → posts a pre-built envelope.

- **`validateValues(fields, input, opts)`** → `{ values, issues }`. Pure
  validation/coercion, no crypto.

- **Browser embed** — `createSubmitHandler(config)` wires the lightweight JS
  renderer's submit; `mountWasm(config)` frames the high-fidelity Wasm renderer.
  Both read `theme` from the config (`'auto'` default — follows the visitor's
  `prefers-color-scheme` and re-themes live; `'light'`/`'dark'` force one).

### Validation

`submit`/`buildSubmission` reject (with a `ValidationError` carrying
`.issues`): missing required fields, unknown keys (typo protection — set
`allowUnknown: true` to ignore), and values for read-only kinds (`calc`,
`heading`, `signature`). Values are coerced to the wire shape per kind:
numbers/decimals/money → `number`, booleans from `"yes"`/`"true"`/`1`,
multi-choice → array, choice membership checked unless the field allows custom.

File-upload kinds (`image`, `attachment`) are passed through as-is; building
the upload ref is out of scope for this version.

## CLI

```sh
# Inspect a bundle's fields, recipient, and signature status
datamaker inspect contact.dmf
datamaker inspect contact.dmf --json

# Submit
datamaker submit contact.dmf --field email=ada@example.com --field name=Ada
datamaker submit contact.dmf --data '{"email":"ada@example.com"}'
datamaker submit contact.dmf --data-file answers.json --dry-run
```

Flags: `--field k=v` (repeatable; repeats build an array), `--data <json>`,
`--data-file <path>`, `--api <url>`, `--submitter <id>`, `--dry-run`,
`--no-verify`, `--no-validate`.

## License

BSD-3-Clause © FOBO Tools
