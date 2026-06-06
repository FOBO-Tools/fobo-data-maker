/**
 * dm-wasm.js — mount a Data Maker form with the full Uno-in-WebAssembly renderer
 * (pixel-for-pixel with the desktop designer) instead of the lightweight HTML
 * renderer (renderer.js). The wasm counterpart to dm-submit.js + renderer.js.
 *
 * Use it when the HTML render looks different from your design. It's heavier
 * (the renderer ships as a WASM bundle, first load is slower), so the JS
 * renderer stays the default — opt in with `renderer: 'wasm'`.
 *
 * Load order on an embed page (NOTE: no renderer.js — this replaces it):
 *   1. <script>window.DataMakerConfig = { renderer:'wasm', recipientPublicKey, ... }</script>
 *   2. datamaker.browser.js   (exposes window.DataMaker)
 *   3. dm-wasm.js             (this file — frames the wasm renderer into #form-root)
 *
 * Config (window.DataMakerConfig):
 *   renderer            'wasm' — required for this glue to act (else it no-ops)
 *   recipientPublicKey  required — base64 X25519 key from the form's .dmf manifest
 *   recipientUserId     optional — form owner's userId (file-attachment uploads)
 *   apiBaseUrl          optional — submissions endpoint; defaults to the public one
 *   afterSubmitText     optional — shown in place of the form after a successful submit
 *   wasmHost            optional — base URL of the Wasm bundle (defaults to the public host)
 *
 * The form schema is read from the <script type="application/json" id="form-bundle">
 * the same way renderer.js reads it, so the page markup is unchanged.
 */
(function () {
  'use strict';

  var config = window.DataMakerConfig || {};
  // Only act when the wasm renderer was explicitly requested. This lets a page
  // safely include dm-wasm.js and flip renderers with a single prop.
  if (config.renderer !== 'wasm') return;

  function boot() {
    if (!window.DataMaker || typeof window.DataMaker.mountWasm !== 'function') {
      throw new Error('datamaker.browser.js must load before dm-wasm.js');
    }
    window.DataMaker.mountWasm(config);
  }

  try {
    boot();
  } catch (e) {
    var el = document.getElementById('boot-error');
    if (el) {
      el.hidden = false;
      el.textContent = (el.textContent ? el.textContent + '\n\n' : '') + (e && e.message || e);
    } else {
      throw e;
    }
  }
})();
