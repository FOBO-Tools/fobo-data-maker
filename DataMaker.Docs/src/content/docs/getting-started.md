---
title: Getting started
description: How a DataMaker form, the .dmf bundle, and submissions fit together.
---

DataMaker forms are designed once and consumed anywhere. As a developer you'll
work with three things: the **`.dmf` bundle**, the **submission API**, and the
**form schema**.

## The shape of it

```
 form author                    you (a client)                form owner
 ───────────                    ──────────────                ──────────
 designs a form                 read the .dmf                 holds the X25519
 → publishes a .dmf  ─────────▶ render / collect values       private key
   (signed bundle)              validate + sealed-box encrypt
                                POST ciphertext  ───────────▶ /submissions
                                                              decrypts & stores
```

1. **The `.dmf` bundle** is a signed ZIP carrying the form definition, the
   publisher's signature, and the recipient's **public** key. You verify the
   signature and read what you need from it. See [The .dmf bundle](/fobo-data-maker/concepts/dmf/).
2. **Submitting** means: validate values against the schema, **sealed-box
   encrypt** them against the recipient public key, and POST the ciphertext to
   the public submissions endpoint. The server never sees plaintext. See
   [Submission & encryption](/fobo-data-maker/concepts/submission/).
3. **The schema** inside the bundle describes the fields, layout, validation,
   and expressions — enough to render the form or build your own tooling. See
   [the schema reference](/fobo-data-maker/schema/form-model/).

## Fastest path: an SDK

If you just want to submit records, use an SDK — it does verify + validate +
encrypt + post for you.

```js
// JavaScript / Node
import fs from 'node:fs';
import * as dm from '@fobo-tools/datamaker';

const form = await dm.readForm(fs.readFileSync('contact.dmf'));
await dm.submit({ form, values: { email: 'ada@example.com', name: 'Ada' } });
```

```python
# Python
import datamaker as dm

form = dm.read_form(open("contact.dmf", "rb").read())
dm.submit(form=form, values={"email": "ada@example.com", "name": "Ada"})
```

```csharp
// .NET
using DataMaker.Sdk;
var form = DataMakerClient.ReadForm(File.ReadAllBytes("contact.dmf"));
await new DataMakerClient().SubmitAsync(form,
    new Dictionary<string, object?> { ["email"] = "ada@example.com", ["name"] = "Ada" });
```

Full guides: [JavaScript](/fobo-data-maker/sdks/javascript/) · [Python](/fobo-data-maker/sdks/python/) ·
[.NET](/fobo-data-maker/sdks/dotnet/) · [PHP](/fobo-data-maker/sdks/php/).

## Render a form

- **Web** — drop in the JS renderer + a `<script>` of config. See
  [Web embed](/fobo-data-maker/renderers/web-embed/).
- **WordPress** — install the plugin, upload a `.dmf`, use a shortcode/block.
  See [WordPress](/fobo-data-maker/renderers/wordpress/).
- **Terminal** — fill a form in the TUI. See [Terminal](/fobo-data-maker/renderers/terminal/).
