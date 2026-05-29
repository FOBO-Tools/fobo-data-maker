# FOBO Data Maker

Open client tooling for [Data Maker](https://datamaker.fobo-tools.com/) forms —
submit records, render forms, and read the schema. Submissions are
**end-to-end encrypted**: values are sealed against the form owner's public key,
and the server only routes ciphertext.

📖 **Docs: https://fobo-tools.github.io/fobo-data-maker/**

## What's here

| Path | What |
|---|---|
| `DataMaker.Sdk.Js` | JavaScript / Node SDK (`@fobo-tools/datamaker`) + CLI |
| `DataMaker.Sdk.Py` | Python SDK (`datamaker-forms`) + CLI |
| `DataMaker.Sdk.Net` | .NET SDK (`DataMaker.Sdk`) + ASP.NET Core renderer (`DataMaker.Sdk.AspNetCore`) |
| `DataMaker.Zapier` | Zapier integration app |
| `DataMaker.Forms.Renderer.Terminal` | TUI form filler (`datamaker-form`) |
| `DataMaker.Forms.Renderer.Web/wwwroot` | Browser renderer assets (`renderer.js`, `fn.js`, CSS) |
| `DataMaker.Forms.Renderer.WordPress` | WordPress plugin |
| `DataMaker.Schema` | Form schema model (fields, layout, validation, styling) |
| `DataMaker.Expressions` | Expression engine + `Fn` library |
| `DataMaker.Forms.Signing.Verify` | `.dmf` bundle reader + signature/attestation verification |
| `DataMaker.Forms.Evaluation` | Form expression evaluator (VisibleWhen / Calculated / validation) |
| `FOBO.Auth` | OAuth (PKCE) client for authenticated forms |
| `DataMaker.Docs` | Documentation site (Astro Starlight) |

## Quick start

```js
// JavaScript / Node — npm install @fobo-tools/datamaker
import fs from 'node:fs';
import * as dm from '@fobo-tools/datamaker';
const form = await dm.readForm(fs.readFileSync('contact.dmf'));
await dm.submit({ form, values: { email: 'ada@example.com', name: 'Ada' } });
```

See the [docs](https://fobo-tools.github.io/fobo-data-maker/) for Python, .NET,
the renderers, the `.dmf` format, the full schema reference, and a guide to
building your own client or renderer.

## Build

- **.NET libraries + terminal:** `dotnet build FOBO-Data-Maker.slnx`
- **.NET SDK + tests:** `dotnet test DataMaker.Sdk.Net/DataMaker.Sdk.slnx`
- **JS SDK:** `cd DataMaker.Sdk.Js && npm ci && npm test`
- **Python SDK:** `cd DataMaker.Sdk.Py && pip install -e ".[test]" && pytest`
- **Docs:** `cd DataMaker.Docs && npm ci && npm run build`

## License

BSD-3-Clause © FOBO Tools

This repository is a generated, read-only mirror of the user-accessible subset
of Data Maker. Issues + PRs welcome.
