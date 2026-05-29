# DataMaker .NET SDK

Submit records to a [Data Maker](https://datamaker.fobo-tools.com/) form from
.NET, and render forms in ASP.NET Core. Two packages, two segments:

- **`DataMaker.Sdk`** — pure submit logic. Read a signed `.dmf` bundle,
  validate values, sealed-box encrypt, POST to the public submissions endpoint.
- **`DataMaker.Sdk.AspNetCore`** — an MVC/Razor TagHelper that renders a `.dmf`
  form (hosting the JS renderer) plus a server-side encrypt endpoint.

Standalone: own `.dmf` reader + libsodium sealed-box (`Sodium.Core`), wire-
identical to the Data Maker app and to the JS/Python SDKs. No dependency on the
app internals.

## DataMaker.Sdk (submit)

```csharp
using DataMaker.Sdk;

var client = new DataMakerClient();
var form   = DataMakerClient.ReadForm(File.ReadAllBytes("contact.dmf")); // verifies signature + hash

var result = await client.SubmitAsync(form, new Dictionary<string, object?>
{
    ["email"]     = "ada@example.com",
    ["full_name"] = "Ada",
});
// result.SubmissionId, result.EditToken, result.FormId
```

- `ReadForm(bytes, verify, includeRenderBundle)` → `FormDescriptor`. Verifies the
  publisher's Ed25519 manifest signature and that `form.json` matches its signed
  hash; throws `DmfException` on tampering.
- `BuildSubmission(form, values, opts)` → validate + seal without sending.
- `SubmitAsync(form|dmfBytes, values, opts)` → validate, seal, POST.
- Validation throws `ValidationException` (with `.Issues`) for missing required
  fields, unknown keys, read-only kinds, and bad choices; values are coerced per
  field kind. `SubmissionException` carries a non-2xx status.

## DataMaker.Sdk.AspNetCore (render)

Add the TagHelper, then drop a form into a Razor view:

```cshtml
@addTagHelper *, DataMaker.Sdk.AspNetCore

@* End-to-end: the browser seals values and posts ciphertext to /submissions.
   Your server never sees plaintext. *@
<datamaker-form dmf-path="forms/contact.dmf" encrypt="client" />

@* Server-side: the browser posts plaintext to your endpoint, which seals. *@
<datamaker-form dmf-path="forms/contact.dmf" encrypt="server" submit-url="/datamaker/submit" />
```

For server mode, map the seal endpoint (resolves a formId to its `.dmf`):

```csharp
app.MapDataMakerSubmit("/datamaker/submit",
    formId => File.ReadAllBytes($"forms/{formId}.dmf"));
```

Both modes host the existing JS renderer (full styling, conditional visibility,
calc fields, validation) — the render bundle is read straight from the `.dmf v3`,
so no server-side bundle builder is needed. `apply-form-style="false"` renders
structure-only (drops the `.dmf` author design so your site's CSS applies).

The renderer assets (renderer.js, fn.js, layout.css, styles.css, dm-submit.js)
ship as static web assets at `/_content/DataMaker.Sdk.AspNetCore/`. Client mode
also needs `datamaker.browser.js` — loaded from unpkg by default; override with
`browser-bundle-url`.

## Build & test

```sh
cd src/DataMaker.Sdk          && dotnet build
cd src/DataMaker.Sdk.AspNetCore && dotnet build
cd tests/DataMaker.Sdk.Tests  && dotnet test
```

## License

BSD-3-Clause © FOBO Tools
