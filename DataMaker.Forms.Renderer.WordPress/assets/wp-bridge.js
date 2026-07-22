/**
 * DataMaker → WordPress bridge. Runs before renderer.js, so the renderer's
 * `window.DataMakerRenderer.{onSubmit,onSave,onReset}` hooks are wired up
 * by the time it boots.
 *
 * Three jobs:
 *  1. Move the bundle JSON from the inline <script id="dm-bundle-..."> into
 *     the renderer's expected <script id="form-bundle"> slot.
 *  2. Wire submit/save/reset to the WP REST proxy (POST → /fobo-data-maker/v1/submit,
 *     PUT-style update → /fobo-data-maker/v1/update).
 *  3. Manage the localStorage edit flow: stash the editToken returned by
 *     POST; on a subsequent visit show a "Continue editing?" banner.
 */
(function () {
  'use strict';

  // Localizable strings. WP `wp_localize_script` populates
  // `window.DataMakerRenderer.i18n`; missing keys fall back to inline
  // English so the bridge never shows an empty button.
  function t(key, fallback) {
    try {
      const dict = window.DataMakerRenderer && window.DataMakerRenderer.i18n;
      if (dict && typeof dict[key] === 'string' && dict[key] !== '') return dict[key];
    } catch (_) {}
    return fallback;
  }

  // Run synchronously, not on DOMContentLoaded. The script is enqueued with
  // defer + listed as a dependency of renderer.js — defer guarantees the DOM
  // is parsed when this runs, AND that we run before renderer.js's IIFE
  // bootstraps. If we waited for DOMContentLoaded, renderer.js (also defer)
  // would execute first and throw "form-bundle script element missing".
  boot();

  function boot() {
    const mounts = document.querySelectorAll('.dm-form-mount');
    if (!mounts.length) return;

    // Multi-form per page: iterate every shortcode/block mount on the
    // page and call renderer.js's `mount(root, bundle, hooks)` API once
    // per. Each call gets its own scope inside the renderer, so values,
    // touched, validation state, edit-flow context — none of it leaks
    // between forms. Bridge keeps a per-mount hooks object so submits,
    // resumes and field-error echo all route to the right form.
    for (const m of mounts) initMount(m);
  }

  /** Push a mount() request — falls back to a queue when renderer.js
   *  hasn't loaded yet, so the bridge can run before the renderer
   *  script (defer order is implementation-defined across browsers). */
  function callMount(rootEl, bundleEl, perMountHooks) {
    const ns = (window.DataMakerRenderer = window.DataMakerRenderer || {});
    if (typeof ns.mount === 'function') {
      ns.mount(rootEl, bundleEl, perMountHooks);
    } else {
      (ns._pending = ns._pending || []).push([rootEl, bundleEl, perMountHooks]);
    }
  }

  function initMount(mount) {
    const bundleId   = mount.getAttribute('data-bundle');
    const bundleEl   = document.getElementById(bundleId);
    if (!bundleEl) return;
    const root       = mount.querySelector('[data-dm-form-root]');
    if (!root) return;

    const formId    = mount.getAttribute('data-form-id')    || '';
    const slug      = mount.getAttribute('data-form-slug')  || '';
    const restBase  = (mount.getAttribute('data-rest-base') || '').replace(/\/+$/, '');
    const editFlow  = mount.getAttribute('data-edit-flow')  === '1';
    const afterUrl  = mount.getAttribute('data-after-submit-url') || '';
    const successMd = mount.getAttribute('data-success-message-md') || '';
    const useTheme  = mount.getAttribute('data-use-form-theme') !== '0';
    const honeypotOn      = mount.getAttribute('data-honeypot')         === '1';
    const consentRequired = mount.getAttribute('data-consent-required') === '1';
    const turnstileOn     = mount.getAttribute('data-turnstile')        === '1';
    const recipientPubkey = mount.getAttribute('data-recipient-pubkey') || '';

    // When the host opts out of the form's DataMaker theme, strip the brand
    // bits from the bundle BEFORE renderer.js parses it. Layout, fields,
    // compiled VisibleWhen / Calculated all stay; only the palette CSS-vars
    // and the per-element resolved-style overrides go. styles.css's neutral
    // baseline + the WP theme's typography then drive the look.
    if (!useTheme) {
      try {
        const parsed = JSON.parse(bundleEl.textContent);
        parsed.paletteCss = '';
        parsed.elementCss = {};
        bundleEl.textContent = JSON.stringify(parsed);
      } catch (_) { /* malformed bundle — leave it alone, renderer will surface */ }
    }

    // Per-mount hooks object. The renderer reads onSubmit/onSave/onReset
    // from this object (instead of the singleton window.DataMakerRenderer)
    // so two forms on one page get isolated submit handlers, validation
    // error echo, and edit-flow contexts. renderer.js stamps applyFieldErrors
    // and __pendingHydrate onto this same object so the bridge can reach them.
    const hooks = {};

    // E2E-encrypt image/attachment bytes in the browser before renderer.js's
    // direct-to-S3 PUT (#45). Blob bytes skip the PHP proxy (pre-signed PUT),
    // so the seal must run client-side against the form's recipient pubkey.
    // dm-blob-crypto.min.js (tweetnacl + sealedbox) exposes the sealer as a
    // global; it's enqueued as a hard dependency of this bridge. No pubkey
    // (older bundle) → leave encryptBlob unset so the renderer falls back to
    // the inline data-URI path (still works, just not E2E).
    if (recipientPubkey &&
        window.DataMakerBlobCrypto &&
        typeof window.DataMakerBlobCrypto.seal === 'function') {
      hooks.encryptBlob = function (bytes) {
        return window.DataMakerBlobCrypto.seal(bytes, recipientPubkey);
      };
    }
    const lsKey = 'dm:' + slug;

    // Gate submit on the consent checkbox when this form requires it.
    // Reads the checkbox at submit-time (not init-time) so flipping it
    // off-and-on after a failed attempt is honored. The hidden honeypot
    // input value is collected the same way and forwarded as `hp` —
    // SubmitProxy rejects when non-empty.
    function gateAndExtract() {
      if (consentRequired) {
        const cb = mount.querySelector('.dm-consent-checkbox');
        if (!cb || !cb.checked) {
          const consentEl = mount.querySelector('.dm-consent');
          if (consentEl) {
            consentEl.classList.add('dm-consent-invalid');
            const hint = consentEl.querySelector('.dm-consent-hint');
            if (!hint) {
              const h = document.createElement('div');
              h.className = 'dm-consent-hint';
              h.textContent = t('consent_required_hint', 'Please tick the consent box to submit.');
              consentEl.appendChild(h);
            }
          }
          return null;
        }
      }
      const hp = honeypotOn ? (mount.querySelector('.dm-hp-input') || {}).value : '';
      // Turnstile injects a hidden <input name="cf-turnstile-response">
      // inside the widget container once the challenge passes. Empty
      // string = user hasn't completed it yet → block the submit with a
      // visible hint, mirroring the consent gate flow. Server re-verifies
      // with Cloudflare anyway, so this is purely UX.
      let captcha = '';
      if (turnstileOn) {
        const tEl = mount.querySelector('[name="cf-turnstile-response"]');
        captcha = tEl ? (tEl.value || '') : '';
        if (!captcha) {
          const widget = mount.querySelector('.dm-turnstile');
          if (widget) {
            widget.classList.add('dm-turnstile-invalid');
            let hint = widget.querySelector('.dm-turnstile-hint');
            if (!hint) {
              hint = document.createElement('div');
              hint.className = 'dm-turnstile-hint';
              hint.textContent = t('captcha_required_hint', 'Please complete the challenge to submit.');
              widget.appendChild(hint);
            }
          }
          return null;
        }
      }
      return { hp: hp || '', consent: consentRequired, captcha: captcha };
    }

    // Double-submit guard. While a POST is in flight every submit-style
    // button on this mount is disabled — including the auto `.dm-submit`
    // row, schema ButtonColumns with Action=Submit/Save, and the resume
    // banner buttons. Re-enabled after the response (success or error)
    // resolves so the user can retry on network failure. Tracked per
    // mount so a multi-form page (when v0.2 supports it) doesn't lock
    // out the second form when the first is submitting.
    let inFlight = false;
    function setSubmitBusy(busy) {
      inFlight = !!busy;
      const sel = '.dm-submit, .dm-btn[data-action="Submit"], .dm-btn[data-action="Save"]';
      mount.querySelectorAll(sel).forEach(function (btn) {
        btn.disabled = !!busy;
        btn.classList.toggle('dm-busy', !!busy);
        if (busy) btn.setAttribute('aria-busy', 'true');
        else      btn.removeAttribute('aria-busy');
      });
    }

    function submitOnce(isUpdate, ctx) {
      if (inFlight) return;
      const extra = gateAndExtract();
      if (extra === null) return;
      const editCtx = isUpdate ? hooks.__editContext : null;
      setSubmitBusy(true);
      postSubmit(restBase, isUpdate, slug, ctx.values, editCtx, extra)
        .then(function (r) {
          try { onPosted(r, slug, lsKey, root, editFlow, afterUrl, successMd, mount); }
          finally { setSubmitBusy(false); }
        })
        .catch(function () { setSubmitBusy(false); });
    }

    hooks.onSubmit = function (ctx) { submitOnce(false, ctx); };
    hooks.onSave   = hooks.onSubmit;
    hooks.onReset  = function () {
      if (editFlow) try { window.localStorage.removeItem(lsKey); } catch (_) {}
    };
    // Storage v2: forward renderer.js's uploadSlot hook to the WP REST
    // proxy `/upload-slot`, which talks to the Lambda's
    // POST /submissions/upload-slot and returns the pre-signed PUT URL.
    // Failure surfaces as null so renderer.js falls back to inline
    // data-URI (legacy behaviour — the form still has a value).
    hooks.uploadSlot = async function (slotReq) {
      const url = restBase.indexOf('?rest_route=') >= 0
        ? restBase + '/upload-slot'
        : restBase.replace(/\/+$/, '') + '/upload-slot';
      try {
        const resp = await fetch(url, {
          method:  'POST',
          headers: { 'Content-Type': 'application/json' },
          body:    JSON.stringify({
            slug:      slug,
            hash:      slotReq.hash,
            mime:      slotReq.mime,
            sizeBytes: slotReq.sizeBytes,
          }),
        });
        if (!resp.ok) return null;
        const body = await resp.json();
        return (body && body.url) ? { url: body.url, key: body.key } : null;
      } catch (_) {
        return null;
      }
    };

    if (editFlow) {
      const stored = readLs(lsKey);
      if (stored && stored.submissionId && stored.editToken) {
        showResumeBanner(root, stored, function () {
          // User chose Edit — stash the saved values + edit context on
          // THIS mount's hooks object so renderer.js's hydration block
          // picks them up after mount() runs.
          hooks.__pendingHydrate = stored.values || {};
          hooks.__editContext    = { submissionId: stored.submissionId, editToken: stored.editToken };
          // Switch this mount's onSubmit to PUT mode.
          hooks.onSubmit = function (ctx) { submitOnce(true, ctx); };
          hooks.onSave   = hooks.onSubmit;
        }, function () {
          try { window.localStorage.removeItem(lsKey); } catch (_) {}
        });
      }
    }

    callMount(root, bundleEl, hooks);
  }

  function postSubmit(restBase, isUpdate, slug, values, editCtx, extra) {
    // restBase comes from PHP `rest_url('fobo-data-maker/v1')` on the mount —
    // it already respects the site's permalink setting (plain vs pretty),
    // so we never hardcode `/wp-json/...` which breaks when the site
    // hasn't enabled pretty permalinks and WP routes the URL to the
    // homepage instead of the REST handler.
    // Plain-permalinks mode → restBase is `…/index.php?rest_route=/fobo-data-maker/v1`
    // and the endpoint name extends the rest_route query value: append
    // `/submit` to the end of the string (query-string land — no '&').
    // Pretty-permalinks mode → restBase is `…/wp-json/fobo-data-maker/v1` and the
    // endpoint joins with a path separator.
    const ep  = isUpdate ? 'update' : 'submit';
    const url = restBase.indexOf('?rest_route=') >= 0
      ? restBase + '/' + ep
      : restBase.replace(/\/+$/, '') + '/' + ep;
    const body = { slug: slug, values: values };
    if (isUpdate) {
      body.submission_id = editCtx.submissionId;
      body.edit_token    = editCtx.editToken;
    }
    if (extra) {
      if (typeof extra.hp === 'string')       body.hp             = extra.hp;
      if (typeof extra.consent === 'boolean') body.consent        = extra.consent;
      if (typeof extra.captcha === 'string' && extra.captcha) body.captcha_token = extra.captcha;
    }
    return fetch(url, {
      method:  'POST',
      headers: { 'Content-Type': 'application/json' },
      body:    JSON.stringify(body),
    }).then(r => r.text().then(t => {
      let parsed = null;
      try { parsed = t ? JSON.parse(t) : {}; } catch (_) {}
      return { ok: r.ok && parsed !== null, status: r.status, body: parsed || { error: 'Non-JSON response (HTTP ' + r.status + ')' } };
    })).catch(err => ({
      ok: false,
      status: 0,
      body: { error: (err && err.message) ? err.message : 'Network error' },
    }));
  }

  // Map HTTP status (+ optional server message) to a message the submitter
  // can actually act on. Lambda's payload-too-large response already carries
  // a human-readable string, so we pass it through; generic 5xx/4xx codes
  // get rewritten so people don't stare at "HTTP 500".
  function friendlyError(status, serverMsg) {
    const msg = (serverMsg || '').trim();
    if (status === 413) return msg || t('err_too_large',   'This submission is too large to send. Try shrinking large images or removing big attachments, then submit again.');
    if (status === 0)   return                 t('err_network',     'Network error — please check your connection and try submitting again.');
    if (status === 404) return                 t('err_form_gone',   'This form is no longer available. Please contact the form owner.');
    if (status === 401 || status === 403) return t('err_not_accepting', 'This form is not accepting submissions right now.');
    if (status === 502 || status === 503 || status === 504) return t('err_server_unreachable', 'The form server is unreachable right now. Please try again in a moment.');
    if (status >= 500)  return                 t('err_generic_5xx', 'Something went wrong sending your submission. Please try again — if it keeps happening, contact the form owner.');
    return msg || (t('err_generic',            'Submission failed') + ' (HTTP ' + status + ').');
  }

  function onPosted(resp, slug, lsKey, root, editFlow, afterUrl, successMd, mount) {
    if (!resp.ok) {
      // Structured per-field errors take precedence over the generic
      // banner so the submitter can see exactly which inputs the server
      // rejected (length, regex, server-side cross-field rules the
      // client validator can't run). Shape:
      //   { fieldErrors: { "<field name>": "message", ... }, error?: "..." }
      const fe = resp.body && resp.body.fieldErrors;
      const applier = window.DataMakerRenderer && window.DataMakerRenderer.applyFieldErrors;
      if (fe && typeof fe === 'object' && typeof applier === 'function') {
        try { applier(fe); } catch (_) {}
      }
      // Always show the form-level message too — covers cases where the
      // server rejected for a reason that isn't field-specific
      // (rate-limited, captcha failed, etc.) and gives the submitter a
      // single place to land their eyes.
      let panel = mount && mount.querySelector('.dm-submit-error');
      if (!panel) {
        panel = document.createElement('div');
        panel.className   = 'dm-submit-error dm-submit-status';
        panel.setAttribute('role', 'alert');
        panel.style.color = '#c92a2a';
        root.appendChild(panel);
      }
      panel.textContent = friendlyError(resp.status, resp.body && resp.body.error);
      // Turnstile tokens are single-use; refresh the widget so the user
      // can submit again without a manual page reload after a server
      // rejection (validation, rate limit, captcha-failed, etc.).
      if (mount && window.turnstile) {
        const w = mount.querySelector('.cf-turnstile');
        if (w) try { window.turnstile.reset(w); } catch (_) {}
      }
      return;
    }
    // Clear any prior failure panel before painting success.
    const prior = mount && mount.querySelector('.dm-submit-error');
    if (prior) prior.remove();
    if (editFlow && resp.body && resp.body.submissionId && resp.body.editToken) {
      try {
        window.localStorage.setItem(lsKey, JSON.stringify({
          submissionId: resp.body.submissionId,
          editToken:    resp.body.editToken,
          ts:           Date.now(),
          slug:         slug,
        }));
      } catch (_) {}
    }
    if (afterUrl) {
      // Per-form admin chose a redirect target — navigate after a short tick
      // so the success state is visible briefly even on fast networks.
      const status = document.createElement('div');
      status.className = 'dm-submit-status';
      status.textContent = t('submitted_redirecting', 'Submitted. Redirecting…');
      root.appendChild(status);
      setTimeout(function () { window.location.href = afterUrl; }, 250);
      return;
    }
    // Stay-on-page mode: replace the form with the admin-configured
    // Markdown success message. Reuses the renderer's own renderMarkdown
    // helper so styling (heading sizes, list bullets, link color) lines
    // up with rich-text columns inside the form.
    root.innerHTML = '';
    root.classList.add('dm-success');
    const panel = document.createElement('div');
    panel.className = 'dm-richtext dm-success-message';
    const md = (successMd || '## Thanks for your submission.');
    const renderer = window.DataMakerRenderer && window.DataMakerRenderer.renderMarkdown;
    panel.innerHTML = typeof renderer === 'function'
      ? renderer(md)
      : '<p>' + md.replace(/</g, '&lt;') + '</p>';
    root.appendChild(panel);
    // Scroll the success panel into view so the user sees it even on a
    // long form rendered above the fold.
    try { root.scrollIntoView({ behavior: 'smooth', block: 'start' }); } catch (_) {}
  }

  function readLs(key) {
    try {
      const raw = window.localStorage.getItem(key);
      return raw ? JSON.parse(raw) : null;
    } catch (_) { return null; }
  }

  function showResumeBanner(host, stored, onEdit, onFresh) {
    const wrap = document.createElement('div');
    wrap.className = 'dm-resume-banner';
    wrap.style.cssText = 'border:1px solid var(--dm-accent,#04c8ff);background:rgba(4,200,255,0.08);padding:12px;margin-bottom:16px;border-radius:6px;display:flex;gap:8px;align-items:center;flex-wrap:wrap;';
    const msg = document.createElement('span');
    msg.style.flex = '1';
    msg.textContent = t('resume_prompt', 'You started this form earlier on this browser. Continue editing your previous submission?');
    const editBtn = document.createElement('button');
    editBtn.type  = 'button';
    editBtn.textContent = t('continue_editing', 'Continue editing');
    editBtn.className   = 'dm-btn dm-btn-primary';
    const freshBtn = document.createElement('button');
    freshBtn.type  = 'button';
    freshBtn.textContent = t('start_over', 'Start over');
    freshBtn.className   = 'dm-btn dm-btn-subtle';
    editBtn.addEventListener('click',  () => { onEdit();  wrap.remove(); });
    freshBtn.addEventListener('click', () => { onFresh(); wrap.remove(); });
    wrap.appendChild(msg);
    wrap.appendChild(editBtn);
    wrap.appendChild(freshBtn);
    host.parentNode.insertBefore(wrap, host);
  }
})();
