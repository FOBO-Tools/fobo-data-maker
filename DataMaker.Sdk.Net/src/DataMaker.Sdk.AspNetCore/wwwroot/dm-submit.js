/**
 * dm-submit.js (ASP.NET SDK build) — wires the Data Maker web renderer's
 * submit hook in one of two modes, driven by window.DataMakerConfig.encrypt:
 *
 *   "client"  end-to-end: datamaker.browser.js seals the values in the browser
 *             and POSTs the ciphertext straight to /submissions. The server
 *             never sees plaintext. Needs recipientPublicKey + recipientUserId.
 *
 *   "server"  the renderer POSTs the plaintext values to your MVC endpoint
 *             (submitUrl); the DataMaker.Sdk.AspNetCore endpoint validates +
 *             seals server-side and forwards to /submissions. No libsodium in
 *             the browser; your server is trusted with the plaintext.
 *
 * applyFormStyle=false renders structure-only (drops the .dmf author design).
 */
(function () {
  'use strict';
  var ns = (window.DataMakerRenderer = window.DataMakerRenderer || {});
  ns.applyDesign = function (enabled) { ns.applyFormStyle = enabled; };

  function postJson(url, body) {
    return fetch(url, {
      method: 'POST',
      headers: { 'content-type': 'application/json' },
      body: JSON.stringify(body),
    });
  }

  function fieldErrors(map) { if (typeof ns.applyFieldErrors === 'function') ns.applyFieldErrors(map); }

  ns.enableSubmit = function (cfg) {
    cfg = cfg || {};
    if (cfg.applyFormStyle !== undefined) ns.applyFormStyle = cfg.applyFormStyle;

    if (cfg.encrypt === 'server') {
      var url = cfg.submitUrl || '/datamaker/submit';
      ns.onSubmit = function (payload) {
        var formId = (payload && payload.form && payload.form.id) || cfg.formId;
        return postJson(url, { formId: formId, values: (payload && payload.values) || {} })
          .then(function (r) { return r.text().then(function (t) { return { status: r.status, text: t }; }); })
          .then(function (res) {
            var data = {};
            try { data = JSON.parse(res.text); } catch (e) {}
            if (res.status < 200 || res.status >= 300) {
              if (Array.isArray(data.issues)) {
                var m = {}; data.issues.forEach(function (i) { m[i.field] = i.message; }); fieldErrors(m);
                return { ok: false, issues: data.issues };
              }
              if (cfg.onError) cfg.onError(new Error('submit failed: ' + res.status));
              return { ok: false, status: res.status };
            }
            if (cfg.onSuccess) cfg.onSuccess(data);
            return { ok: true, submissionId: data.submissionId, editToken: data.editToken };
          });
      };
      return ns.onSubmit;
    }

    // client (end-to-end) mode
    if (!window.DataMaker || typeof window.DataMaker.createSubmitHandler !== 'function') {
      throw new Error('datamaker.browser.js must load before dm-submit.js for client-side encryption');
    }
    var handler = window.DataMaker.createSubmitHandler({
      recipientPublicKey: cfg.recipientPublicKey,
      recipientUserId: cfg.recipientUserId,
      apiBaseUrl: cfg.apiBaseUrl,
      submitterId: cfg.submitterId || null,
      applyFormStyle: cfg.applyFormStyle,
      applyFieldErrors: fieldErrors,
      onSuccess: cfg.onSuccess,
      onError: cfg.onError,
    });
    ns.onSubmit = function (p) { return handler(p); };
    return ns.onSubmit;
  };

  var c = window.DataMakerConfig;
  if (c) {
    if (c.applyFormStyle !== undefined) ns.applyFormStyle = c.applyFormStyle;
    if (c.encrypt === 'server' || c.recipientPublicKey) {
      try { ns.enableSubmit(c); }
      catch (e) {
        var el = document.getElementById('boot-error');
        if (el) { el.hidden = false; el.textContent = (el.textContent ? el.textContent + '\n\n' : '') + (e && e.message || e); }
      }
    }
  }
})();
