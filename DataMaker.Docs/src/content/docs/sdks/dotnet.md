---
title: .NET SDK
description: Submit records and render forms in ASP.NET Core with DataMaker.Sdk and DataMaker.Sdk.AspNetCore.
---

Two packages: **`DataMaker.Sdk`** (submit) and **`DataMaker.Sdk.AspNetCore`**
(render a `.dmf` in MVC/Razor). Standalone — own `.dmf` reader + libsodium
sealed box (`Sodium.Core`), wire-identical to the JS/Python SDKs.

## DataMaker.Sdk — submit

```csharp
using DataMaker.Sdk;

var client = new DataMakerClient();
var form   = DataMakerClient.ReadForm(File.ReadAllBytes("contact.dmf")); // verifies sig + hash

var result = await client.SubmitAsync(form, new Dictionary<string, object?>
{
    ["email"]     = "ada@example.com",
    ["full_name"] = "Ada",
});
// result.SubmissionId, result.EditToken, result.FormId
```

- `ReadForm(bytes, verify, includeRenderBundle)` → `FormDescriptor`; throws
  `DmfException` on tampering.
- `BuildSubmission(form, values, opts)` → validate + seal without sending.
- `SubmitAsync(form | dmfBytes, values, opts)` → validate, seal, POST.
- Validation throws `ValidationException` (`.Issues`); a non-2xx throws
  `SubmissionException` (`.Status`). Inject an `HttpClient` for auth/proxy.

### Building the values dictionary

`values` is `Dictionary<string, object?>` keyed by each field's **`Key`** (its
storage name — *not* the label). Inspect the form to see names + kinds:

```csharp
foreach (var f in form.Fields)
    Console.WriteLine($"{f.Key} · {f.Kind} {(f.Required ? "(required)" : "")}");
// email · email (required)
// age · number   ·   subscribed · boolean   ·   plan · choice   ·   interests · multi-choice
```

Then map your data, using the CLR type each kind expects:

```csharp
var values = new Dictionary<string, object?>
{
    ["email"]       = "ada@example.com",          // email        → string
    ["full_name"]   = "Ada Lovelace",             // text         → string
    ["age"]         = 37,                         // number        → long/int
    ["subscribed"]  = true,                       // boolean       → bool
    ["plan"]        = "pro",                       // choice        → string (a choice value)
    ["interests"]   = new[] { "news", "beta" },   // multi-choice  → string[]
    ["signup_date"] = "2026-05-29",               // date          → "yyyy-MM-dd"
    ["budget"]      = 1999.99,                     // money         → double/decimal
};

await client.SubmitAsync(form, values);
```

Full value type per kind: the
[field kinds reference](/fobo-data-maker/schema/field-kinds/). By default
`SubmitAsync` coerces (`"37"` → `37`, `"yes"` → `true`) and validates — catch
`ValidationException` and read `.Issues` (`Field` + `Message`); pass
`new SubmitOptions { Validate = false }` to skip or `AllowUnknown = true` to
ignore extra keys.

## DataMaker.Sdk.AspNetCore — render

```cshtml
@addTagHelper *, DataMaker.Sdk.AspNetCore

@* End-to-end: the browser seals values and posts ciphertext to /submissions. *@
<datamaker-form dmf-path="forms/contact.dmf" encrypt="client" />

@* Server-side: the browser posts plaintext to your endpoint, which seals. *@
<datamaker-form dmf-path="forms/contact.dmf" encrypt="server" submit-url="/datamaker/submit" />
```

For server mode, map the seal endpoint:

```csharp
app.MapDataMakerSubmit("/datamaker/submit",
    formId => File.ReadAllBytes($"forms/{formId}.dmf"));
```

Both modes host the JS renderer (full styling, conditional logic, validation),
reading the bundle straight from the `.dmf v3`. `apply-form-style="false"`
renders structure-only so your site's CSS applies. See
[Web embed](/fobo-data-maker/renderers/web-embed/) for the encryption-mode trade-offs.

## Install

```sh
dotnet add package DataMaker.Sdk
dotnet add package DataMaker.Sdk.AspNetCore   # for the renderer
```
