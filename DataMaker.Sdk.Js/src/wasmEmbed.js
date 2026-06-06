'use strict';

// Wasm renderer embed — mount a Data Maker form using the full Uno-in-WebAssembly
// renderer (pixel-for-pixel with the desktop designer) instead of the lightweight
// HTML renderer (renderer.js). Browser-only; a no-op under Node.
//
// The Wasm bundle is served from a public static host (default below). We frame
// it in an <iframe> and hand it the form schema + recipient over postMessage: the
// iframe announces `datamaker:ready`, we reply with `datamaker:init`, and the app
// boots, renders, and — on submit — seals the values (sealed box) and POSTs to
// /submissions using its own copy of this SDK loaded inside the iframe. The
// recipient's public key is the only key involved and is public by design, so
// nothing secret crosses the frame boundary.

// Public host of the static Wasm renderer bundle. Overridable via
// config.wasmHost for self-hosting or staging.
const DEFAULT_WASM_HOST = 'https://hosted-forms.com/_wasm/';

// config:
//   recipientPublicKey  (required) — base64 X25519 key from the .dmf manifest
//   recipientUserId     (optional) — form owner's userId (file-attachment uploads)
//   apiBaseUrl          (optional) — submissions endpoint; defaults to the public one
//   afterSubmitText     (optional) — HTML/text shown in place of the form on success
//   wasmHost            (optional) — base URL of the Wasm bundle (DEFAULT_WASM_HOST)
//
// mount target — pass { root, form } explicitly, or rely on the DOM defaults:
//   root  — element to mount into (default #form-root)
//   form  — the form schema object, or read from a <script id="form-bundle"> JSON
//           ({ form, compiled, elementCss, paletteCss } — the .dmf v3 bundle)
function mountWasm(config, opts) {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    throw new Error('mountWasm is browser-only');
  }
  config = config || {};
  opts = opts || {};
  if (!config.recipientPublicKey) {
    throw new Error('mountWasm needs config.recipientPublicKey');
  }

  const root = opts.root || document.getElementById('form-root');
  if (!root) throw new Error('mountWasm: no root element (#form-root)');

  let form = opts.form;
  if (!form) {
    const bundleEl = opts.bundle || document.getElementById('form-bundle');
    if (!bundleEl) throw new Error('mountWasm: no form — pass opts.form or a #form-bundle script');
    const bundle = JSON.parse(bundleEl.textContent || '{}');
    form = bundle.form || bundle; // accept a bare form or a .dmf v3 bundle
  }

  const host = (config.wasmHost || DEFAULT_WASM_HOST).replace(/\/+$/, '');
  const hostOrigin = new URL(host, window.location.href).origin;

  const iframe = document.createElement('iframe');
  iframe.src = host + '/index.html?embed=pm';
  iframe.title = 'Form';
  iframe.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms');
  iframe.style.cssText = 'display:block;width:100%;border:0;min-height:640px;';

  function onMessage(e) {
    if (e.source !== iframe.contentWindow) return; // only our frame
    const d = e.data;
    if (!d || typeof d !== 'object') return;
    if (d.type === 'datamaker:ready') {
      // Push the form + recipient to the (known) host origin only.
      iframe.contentWindow.postMessage(
        {
          type: 'datamaker:init',
          form: form,
          recipientPublicKey: config.recipientPublicKey,
          recipientUserId: config.recipientUserId || null,
          apiBaseUrl: config.apiBaseUrl || null,
          afterSubmitText: config.afterSubmitText || null,
          // 'auto' (default) follows the browser; 'light'/'dark' force it.
          theme: config.theme || 'auto',
        },
        hostOrigin
      );
    } else if (d.type === 'datamaker:height') {
      const h = parseInt(d.height, 10);
      if (h > 0) iframe.style.height = h + 'px';
    }
  }

  window.addEventListener('message', onMessage);
  root.innerHTML = '';
  root.appendChild(iframe);

  // Teardown handle for SPAs that unmount the form.
  return function unmount() {
    window.removeEventListener('message', onMessage);
    if (iframe.parentNode) iframe.parentNode.removeChild(iframe);
  };
}

module.exports = { mountWasm, DEFAULT_WASM_HOST };
