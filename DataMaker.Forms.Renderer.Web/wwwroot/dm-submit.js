/**
 * dm-submit.js — wire @fobo-tools/datamaker as the web renderer's submit hook.
 *
 * The renderer (renderer.js) renders the form and collects values, but doesn't
 * encrypt or POST — it calls hooks.onSubmit({ form, col, values }). This glue
 * builds that hook from the SDK's createSubmitHandler so the embed seals the
 * values client-side (libsodium sealed box) and posts them to /submissions.
 *
 * Load order on an embed page:
 *   1. <script>window.DataMakerConfig = { recipientPublicKey, ... }</script>
 *   2. datamaker.browser.js   (exposes window.DataMaker — build via
 *      `npm run build:browser` in DataMaker.Sdk.Js, or load from
 *      https://unpkg.com/@fobo-tools/datamaker/dist/datamaker.browser.js)
 *   3. dm-submit.js           (this file — sets DataMakerRenderer.onSubmit)
 *   4. renderer.js            (auto-mounts #form-root + #form-bundle)
 *
 * Config (window.DataMakerConfig or passed to enableSubmit):
 *   recipientPublicKey  required — base64 X25519 key from the form's .dmf manifest
 *   apiBaseUrl          optional — defaults to the public submissions endpoint
 *   submitterId         optional — null (anonymous) unless the host knows it
 *   applyFormStyle      optional — false renders structure-only, dropping the
 *                       form author's design baked in the .dmf so this site's
 *                       own styles apply (the layout layer still works)
 *   onSuccess(result)   optional — { submissionId, editToken } after a 2xx
 *   onError(error)      optional — network / server failures
 */
(function () {
  'use strict';
  var ns = (window.DataMakerRenderer = window.DataMakerRenderer || {});

  // Apply the "render the author's .dmf design or not" flag as early as
  // possible — renderer.js reads DataMakerRenderer.applyFormStyle at mount.
  // Honored even when the page doesn't wire submit through this glue.
  ns.applyDesign = function (enabled) { ns.applyFormStyle = enabled; };

  ns.enableSubmit = function (config) {
    config = config || {};
    if (!window.DataMaker || typeof window.DataMaker.createSubmitHandler !== 'function') {
      throw new Error('datamaker.browser.js must load before dm-submit.js');
    }
    if (!config.recipientPublicKey) {
      throw new Error('DataMakerConfig needs recipientPublicKey');
    }
    if (config.applyFormStyle !== undefined) ns.applyFormStyle = config.applyFormStyle;

    var handler = window.DataMaker.createSubmitHandler({
      recipientPublicKey: config.recipientPublicKey,
      apiBaseUrl: config.apiBaseUrl,
      submitterId: config.submitterId || null,
      applyFormStyle: config.applyFormStyle,
      // Late-bound: renderer.js installs hooks.applyFieldErrors during mount,
      // after this runs, so read it off the namespace at call time.
      applyFieldErrors: function (m) {
        if (typeof ns.applyFieldErrors === 'function') ns.applyFieldErrors(m);
      },
      onSuccess: config.onSuccess,
      onError: config.onError,
    });

    ns.onSubmit = function (payload) { return handler(payload); };
    return ns.onSubmit;
  };

  // Honor a standalone design flag even without submit wiring.
  if (window.DataMakerConfig && window.DataMakerConfig.applyFormStyle !== undefined) {
    ns.applyFormStyle = window.DataMakerConfig.applyFormStyle;
  }

  // Auto-enable when config is already present at load.
  if (window.DataMakerConfig && window.DataMakerConfig.recipientPublicKey) {
    try {
      ns.enableSubmit(window.DataMakerConfig);
    } catch (e) {
      var el = document.getElementById('boot-error');
      if (el) { el.hidden = false; el.textContent = (el.textContent ? el.textContent + '\n\n' : '') + (e && e.message || e); }
    }
  }
})();
