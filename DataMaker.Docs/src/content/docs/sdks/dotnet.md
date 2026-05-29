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
