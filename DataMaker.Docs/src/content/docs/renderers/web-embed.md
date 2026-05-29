---
title: Web embed
description: Render a DataMaker form on any web page with the JS renderer, client-side or server-side encrypted.
---

The JS renderer turns a `.dmf v3` bundle into a live form in the browser:
fields, layout, conditional visibility, calculated fields, and validation. You
pair it with a submit hook that either seals in the browser (end-to-end) or
posts plaintext to your server to seal there.

## Plain HTML embed (client-side encryption)

End-to-end: the browser seals values against the recipient public key and posts
ciphertext to `/submissions`. Your server never sees plaintext.

```html
<div id="form-root"></div>
<script type="application/json" id="form-bundle">
  /* { form, compiled, elementCss, paletteCss } — the .dmf v3 bundle, server-injected */
</script>

<script>
  window.DataMakerConfig = {
    encrypt: 'client',
    recipientPublicKey: '<base64 from manifest.recipient.publicKey>',
    recipientUserId: '<manifest.recipient.userId>',
    // apiBaseUrl optional — defaults to the public endpoint
  };
</script>
<script src="datamaker.browser.js"></script>   <!-- the SDK browser bundle -->
<script src="dm-submit.js"></script>           <!-- wires submit -->
<script src="renderer.js"></script>            <!-- renders + auto-mounts -->
```

`renderer.js` auto-mounts on `#form-root` + `#form-bundle`. `dm-submit.js` reads
`DataMakerConfig` and installs the submit handler. The form bundle JSON is
produced server-side when the `.dmf` is published — read it out of the `.dmf v3`
(entries `form.json`, `compiled.json`, `elementCss.json`, `palette.css`).

### Render structure-only

Set `DataMakerConfig.applyFormStyle = false` to drop the form author's baked
design (palette + per-element CSS) and let your own site styles apply. The
structural layout layer still works.

## ASP.NET Core (TagHelper)

If you host on .NET, the `DataMaker.Sdk.AspNetCore` TagHelper does all of the
above from a `.dmf` path — see the [.NET SDK](/fobo-data-maker/sdks/dotnet/).

```cshtml
<datamaker-form dmf-path="forms/contact.dmf" encrypt="client" />
```

## Server-side encryption

Set `encrypt: 'server'` and a `submitUrl`. The browser posts **plaintext**
`{ formId, values }` to your endpoint, which validates + seals + forwards. Use
this when you don't want libsodium in the browser and your server is trusted
with the plaintext. The .NET `MapDataMakerSubmit` endpoint implements the
server half.

| | client | server |
|---|---|---|
| Who encrypts | browser | your server |
| Server sees plaintext | no | yes |
| Needs libsodium in browser | yes | no |
| Trust model | end-to-end | server-trusted |
