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
    recipientPublicKey: '<base64 from manifest.recipient.publicKey>', // required — the submission destination
    // apiBaseUrl optional — defaults to https://datamaker-api.fobo-tools.com
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

## Wasm renderer (exact design fidelity)

The JS renderer rebuilds your form in HTML/CSS. It matches most designs closely,
but a few advanced layouts render differently. When that happens, switch to the
**Wasm renderer** — the full desktop designer compiled to WebAssembly, so the
embedded form is a pixel-for-pixel match of what you built. It's heavier (the
renderer downloads as a WASM bundle, so the first load is slower), which is why
the JS renderer stays the default. Opt in with a single prop:

```html
<div id="form-root"></div>
<script type="application/json" id="form-bundle">
  /* { form, compiled, elementCss, paletteCss } — the .dmf v3 bundle */
</script>

<script>
  window.DataMakerConfig = {
    renderer: 'wasm',                              // ← the only change
    recipientPublicKey: '<base64 from manifest.recipient.publicKey>',
    // apiBaseUrl optional — defaults to the public endpoint
  };
</script>
<script src="datamaker.browser.js"></script>   <!-- the SDK browser bundle -->
<script src="dm-wasm.js"></script>              <!-- frames the wasm renderer (replaces renderer.js + dm-submit.js) -->
```

`dm-wasm.js` reads `renderer: 'wasm'`, mounts the renderer in an `<iframe>` on
`#form-root`, and hands it the form from `#form-bundle`. Submission still seals
client-side against `recipientPublicKey` — the iframe loads its own copy of the
SDK and posts sealed ciphertext to `/submissions`, exactly like the JS path. The
iframe auto-sizes to the form.

Extra config props for the Wasm renderer:

- `afterSubmitText` — text shown in place of the form after a successful submit.
- `wasmHost` — base URL of the renderer bundle. Defaults to the public host; set
  it only when self-hosting the bundle.

Programmatic mount (SPAs):

```js
const unmount = DataMaker.mountWasm(window.DataMakerConfig, {
  root: document.querySelector('#my-form'),
  // form: <schema object>   // or read from #form-bundle by default
});
// later: unmount();
```

## Light / dark theme

Both renderers (HTML and Wasm) resolve the theme the same way. Set `theme` in
`DataMakerConfig`:

```js
window.DataMakerConfig = {
  theme: 'auto',   // 'auto' (default) | 'light' | 'dark'
  recipientPublicKey: '…',
};
```

- **`auto`** (default) follows the visitor's browser `prefers-color-scheme` and
  **updates live** if they switch their OS between light and dark.
- **`light`** / **`dark`** force one mode regardless of the browser.

The form carries both palettes (designed in the Styling tab); the theme just
picks which one renders. There's no per-form toggle button on an embed — the
embedding site owns the theme via this prop.

## Hosted forms

Publishing to `{subdomain}.hosted-forms.com`? Two settings live in the
**Publish to the web** dialog:

- **Renderer** — *HTML* (default) or *Exact*. Pick **Exact** when the hosted
  page looks different from your design (serves the Wasm renderer).
- **Theme** — *Auto* (default, follow the visitor's browser, live) / *Light* /
  *Dark*.

Everything else (header, custom CSS, hidden elements, encrypted delivery) is
unchanged.

The plain-JS embed is **always end-to-end**: it seals client-side against
`recipientPublicKey` and posts ciphertext. There is no `encrypt` or `submitUrl`
config — those are ASP.NET Core TagHelper attributes (below).

## ASP.NET Core (TagHelper)

If you host on .NET, the `DataMaker.Sdk.AspNetCore` TagHelper does all of the
above from a `.dmf` path — see the [.NET SDK](/fobo-data-maker/sdks/dotnet/).

```cshtml
<datamaker-form dmf-path="forms/contact.dmf" encrypt="client" />
```

The TagHelper adds a server-side encryption option the plain-JS path doesn't
have, via two attributes:

```cshtml
<datamaker-form dmf-path="forms/contact.dmf"
                encrypt="server" submit-url="/datamaker/submit" />
```

With `encrypt="server"` the browser posts **plaintext** `{ formId, values }` to
`submit-url`, and your endpoint validates + seals + forwards. The .NET
`MapDataMakerSubmit` endpoint implements the server half. Use it when you don't
want libsodium in the browser and your server is trusted with the plaintext.

| | `encrypt="client"` (default) | `encrypt="server"` |
|---|---|---|
| Who encrypts | browser | your server |
| Server sees plaintext | no | yes |
| Needs libsodium in browser | yes | no |
| Trust model | end-to-end | server-trusted |
