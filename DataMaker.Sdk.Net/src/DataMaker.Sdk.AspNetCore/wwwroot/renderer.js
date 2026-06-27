/**
 * DataMaker form renderer — runs in WebView2 (designer preview) and
 * eventually on a recipient web URL with the same code. Reads the
 * form-bundle JSON injected by WebRenderer, walks the schema, builds the
 * DOM, evaluates compiled VisibleWhen/Calculated expressions on every
 * value change, and applies the results.
 *
 * Property casing: schema is camelCase from System.Text.Json source-gen,
 * except Column carries a custom-converter "Kind" (capitalised). We read
 * both casings to be tolerant of either.
 */
// Report a genuine client-side error. On the hosted product this beacons to the
// Sync Lambda, which logs it to CloudWatch under [client-error]; self-hosted
// embeds (WordPress, ASP.NET) leave errorBeaconUrl unset, so it's a no-op (the
// browser console still carries the error for the site owner). End users are
// shown NOTHING: a form-filler should never see a stack trace, and most window
// 'error' events on mobile come from injected browser/extension scripts we can't
// act on anyway.
function dmReportError(where, info) {
  try {
    const url = (window.DataMakerConfig || {}).errorBeaconUrl;
    if (!url) return;
    const body = JSON.stringify({
      where: where,
      message: String((info && info.message) || '').slice(0, 1000),
      stack: String((info && info.stack) || '').slice(0, 4000),
      source: (info && info.source) || '',
      page: location.href,
      ua: navigator.userAgent,
    });
    if (navigator.sendBeacon) navigator.sendBeacon(url, body);
    else fetch(url, { method: 'POST', body: body, keepalive: true, headers: { 'Content-Type': 'application/json' } });
  } catch (e) { /* error reporting must never throw */ }
}

window.addEventListener('error', e => {
  // Drop WebKit's masked cross-origin error — the bare "Script error." with a
  // null e.error and no location. On Firefox iOS the browser injects its own
  // content scripts; their exceptions surface here masked exactly like this,
  // with nothing we can attribute or fix (Safari iOS, injecting nothing, never
  // raises it). Not ours, not actionable — neither shown nor logged.
  if (!e.error && (!e.filename || e.message === 'Script error.')) return;
  dmReportError('window.onerror', {
    message: e.message,
    stack: e.error && e.error.stack,
    source: (e.filename || '?') + ':' + e.lineno + ':' + e.colno,
  });
});
window.addEventListener('unhandledrejection', e =>
  dmReportError('unhandledrejection', {
    message: String((e.reason && e.reason.message) || e.reason),
    stack: e.reason && e.reason.stack,
  }));

// Wrap an event handler so a throw inside it is reported with its REAL stack
// (the local catch sees the true Error object, unlike the masked window 'error'
// event) and swallowed, so one bad handler can't white-screen the form.
function dmGuard(where, fn) {
  return function () {
    try { return fn.apply(this, arguments); }
    catch (err) { dmReportError(where, { message: err && err.message, stack: err && err.stack }); }
  };
}

// Normalize a button's iconGlyph for DOM text content. Canonical wire format
// is the FA hex codepoint string ("f0c7"); older form data may carry an
// already-rendered unicode char (length 1-2). Hex strings are parsed and
// expanded to the matching unicode character; anything else passes through
// unchanged so author input is never silently dropped.
function resolveIconGlyph(raw) {
  if (raw == null) return '';
  const s = String(raw).trim();
  if (s.length === 0) return '';
  if (/^[0-9a-fA-F]{1,6}$/.test(s)) {
    const cp = parseInt(s, 16);
    if (cp > 0 && cp <= 0x10FFFF) return String.fromCodePoint(cp);
  }
  return s;
}

(function () {
  'use strict';
  // Expose mount() for hosts that bootstrap multiple forms on one page
  // (WP shortcode + Gutenberg block, multi-form landing pages). Single-
  // form hosts (Wasm preview, WebView2) keep the auto-bootstrap path
  // below and don't need to touch this entry point.
  const ns = (window.DataMakerRenderer = window.DataMakerRenderer || {});
  ns.mount = mount;

  // Drain any mount requests the host queued before renderer.js loaded.
  // Script loading order (deferred / dependency-chain) puts the host's
  // bootstrap script ahead of this one in some browsers, so the host
  // pushes intents onto ns._pending and we replay them here.
  if (Array.isArray(ns._pending)) {
    const q = ns._pending; ns._pending = null;
    for (const args of q) {
      try { mount.apply(null, args); }
      catch (e) { dmReportError('mount', { message: e && e.message, stack: e && e.stack }); }
    }
  }

  // Auto-mount on page load when the page exposes the legacy global
  // ids (#form-root + #form-bundle). Multi-form hosts skip those and
  // call ns.mount(root, bundle, hooks) per shortcode instance instead.
  try {
    const r = document.getElementById('form-root');
    const b = document.getElementById('form-bundle');
    if (r && b) mount(r, b);
  } catch (e) { dmReportError('mount', { message: e && e.message, stack: e && e.stack }); }
})();

/**
 * Mount one form instance. Every variable inside this function is per-
 * call scope, so multiple mounts on a single page each get their own
 * `values`, `touched`, `fieldEls` etc. without state bleed between forms.
 *
 * @param {HTMLElement} rootArg    Element the form DOM will be appended into.
 * @param {HTMLElement} bundleArg  <script type="application/json"> carrying the FormBundleBuilder payload.
 * @param {Object}      hooksArg   Per-mount { onSubmit, onSave, onReset, onAction, applyFieldErrors? } callbacks.
 *                                 When omitted the renderer falls back to the legacy global
 *                                 window.DataMakerRenderer.{onSubmit,...} so the preview shell still works.
 */
function mount(rootArg, bundleArg, hooksArg) {
  const bundleEl = bundleArg;
  if (!bundleEl) throw new Error('form-bundle script element missing');

  const bundle    = JSON.parse(bundleEl.textContent);
  const form      = bundle.form;
  const compiled  = bundle.compiled  || {};   // expression key → js source string or null

  // The form author's design cascade lives in the .dmf: per-element resolved
  // CSS (elementCss) + the palette (paletteCss). A host can render the form
  // *structure-only* — inheriting its own site's look instead of the author's
  // — by setting DataMakerRenderer.applyFormStyle = false before mount. The
  // structural layout layer still applies; only the .dmf's visual design is
  // dropped. Exposed through the SDK (DataMakerConfig.applyFormStyle).
  const _applyFormStyle = (hooksArg || window.DataMakerRenderer || {}).applyFormStyle !== false;
  const elementCss = _applyFormStyle ? (bundle.elementCss || {}) : {}; // element key → resolved-style CSS string
  const paletteCss = _applyFormStyle ? (bundle.paletteCss || '') : ''; // :root.light{...}:root.dm-dark{...}
  if (!_applyFormStyle && rootArg && rootArg.classList) rootArg.classList.add('dm-unstyled');

  // Per-mount hooks. Falls back to the legacy global namespace for hosts
  // that didn't supply a per-mount object (designer preview, WebView2).
  // applyFieldErrors / renderMarkdown are populated by this renderer below
  // and live on the global namespace so the bridge can reach them.
  const globalNs = (window.DataMakerRenderer = window.DataMakerRenderer || {});
  const hooks    = hooksArg || globalNs;
  hooks.onSubmit ||= function (_)  { /* no-op preview */ };
  hooks.onSave   ||= function (_)  { /* no-op preview */ };
  hooks.onReset  ||= function (_)  { /* no-op preview */ };
  hooks.onAction ||= function (_)  { /* no-op preview */ };
  // hooks.uploadSlot({ hash, mime, sizeBytes, fileName }) → Promise of
  // { url, key } or null. The host (WP plugin, WebView2 embed, etc.)
  // proxies to POST /submissions/upload-slot on the Lambda. Returning
  // null falls the field back to the legacy inline data-URI path so a
  // host that hasn't wired the storage-v2 endpoint yet still works.
  // See docs/PLAN-STORAGE-V2.md.
  hooks.uploadSlot ||= async function (_) { return null; };
  // Expose the renderer's own Markdown → HTML helper so wp-bridge (and
  // any future host) can render a success message without duplicating
  // the parser. Set lazily so multiple mounts on a page agree on the
  // same impl.
  if (typeof hooks.renderMarkdown !== 'function') {
    hooks.renderMarkdown = function (md) { return renderMarkdown(md); };
  }
  // Always also surface the markdown helper on the global namespace —
  // hosts that hold a per-mount hooks ref still need it for non-mount
  // contexts (success-message panel after `root.innerHTML = ''`).
  if (typeof globalNs.renderMarkdown !== 'function') {
    globalNs.renderMarkdown = hooks.renderMarkdown;
  }

  // Localizable user-visible strings. Hosts populate
  // `window.DataMakerRenderer.i18n` (the WP plugin uses
  // `wp_localize_script`) before this script runs; missing keys fall back
  // to the literal English fallback passed inline, so the renderer never
  // shows an empty button or error message even on a misconfigured host.
  function t(key, fallback) {
    try {
      const dict = window.DataMakerRenderer && window.DataMakerRenderer.i18n;
      if (dict && typeof dict[key] === 'string' && dict[key] !== '') return dict[key];
    } catch (_) {}
    return fallback;
  }

  // Server → client error echo. Host (wp-bridge) calls this with a
  // { fieldName: message } map after a failed POST so submitters see
  // which inputs the server rejected, not just a generic banner.
  // Fields named here are forced into the touched + invalid state and
  // the first one gets keyboard focus.
  hooks.applyFieldErrors = function (errs) {
    if (!errs || typeof errs !== 'object') return;
    let first = null;
    for (const f of (form.fields || [])) {
      const msg = errs[f.name];
      if (typeof msg !== 'string' || msg === '') continue;
      const wrap = fieldEls[f.id];
      if (!wrap) continue;
      touched[f.name] = true;
      wrap.classList.add('dm-invalid');
      const errEl = wrap.querySelector('.dm-err');
      if (errEl) errEl.textContent = msg;
      const inp = fieldInputEls[f.id];
      if (inp) inp.setAttribute('aria-invalid', 'true');
      if (!first) first = wrap;
    }
    validationCtx = 'submit';   // server rejected a submit — keep the boxed banner shown
    const banner = root.querySelector('.dm-form-issues');
    if (banner) banner.hidden = !first;
    if (first) {
      const fid = first.dataset.fieldId;
      const target = (fid && fieldInputEls[fid]) || first.querySelector('input, select, textarea, button');
      const focusTarget = target && target._dmRawInput ? target._dmRawInput : target;
      if (focusTarget && typeof focusTarget.focus === 'function') {
        try { focusTarget.focus(); } catch (_) {}
      }
      try { first.scrollIntoView({ behavior: 'smooth', block: 'center' }); } catch (_) {}
    }
  };

  // Palette + per-button state CSS. Both are pre-resolved server-side; the
  // renderer just appends a single <style> block at bootstrap so the rest of
  // the document picks them up.
  installRuntimeStyles(paletteCss, elementCss);

  // ── Locale / date formatting (must be before form rendering) ──
  // Prefer the browser's locale over the page lang attribute. Embedders
  // (WordPress, etc.) set <html lang="..."> for content language; that's
  // not the same as the user's preferred number / date format. A Dutch
  // user filling out a form on an English-language WP site still wants
  // 13,56 + dd-mm-yyyy. Page lang is kept as a last-resort fallback for
  // headless contexts where navigator is missing.
  const _locale = navigator.language || document.documentElement.lang || undefined;

  // Probe the active locale's decimal + group separators once. Used by
  // every number/decimal/money input's parse-on-input path so a Dutch
  // user typing "13,56" lands as the JS number 13.56 (and serialises to
  // JSON / the Sync Lambda as "13.56" — invariant on the wire), while
  // the display still renders as "13,56" via toLocaleString. A locale
  // with no group separator (e.g. CJK) produces an empty string and the
  // parser falls through to a no-op group strip.
  const _decSep = (() => {
    const s = (1.1).toLocaleString(_locale);
    const m = s.match(/[^\d]/);
    return m ? m[0] : '.';
  })();
  const _grpSep = (() => {
    const s = (1234567).toLocaleString(_locale);
    const m = s.match(/[^\d]/);
    return m ? m[0] : '';
  })();
  const _timePattern = window.__dmTimePattern || null;
  const _dateFormat = (() => {
    const s = new Date(2000, 1, 13).toLocaleDateString(_locale);
    const sep = s.replace(/[\d]/g, '').charAt(0) || '-';
    const parts = s.split(/[^\d]+/);
    const dayIdx  = parts.findIndex(p => +p === 13);
    const yearIdx = parts.findIndex(p => +p === 2000);
    const order = yearIdx === 0 ? 'ymd' : dayIdx === 0 ? 'dmy' : 'mdy';
    return { sep, order };
  })();
  const _dmy = _dateFormat.order === 'dmy';
  const _datePattern = window.__dmDatePattern
    || (_dateFormat.order === 'ymd' ? 'yyyy' + _dateFormat.sep + 'MM' + _dateFormat.sep + 'dd'
      : _dateFormat.order === 'dmy' ? 'dd' + _dateFormat.sep + 'MM' + _dateFormat.sep + 'yyyy'
      : 'MM' + _dateFormat.sep + 'dd' + _dateFormat.sep + 'yyyy');

  // ── State ────────────────────────────────────────────────────
  const fns      = {};   // expression key → compiled JS function (or null = server-only)
  const values   = {};   // field name → current value
  const fieldEls = {};   // field id → outer DOM element
  const fieldInputEls = {};  // field id → its primary <input>/<select>/<textarea>
  const fieldDefs = {};  // field id → FieldDefinition
  // Decorative-column DOM nodes keyed by the same string the bundle uses
  // for compiled VisibleWhen ('groups/{id}/visibleWhen', 'richtext/{id}',
  // …). evaluateAll reads this map to toggle each column's visibility on
  // every value change — without it the JS only walked form.fields and
  // ignored group / richtext / image / divider gating entirely.
  const columnEls = {};
  // Tracks whether the user has interacted with a field (blur or change).
  // Visibility/calculated re-evaluate on every keystroke (live), but
  // validation errors only render for touched fields so the form doesn't
  // open already covered in "Required" chips on every empty field.
  const touched  = {};

  // Multi-step wizard state. Declared here (not by the helpers below) so it's
  // out of the temporal-dead-zone when renderForm runs during initial mount.
  let wizardStepEls    = [];   // one <div class="dm-step"> per step
  let wizardCurrent    = 0;    // index of the visible step
  let wizardBarEl      = null; // the numbered step bar
  let wizardBackBtn    = null;
  let wizardPrimaryBtn = null; // Next on inner steps, Submit on the last
  let wizardNavStatus  = null; // inline "complete the required fields" hint
  // Gates the form-level issues banner: null until the user clicks Next/Submit,
  // then 'step' (Next on an invalid step) or 'submit' (Submit). The SAME boxed
  // banner carries either message. Without this the banner popped on every blur
  // and doubled up with a separate inline step hint.
  let validationCtx    = null;

  // Compile every expression once. eval is acceptable here because the
  // bundle was produced by C# JsCompiler from a signed form schema, not
  // from untrusted browser input.
  for (const key in compiled) {
    const body = compiled[key];
    fns[key] = body == null ? null : (0, eval)('(' + body + ')');
  }

  // ─── Money + format helpers (mirrors DataMaker.Schema.Fields.CurrencySymbols
  //      and NetFormatParser, kept inline so the bundle ships standalone).
  //      Defined here — above the renderForm() call below — because renderControl
  //      reads CURRENCY_SYMBOLS during the initial money-field render, and a
  //      `const` in a TDZ would throw ReferenceError before it's been
  //      initialised. ──────────────────────────────────────────────
  const CURRENCY_SYMBOLS = {
    EUR:'€', USD:'$', GBP:'£', JPY:'¥', CHF:'Fr', CAD:'C$', AUD:'A$', NZD:'NZ$',
    SEK:'kr', NOK:'kr', DKK:'kr', PLN:'zł', CZK:'Kč', HUF:'Ft', CNY:'¥', INR:'₹',
    BRL:'R$', MXN:'$', ZAR:'R', TRY:'₺', KRW:'₩', SGD:'S$', HKD:'HK$',
  };
  function currencySymbolFor(code) {
    if (!code) return '';
    const k = String(code).trim().toUpperCase();
    return CURRENCY_SYMBOLS[k] || k;
  }
  function netFormatFractionDigits(fmt) {
    if (typeof fmt !== 'string' || !fmt.trim()) return null;
    const s = fmt.trim();
    const head = s[0];
    if (head === 'F' || head === 'f' || head === 'N' || head === 'n' || head === 'D' || head === 'd') {
      if (s.length === 1) return (head === 'D' || head === 'd') ? 0 : 2;
      const n = parseInt(s.slice(1), 10);
      return Number.isInteger(n) ? Math.max(0, n) : null;
    }
    const dot = s.indexOf('.');
    if (dot < 0) return 0;
    let count = 0;
    for (let i = dot + 1; i < s.length; i++) {
      if (s[i] === '0' || s[i] === '#') count++;
      else break;
    }
    return count;
  }
  /// Format a calculated-field value for display. Numeric kinds use the
  /// page locale's decimal/thousands separators (so a Dutch user sees
  /// "13,56" instead of "13.56" beneath an input where they typed
  /// "13,56" themselves). Fraction digits come from the field's
  /// money.decimalPlaces / number.decimalPlaces / number.format ("N2"
  /// → 2 fraction digits, grouping on; "F4" → 4 digits, no grouping).
  /// Non-numeric kinds round-trip the value verbatim.
  function formatCalculatedValue(val, f) {
    if (val == null) return '';
    const kind = (f.kind || '').toLowerCase();
    const num  = typeof val === 'number' ? val : parseFloat(val);
    if (!Number.isFinite(num)) return String(val);
    if (kind === 'number' || kind === 'decimal' || kind === 'money') {
      const opts  = f[kind] || {};
      const fmt   = opts.format;
      const fdExplicit = (typeof opts.decimalPlaces === 'number') ? opts.decimalPlaces : null;
      const fdFromFmt  = netFormatFractionDigits(fmt);
      const fd    = fdExplicit != null ? fdExplicit
                  : fdFromFmt  != null ? fdFromFmt
                  : (kind === 'money' ? 2 : (kind === 'decimal' ? 2 : 0));
      const grp   = fmt ? (netFormatGrouped(fmt) === true) : (kind === 'money');
      return num.toLocaleString(_locale, {
        minimumFractionDigits: fd,
        maximumFractionDigits: fd,
        useGrouping: grp,
      });
    }
    return String(val);
  }

  function netFormatGrouped(fmt) {
    if (typeof fmt !== 'string' || !fmt.trim()) return null;
    const s = fmt.trim();
    const head = s[0];
    if (head === 'N' || head === 'n') return true;
    if (head === 'F' || head === 'f' || head === 'D' || head === 'd') return false;
    const dot = s.indexOf('.');
    const integerPart = dot < 0 ? s : s.slice(0, dot);
    return integerPart.indexOf(',') >= 0;
  }

  // Validator regexes — declared at the top of bootstrap so they're
  // initialised before any input-event listener (attached during the
  // first renderForm pass) can fire evaluateAll → validate →
  // intrinsicError. Earlier they sat near intrinsicError itself and a
  // synchronous keystroke could trip the TDZ.
  const EMAIL_RX = /^[^\s@]+@[^\s@]+\.[^\s@]+$/i;
  const PHONE_RX = /^\+?[0-9\s\-().]{6,}$/;

  // ── Bootstrap ────────────────────────────────────────────────
  // mount() callers pass the root element directly; the legacy
  // single-form auto-bootstrap path passes the #form-root lookup result.
  const root = rootArg;
  root.innerHTML = '';   // clear the "Loading…" fallback
  for (const f of (form.fields || [])) fieldDefs[f.id] = f;

  // Mount-scoped id prefix. Used to build stable, predictable element
  // ids so external CSS / tests / userscripts can target rendered DOM
  // without relying on internal class names. Derived from form.id —
  // unique within a single rendered form. Multiple forms on the same
  // page should each have a unique form.id; renderer warns if not.
  const mountId = sanitizeIdToken(form.id || form.name || 'form');
  root.dataset.formId = form.id || '';
  root.dataset.formName = form.name || '';

  renderForm(root, form);

  // Edit-flow hydration. When a host (WP bridge) picks up a stored
  // submission from localStorage (Continue editing? banner), it stashes
  // the saved values + edit context on the global before scripts run.
  // Restore them into the freshly-rendered DOM so the next submit
  // round-trips correctly. Bridge clears __editContext on success; we
  // clear __pendingHydrate here so a re-mount doesn't double-apply.
  const pendingHydrate = hooks.__pendingHydrate;
  if (pendingHydrate && typeof pendingHydrate === 'object') {
    delete hooks.__pendingHydrate;
    for (const f of (form.fields || [])) {
      if (!(f.name in pendingHydrate)) continue;
      const el = fieldInputEls[f.id];
      if (el) setValue(el, f, pendingHydrate[f.name]);
      values[f.name]  = pendingHydrate[f.name];
      touched[f.name] = true;
    }
  }

  // Auto submit row — mirrors the Wasm MainPage submit row + the desktop
  // preview's. No POST endpoint here (this is the WebForm preview); the
  // click marks every field touched, runs validation, and surfaces the
  // same valid/invalid status the other surfaces show. Skipped when the
  // schema declares its own ButtonColumn(s) — those drive submit/save/reset
  // themselves and ownership of the action surface belongs to the author.
  if (!hasSchemaButton(form) && (form.steps || []).length <= 1) {
    const submitRow = document.createElement('div');
    submitRow.className = 'dm-submit-row';
    const submitStatus = document.createElement('span');
    submitStatus.className = 'dm-submit-status';
    const submitBtn = document.createElement('button');
    submitBtn.className = 'dm-submit';
    submitBtn.type = 'button';
    submitBtn.textContent = t('submit', 'Submit');
    submitBtn.addEventListener('click', () => {
      runSchemaAction('submit', null, submitStatus);
    });
    submitRow.appendChild(submitStatus);
    submitRow.appendChild(submitBtn);
    root.appendChild(submitRow);
  }

  // Form-level validation banner — appears directly below the submit row
  // whenever at least one touched field is invalid. evaluateAll toggles
  // its hidden state, so it surfaces on the first blur/change AND on
  // submit-click (which marks every field touched).
  //
  // Placement: insert after the closest containing row of the submit
  // button (auto `.dm-submit` OR a schema ButtonColumn with action Submit
  // / Save). For schema-button forms the button sits inside a layout row;
  // appending to root would put the banner at the very end of the form
  // (well below the action area). Falls back to root append when no
  // submit-style button exists at all.
  const issuesBanner = document.createElement('div');
  issuesBanner.className = 'dm-form-issues';
  issuesBanner.setAttribute('role', 'alert');
  issuesBanner.hidden = true;
  issuesBanner.innerHTML =
    '<span class="dm-form-issues-icon" aria-hidden="true">!</span>' +
    '<span class="dm-form-issues-text">Please fix the highlighted fields before submitting.</span>';
  insertBannerAfterSubmit(root, issuesBanner);

  // Multi-step wizard: append the Back/Next nav and reveal the first step.
  if ((form.steps || []).length > 1) initWizardNav();

  evaluateAll();

  // ─── Tab navigation ─────────────────────────────────────────
  // WKWebView on macOS yields Tab to its parent responder when the OS
  // "Keyboard navigation" toggle is off, so plain Tab inside an input
  // moves focus out of the form to whatever hosts the WebView (designer
  // preview chrome, surrounding page on a real site, etc.). Override it
  // with one keydown listener on the form root: walk the form's own
  // focusables in DOM order and cycle at the ends. Scoped to `#form-root`
  // so we never see chrome elements; selector is the W3C tabbable set
  // restricted to the kinds the form actually emits — every input here
  // is a real native element, so a simple .focus() lands the keyboard.
  root.addEventListener('keydown', (e) => {
    if (e.key !== 'Tab') return;
    if (e.metaKey || e.ctrlKey || e.altKey) return;
    const focusables = collectFormFocusables();
    if (focusables.length === 0) return;
    const idx = focusables.indexOf(document.activeElement);
    const step = e.shiftKey ? -1 : 1;
    const next = idx === -1
      ? focusables[e.shiftKey ? focusables.length - 1 : 0]
      : focusables[(idx + step + focusables.length) % focusables.length];
    e.preventDefault();
    next.focus();
  });

  function collectFormFocusables() {
    const sel = 'input:not([type="hidden"]):not([disabled]), select:not([disabled]), textarea:not([disabled]), button:not([disabled]), [tabindex]:not([tabindex="-1"])';
    return [...root.querySelectorAll(sel)].filter(el =>
      el.offsetParent !== null && getComputedStyle(el).visibility !== 'hidden');
  }

  // ─── Render ──────────────────────────────────────────────────

  /// CSS-id-safe token. Replaces non `[A-Za-z0-9_-]` chars with '-' and
  /// guarantees a leading letter (CSS bare ids prefer one). Used as the
  /// mount prefix for every per-field / per-column DOM id we emit.
  function sanitizeIdToken(s) {
    let t = String(s || '').replace(/[^A-Za-z0-9_-]+/g, '-');
    if (!t || !/^[A-Za-z]/.test(t)) t = 'f-' + t;
    return t;
  }

  /// Stamps `data-col-id` + `data-col-kind` on every layout-column root
  /// element, plus a stable `id="dm-{mount}-col-{colId}"`. Lets external
  /// CSS / tests target any column with predictable selectors:
  ///   [data-col-kind="heading"][data-level="2"] { ... }
  ///   [data-col-kind="button"][data-variant="Primary"] { ... }
  ///   #dm-myform-col-abc123 { ... }
  function stampCol(el, kind, col) {
    if (col && col.id) {
      el.id = 'dm-' + mountId + '-col-' + sanitizeIdToken(col.id);
      el.dataset.colId = col.id;
    }
    el.dataset.colKind = kind;
  }

  function applyElementCss(el, key) {
    const css = elementCss[key];
    if (css) el.setAttribute('style', css);
  }

  // ─── Multi-step wizard ───────────────────────────────────────
  // Each step renders into its own <div class="dm-step">; only the current
  // one is shown. A numbered bar navigates; Back/Next gate forward moves on
  // per-step validation (same rule set as the final submit).

  // Apply the form's StepBar theme (shape, colours, font, show-flags) to a
  // freshly built bar element. Mirrors the Uno step-bar renderer; every value
  // is optional and falls back to the CSS default via the var() fallbacks.
  function applyStepBarStyle(bar, sbStyle) {
    if (!sbStyle) return;
    const shape = (sbStyle.shape || 'Circle').toLowerCase();
    bar.classList.add('dm-step-shape-' + shape);
    if (sbStyle.showConnectors === false) bar.classList.add('dm-step-bar-noconn');
    if (sbStyle.showLabels === false)     bar.classList.add('dm-step-bar-nolabels');
    const setVar = (name, val) => { if (val) bar.style.setProperty(name, val); };
    setVar('--dm-step-active',    sbStyle.activeColor);
    setVar('--dm-step-inactive',  sbStyle.inactiveColor);
    setVar('--dm-step-connector', sbStyle.connectorColor);
    setVar('--dm-step-label',     sbStyle.labelColor);
    setVar('--dm-step-font',      sbStyle.fontFamily);
    if (sbStyle.fontSize != null) bar.style.setProperty('--dm-step-font-size', sbStyle.fontSize + 'px');
    if (sbStyle.margin   != null) bar.style.setProperty('--dm-step-margin', sbStyle.margin + 'px');
  }

  function buildStepBar(steps) {
    const sbStyle = (form.style && form.style.stepBar) || null;
    const bar = document.createElement('div');
    bar.className = 'dm-step-bar';
    applyStepBarStyle(bar, sbStyle);
    steps.forEach((step, i) => {
      if (i > 0) {
        const conn = document.createElement('span');
        conn.className = 'dm-step-conn';
        bar.appendChild(conn);
      }
      const tab = document.createElement('button');
      tab.type = 'button';
      tab.className = 'dm-step-tab';
      const badge = document.createElement('span');
      badge.className = 'dm-step-badge';
      badge.textContent = String(i + 1);
      const label = document.createElement('span');
      label.className = 'dm-step-label';
      label.textContent = (step.title && step.title.trim()) ? step.title : ('Step ' + (i + 1));
      tab.appendChild(badge);
      tab.appendChild(label);
      tab.addEventListener('click', () => goToStep(i));
      bar.appendChild(tab);
    });
    wizardBarEl = bar;
    return bar;
  }

  function initWizardNav() {
    const nav = document.createElement('div');
    nav.className = 'dm-step-nav';

    wizardBackBtn = document.createElement('button');
    wizardBackBtn.type = 'button';
    wizardBackBtn.className = 'dm-step-back dm-btn dm-btn-secondary';
    applyElementCss(wizardBackBtn, 'buttonvariant/Secondary');
    wizardBackBtn.textContent = t('step_back', 'Back');
    wizardBackBtn.addEventListener('click', () => { if (wizardCurrent > 0) { validationCtx = null; showStep(wizardCurrent - 1); evaluateAll(); } });

    wizardNavStatus = document.createElement('span');
    wizardNavStatus.className = 'dm-step-status';

    wizardPrimaryBtn = document.createElement('button');
    wizardPrimaryBtn.type = 'button';
    wizardPrimaryBtn.className = 'dm-step-next dm-btn dm-btn-primary';
    applyElementCss(wizardPrimaryBtn, 'buttonvariant/Primary');
    wizardPrimaryBtn.addEventListener('click', onWizardPrimary);

    nav.appendChild(wizardBackBtn);
    nav.appendChild(wizardNavStatus);
    nav.appendChild(wizardPrimaryBtn);
    root.appendChild(nav);
    showStep(0);
  }

  function onWizardPrimary() {
    if (wizardCurrent >= wizardStepEls.length - 1) {
      // Last step → real submit. Surface the first invalid field on its step
      // first (it may live on a step that isn't currently shown).
      validationCtx = 'submit';
      markStepTouched(-1);
      evaluateAll();
      const firstInvalid = root.querySelector('.dm-field.dm-invalid');
      if (firstInvalid) showStepContaining(firstInvalid);
      runSchemaAction('submit', null, wizardNavStatus);
    } else {
      goToStep(wizardCurrent + 1);
    }
  }

  function goToStep(target) {
    target = Math.max(0, Math.min(target, wizardStepEls.length - 1));
    if (target > wizardCurrent) {
      for (let i = wizardCurrent; i < target; i++)
        if (!validateStep(i)) {
          showStep(i);
          validationCtx = 'step';   // boxed banner carries the step message
          evaluateAll();
          return;
        }
    }
    validationCtx = null;           // clean navigation clears any prior error banner
    showStep(target);
    evaluateAll();
  }

  function showStep(i) {
    wizardCurrent = Math.max(0, Math.min(i, wizardStepEls.length - 1));
    wizardStepEls.forEach((el, idx) => { el.hidden = idx !== wizardCurrent; });
    if (wizardNavStatus) wizardNavStatus.textContent = '';
    updateWizardChrome();
  }

  function updateWizardChrome() {
    if (wizardBarEl) {
      const tabs = wizardBarEl.querySelectorAll('.dm-step-tab');
      tabs.forEach((tab, idx) => {
        tab.classList.toggle('dm-step-current', idx === wizardCurrent);
        tab.classList.toggle('dm-step-done',    idx <  wizardCurrent);
      });
    }
    // De-dup with author-placed nav: if this step already has a button with
    // the matching action, hide the auto one (mirrors the Uno renderer) — the
    // author owns that slot, otherwise both show and overlap.
    const present = stepNavActions(wizardCurrent);
    const last = wizardCurrent >= wizardStepEls.length - 1;
    if (wizardBackBtn) wizardBackBtn.hidden = wizardCurrent === 0 || present.has('prevstep');
    if (wizardPrimaryBtn) {
      wizardPrimaryBtn.hidden = present.has(last ? 'submit' : 'nextstep');
      wizardPrimaryBtn.textContent = last ? t('submit', 'Submit') : t('step_next', 'Next');
    }
    scrollStepIntoView();
  }

  // Keep the active step chip visible in the (horizontally scrollable) bar so a
  // Next/Back move always reveals which step you're on, even when the bar is
  // wider than the viewport. Centres the chip; scrolls only the bar, not the page.
  function scrollStepIntoView() {
    if (!wizardBarEl) return;
    const tab = wizardBarEl.querySelectorAll('.dm-step-tab')[wizardCurrent];
    if (!tab) return;
    const barRect = wizardBarEl.getBoundingClientRect();
    const tabRect = tab.getBoundingClientRect();
    const delta = (tabRect.left - barRect.left) - (wizardBarEl.clientWidth - tabRect.width) / 2;
    const left = Math.max(0, wizardBarEl.scrollLeft + delta);
    try { wizardBarEl.scrollTo({ left: left, behavior: 'smooth' }); }
    catch (_) { wizardBarEl.scrollLeft = left; }
  }

  function markStepTouched(stepIndex) {
    const within = stepIndex < 0 ? null : wizardStepEls[stepIndex];
    for (const f of (form.fields || [])) {
      const wrap = fieldEls[f.id];
      if (!wrap) continue;
      if (within && !within.contains(wrap)) continue;
      const el = fieldInputEls[f.id];
      if (el) values[f.name] = readValue(el, f);
      touched[f.name] = true;
    }
  }

  function validateStep(i) {
    markStepTouched(i);
    evaluateAll();
    const stepEl = wizardStepEls[i];
    return !(stepEl && stepEl.querySelector('.dm-field.dm-invalid'));
  }

  function showStepContaining(el) {
    for (let p = el; p; p = p.parentElement)
      if (p.classList && p.classList.contains('dm-step')) {
        showStep(parseInt(p.dataset.stepIndex, 10) || 0);
        return;
      }
  }

  function renderForm(host, form) {
    // Form-level Style applies to the sheet container itself — the surface the
    // form paints onto (mirrors the Uno renderer's contract).
    applyElementCss(host, 'form/' + form.id);

    // Field-label placement (theme LabelPosition) — a class on the sheet drives
    // the layout in styles.css. Top is the default (no class needed).
    const labelPos = (form.style && form.style.labelPosition) || 'Top';
    if (labelPos === 'Left')     host.classList.add('dm-label-left');
    if (labelPos === 'Floating') host.classList.add('dm-label-floating');

    // form.name/description are metadata only; visible titles live in
    // HeadingColumn / RichTextColumn entries inside the themed cascade.
    const steps = form.steps || [];
    if (steps.length > 1) {
      const sbStyle = (form.style && form.style.stepBar) || null;
      const showBar = !sbStyle || sbStyle.showBar !== false;
      const atBottom = sbStyle && sbStyle.position === 'Bottom';
      // Top bar renders before the steps; bottom bar after them.
      if (showBar && !atBottom) host.appendChild(buildStepBar(steps));
      steps.forEach((step, i) => {
        const stepEl = document.createElement('div');
        stepEl.className = 'dm-step';
        stepEl.dataset.stepIndex = i;
        renderStep(stepEl, step);
        host.appendChild(stepEl);
        wizardStepEls.push(stepEl);
      });
      if (showBar && atBottom) {
        const bar = buildStepBar(steps);
        bar.classList.add('dm-step-bar-bottom');
        host.appendChild(bar);
      }
    } else {
      for (const step of steps) renderStep(host, step);
    }
  }

  function renderStep(host, step) {
    // step.title + step.description are metadata only (step nav, wizard
    // breadcrumbs). Inline titles belong on explicit HeadingColumn entries.
    for (const sec of (step.sections || [])) renderSection(host, sec);
  }

  function renderSection(host, sec) {
    // Sections render their content directly into the form host (matching
    // the Uno renderer), but if they carry their own Style we wrap into a
    // <section> so own-level container styles (background, border, padding)
    // can apply.
    const sectionCss = elementCss['section/' + sec.id];
    const target = sectionCss ? (() => {
      const el = document.createElement('section');
      el.className = 'dm-sec';
      el.id = 'dm-' + mountId + '-sec-' + sanitizeIdToken(sec.id || '');
      if (sec.id) el.dataset.sectionId = sec.id;
      el.setAttribute('style', sectionCss);
      host.appendChild(el);
      return el;
    })() : host;

    // section.title + section.description are designer-side metadata only;
    // visible section headings live in HeadingColumn entries inside the
    // section's rows so the publisher controls level, font, and placement
    // explicitly. Auto-rendering them here used to double-up the heading.
    for (const row of (sec.rows || [])) renderRow(target, row);
  }

  function renderRow(host, row) {
    const cols = row.columnsPerRow || 12;
    const rowEl = document.createElement('div');
    rowEl.className = 'dm-row';
    rowEl.style.setProperty('--cols', cols);
    for (const col of (row.columns || [])) renderColumn(rowEl, col, cols);
    // Skip empty rows — admin-hidden + relation-skip can leave the
    // columns array empty here. Without this guard the renderer emits
    // <div class="dm-row"></div> which shows up as a gap in the grid.
    if (rowEl.children.length === 0) return;
    host.appendChild(rowEl);
  }

  function renderColumn(rowEl, col, totalCols) {
    const kind = (col.kind || col.Kind || '').toLowerCase();

    // Skip relation field columns entirely — no record-lookup pipeline
    // in the web renderer yet, so we drop the column rather than show a
    // dead placeholder. CSS Grid auto-flow then shifts subsequent
    // columns left to fill the slot (each col only declares
    // `grid-column: span N`, so removing a cell rebalances the row).
    if (kind === 'field' && col.fieldId
        && fieldDefs[col.fieldId]
        && (fieldDefs[col.fieldId].kind || '').toLowerCase() === 'relation') {
      return;
    }

    const colEl = document.createElement('div');
    colEl.className = 'dm-col';
    colEl.style.gridColumn = 'span ' + Math.min(col.span || totalCols, totalCols);

    // Per-column responsive stacking — honor Column.StackBelowPx (schema
    // default 640). At/below that viewport width the column goes full-row
    // (via the .dm-stack class); above it keeps its span. Each column tracks
    // its own breakpoint, so a wide column can stack earlier than a narrow
    // sibling. Viewport-based (matches the old global rule's granularity);
    // container queries would be the next refinement.
    const stackPx = (typeof col.stackBelowPx === 'number' && col.stackBelowPx > 0) ? col.stackBelowPx : 640;
    if (typeof window.matchMedia === 'function') {
      const mq = window.matchMedia('(max-width: ' + stackPx + 'px)');
      const applyStack = () => colEl.classList.toggle('dm-stack', mq.matches);
      applyStack();
      if (mq.addEventListener) mq.addEventListener('change', applyStack);
      else if (mq.addListener) mq.addListener(applyStack);
    }

    if (kind === 'field' && col.fieldId && fieldDefs[col.fieldId]) {
      renderField(colEl, fieldDefs[col.fieldId]);
    } else if (kind === 'group') {
      renderGroup(colEl, col);
    } else if (kind === 'richtext') {
      const div = document.createElement('div');
      div.className = 'dm-richtext';
      div.innerHTML = renderMarkdown(col.markdown || '');
      stampCol(div, 'richtext', col);
      applyElementCss(div, 'richtext/' + col.id);
      colEl.appendChild(div);
      // Register so evaluateAll can toggle VisibleWhen.
      if (col.id) columnEls['richtext/' + col.id] = div;
    } else if (kind === 'image') {
      const img = document.createElement('img');
      img.className = 'dm-layout-img';
      img.src = col.source || '';
      img.alt = col.altText || '';
      stampCol(img, 'image', col);
      // applyElementCss replaces the whole style attribute (setAttribute), so
      // it MUST run BEFORE the size/placement styles below — otherwise it wipes
      // the inline maxHeight/maxWidth/margins. Setting them after appends to the
      // element's existing style (e.g. a border-radius from the image's Style).
      applyElementCss(img, 'image/' + col.id);
      if (col.maxHeight) img.style.maxHeight = col.maxHeight + 'px';
      // Cap at maxWidth OR the column width, whichever is smaller — the inline
      // value overrides the stylesheet's `max-width:100%`, so without min() the
      // image keeps its fixed px width and overflows its column (drifting right)
      // once the viewport shrinks the column below maxWidth.
      if (col.maxWidth) img.style.maxWidth = 'min(' + col.maxWidth + 'px, 100%)';
      // Placement within the column (block image, max-width: 100% by default).
      // Fill keeps the default left flow; Left/Center/Right use auto margins
      // once a max-width caps the image narrower than the column.
      const imgAlign = col.align || 'Fill';
      if (imgAlign === 'Center')     { img.style.marginLeft = 'auto'; img.style.marginRight = 'auto'; }
      else if (imgAlign === 'Right') { img.style.marginLeft = 'auto'; img.style.marginRight = '0'; }
      else if (imgAlign === 'Left')  { img.style.marginLeft = '0';    img.style.marginRight = 'auto'; }
      colEl.appendChild(img);
      if (col.id) columnEls['image/' + col.id] = img;
    } else if (kind === 'divider') {
      // <hr> with thickness + optional explicit colour. Default colour
      // resolves from the form's --field-border CSS variable (set in the
      // sheet's cascade), so dividers blend with the rest of the form
      // chrome unless the publisher overrides.
      const hr = document.createElement('hr');
      hr.className = 'dm-divider';
      hr.style.borderTopWidth = (col.thickness || 1) + 'px';
      if (col.color) hr.style.borderTopColor = col.color;
      stampCol(hr, 'divider', col);
      applyElementCss(hr, 'divider/' + col.id);
      colEl.appendChild(hr);
      if (col.id) columnEls['divider/' + col.id] = hr;
    } else if (kind === 'spacer') {
      const sp = document.createElement('div');
      sp.className = 'dm-spacer';
      if (col.height) sp.style.minHeight = col.height + 'px';
      stampCol(sp, 'spacer', col);
      colEl.appendChild(sp);
      if (col.id) columnEls['spacer/' + col.id] = sp;
    } else if (kind === 'heading') {
      const level = Math.min(4, Math.max(1, col.level | 0 || 1));
      const h = document.createElement('h' + level);
      h.className = 'dm-heading dm-heading-' + level;
      // Heading color default is owned by styles.css's `.dm-heading-N { color: var(--dm-headingN, var(--dm-ink)); }`
      // rule — setting `h.style.color` here is destroyed a couple of
      // lines later when applyElementCss replaces the whole `style`
      // attribute with the bundle's emitted heading CSS. Letting CSS own
      // the default keeps both the elementCss override AND the fallback
      // intact: explicit FormStyle.HeadingNStyle.TextColor wins via the
      // inline style; null TextColor leaves the CSS rule to cascade
      // through the palette HeadingNColor / InkColor chain.
      h.textContent = col.text || '';
      stampCol(h, 'heading', col);
      h.dataset.level = String(level);
      applyElementCss(h, 'heading/' + col.id);
      colEl.appendChild(h);
      if (col.id) columnEls['heading/' + col.id] = h;
    } else if (kind === 'button') {
      const variant = (col.variant || 'Primary').toLowerCase();
      const btn = document.createElement('button');
      btn.type = 'button';
      btn.className = 'dm-btn dm-btn-' + variant;
      stampCol(btn, 'button', col);
      btn.dataset.variant = (col.variant || 'Primary');
      btn.dataset.action  = (col.action  || 'None');
      btn.dataset.name    = (col.name    || '');

      const iconPos        = (col.iconPosition        || 'Left').toLowerCase();
      const inlineImagePos = (col.inlineImagePosition || 'Left').toLowerCase();

      const labelSpan = document.createElement('span');
      labelSpan.className = 'dm-btn-label';
      labelSpan.textContent = col.label || '';

      const parts = [];
      if (col.iconGlyph) {
        const i = document.createElement('i');
        i.className = 'fa-solid dm-btn-glyph';
        i.textContent = resolveIconGlyph(col.iconGlyph);
        parts.push({ side: iconPos, el: i });
      }
      if (col.inlineImageSrc) {
        const img = document.createElement('img');
        img.className = 'dm-btn-img';
        img.src = col.inlineImageSrc;
        img.alt = '';
        parts.push({ side: inlineImagePos, el: img });
      }
      for (const p of parts.filter(p => p.side === 'left'))  btn.appendChild(p.el);
      btn.appendChild(labelSpan);
      for (const p of parts.filter(p => p.side === 'right')) btn.appendChild(p.el);

      applyElementCss(btn, 'button/' + col.id);

      btn.addEventListener('click', () => {
        const action = (col.action || 'None').toLowerCase();
        runSchemaAction(action, col, btn.querySelector('.dm-btn-status'));
      });

      colEl.appendChild(btn);
      if (col.id) columnEls['button/' + col.id] = btn;
    }

    rowEl.appendChild(colEl);
  }

  function renderGroup(host, col) {
    const groupEl = document.createElement('fieldset');
    groupEl.className = 'dm-group';
    stampCol(groupEl, 'group', col);
    applyElementCss(groupEl, 'group/' + col.id);

    // Inner container holds the rows so collapse can hide just the body
    // while keeping the legend (chevron + title) clickable.
    const body = document.createElement('div');
    body.className = 'dm-group-body';

    if (col.title || col.isCollapsible) {
      const legend = document.createElement('legend');
      legend.className = 'dm-group-title';
      if (col.isCollapsible) {
        legend.classList.add('dm-collapsible');
        const chev = document.createElement('span');
        chev.className = 'dm-chevron';
        // Match Uno's chevron glyphs: ▼ (expanded) / ▶ (collapsed).
        chev.textContent = '▼';
        legend.appendChild(chev);
        legend.appendChild(document.createTextNode(' '));
      }
      if (col.title) legend.appendChild(document.createTextNode(col.title));
      groupEl.appendChild(legend);

      if (col.isCollapsible) {
        const setCollapsed = (collapsed) => {
          body.hidden = collapsed;
          legend.querySelector('.dm-chevron').textContent = collapsed ? '▶' : '▼';
        };
        setCollapsed(!!col.defaultCollapsed);
        legend.addEventListener('click', () => setCollapsed(!body.hidden));
      }
    }

    for (const row of (col.rows || [])) renderRow(body, row);
    groupEl.appendChild(body);
    host.appendChild(groupEl);
    if (col.id) columnEls['group/' + col.id] = groupEl;
  }

  // Storage v2: hash + upload-to-slot helper used by image + attachment
  // field pickers. Returns the URL-shape value on success, or null when
  // the host hasn't wired hooks.uploadSlot (preview / legacy bridge).
  // Caller falls back to inline data URI on null so older hosts keep
  // working. See docs/PLAN-STORAGE-V2.md §#18a phase 3.
  async function uploadFileToSlot(f0) {
    const buf  = await f0.arrayBuffer();
    const bytes = new Uint8Array(buf);
    // SHA-256 hex of the bytes. crypto.subtle is universally available
    // on https + localhost; on plain http the slot request fails and we
    // fall back to inline.
    let hash;
    try {
      const digest = await crypto.subtle.digest('SHA-256', bytes);
      hash = Array.from(new Uint8Array(digest))
        .map(b => b.toString(16).padStart(2, '0')).join('');
    } catch (_) {
      return null;
    }
    const slot = await hooks.uploadSlot({
      hash, mime: f0.type || null, sizeBytes: f0.size, fileName: f0.name,
    });
    if (!slot || !slot.url) return null;

    const resp = await fetch(slot.url, {
      method: 'PUT',
      body: bytes,
      headers: f0.type ? { 'Content-Type': f0.type } : undefined,
    });
    if (!resp.ok) return null;

    // Store the canonical URL (without the pre-signed query string) so
    // the eventual submission references the underlying object, not the
    // 5-minute write capability. The receiver mints a fresh pre-signed
    // GET when displaying.
    const canonicalUrl = slot.url.split('?')[0];
    return {
      url:       canonicalUrl,
      hash,
      owned:     true,
      fileName:  f0.name,
      mime:      f0.type || null,
      sizeBytes: f0.size,
    };
  }

  function renderField(host, f) {
    // Stable, mount-scoped ids for external CSS / tests / userscripts.
    // - Wrap (the <label>) gets `dm-{mount}-{name}` so authors can target
    //   `#dm-myform-email` directly.
    // - Label span gets `…-label` so composite controls (multi-checkbox,
    //   chips, geo, image, attachment) can `aria-labelledby` it.
    // - Native single-input controls get `…-input` + a real `name` attr
    //   so vanilla HTML form posting / autofill / 1Password recognise them.
    const wrapId  = 'dm-' + mountId + '-' + sanitizeIdToken(f.name || f.id);
    const labelId = wrapId + '-label';
    const inputId = wrapId + '-input';

    // Multi-control fields (radio group, checkbox group, scale, signature, list)
    // must NOT be a <label>: per HTML a label wrapping several controls binds to
    // the FIRST one, so hovering/clicking anywhere in the field fired :hover and
    // selection on the first radio. Single-input fields stay a <label> so clicking
    // the title focuses the input.
    const multiControl =
      (f.kind === 'choice' && f.choice && f.choice.display === 'Radios') ||
      f.kind === 'multi-choice' || f.kind === 'scale' ||
      f.kind === 'signature'   || f.kind === 'list';
    const wrap = document.createElement(multiControl ? 'div' : 'label');
    wrap.className = 'dm-field';
    wrap.id = wrapId;
    wrap.dataset.fieldId   = f.id;
    wrap.dataset.fieldName = f.name || '';
    wrap.dataset.fieldKind = (f.kind || '').toLowerCase();
    if (f.required) wrap.dataset.required = '1';
    fieldEls[f.id] = wrap;

    const labelRow = document.createElement('span');
    labelRow.className = 'dm-label';
    labelRow.id = labelId;
    labelRow.textContent = f.label || f.name || f.id;
    if (f.required) {
      // Marker glyph is theme-configurable (RequiredMarkerGlyph); default "*",
      // empty string hides it entirely.
      const glyph = (form.style && form.style.requiredMarkerGlyph != null)
        ? form.style.requiredMarkerGlyph : '*';
      if (glyph !== '') {
        const req = document.createElement('span');
        req.className = 'dm-req';
        req.setAttribute('aria-hidden', 'true');
        req.textContent = ' ' + glyph;
        labelRow.appendChild(req);
      }
    }
    wrap.appendChild(labelRow);
    // Per-field label styling (form-wide LabelStyle merged with the field's
    // LabelStyle), emitted by FormBundleBuilder under "label/<id>". Independent
    // of the field/value styling on the wrap.
    applyElementCss(labelRow, 'label/' + f.id);

    const input = renderControl(f, wrap);
    const errId = wrapId + '-err';

    if (input) {
      fieldInputEls[f.id] = input;

      // Native single-input kinds get a real id + name; composite wraps
      // (rich-text editor, multi-choice, chips, geo, image/attach pickers)
      // get aria-labelledby pointing at the field label so screen readers
      // and auto-targeting CSS still find them via the label.
      const tag = input.tagName;
      if (tag === 'INPUT' || tag === 'SELECT' || tag === 'TEXTAREA') {
        if (!input.id)   input.id   = inputId;
        if (!input.name) input.name = f.name || '';
        if (f.required) input.setAttribute('aria-required', 'true');
        input.setAttribute('aria-describedby', errId);
        // HTML autocomplete token (WHATWG spec values: 'email', 'tel',
        // 'given-name', 'family-name', 'street-address', 'postal-code',
        // 'bday', 'cc-number', 'off', etc.). Authored on the field in
        // the designer; predefined-field templates ship with sensible
        // defaults. Unset = leave to browser heuristics on the `name`
        // attribute. Composite controls (date-time, money, geo, image)
        // don't use this — they render their own wrappers, not <input>.
        if (typeof f.autocomplete === 'string' && f.autocomplete.length > 0) {
          input.setAttribute('autocomplete', f.autocomplete);
        }
      } else {
        input.setAttribute('aria-labelledby', labelId);
        input.setAttribute('aria-describedby', errId);
        if (f.required) input.setAttribute('aria-required', 'true');
      }

      // 'input' fires per keystroke — used for live visibility/calculated
      // updates. 'change' / 'blur' mark the field as touched, which is
      // what gates validation-error display.
      input.addEventListener('input',  dmGuard('input/' + f.kind,  () => onValueChanged(f, readValue(input, f), false)));
      input.addEventListener('change', dmGuard('change/' + f.kind, () => onValueChanged(f, readValue(input, f), true)));
      input.addEventListener('blur',   dmGuard('blur/' + f.kind,   () => { touched[f.name] = true; evaluateAll(); }), true);
      // Per-field own-level container styles (background, border, padding)
      applyElementCss(wrap, 'field/' + f.id);
      wrap.appendChild(input);
      // Seed initial value into the values bag.
      values[f.name] = readValue(input, f);
    }

    const err = document.createElement('span');
    err.className = 'dm-err';
    err.id = errId;
    err.setAttribute('role', 'status');
    err.setAttribute('aria-live', 'polite');
    wrap.appendChild(err);

    host.appendChild(wrap);
  }

  function renderControl(f, fieldWrap) {
    const ph = f.placeholder || '';
    // Default-value seeding lives inside each case so the value is set as
    // part of construction (not bolted on after) — this matters for chip-
    // style controls where the seed reuses the same chip-creation closure.
    switch ((f.kind || '').toLowerCase()) {
      case 'text':
      case 'email':
      case 'phone':
      case 'url': {
        const i = document.createElement('input');
        i.type = ({ email: 'email', phone: 'tel', url: 'url' })[f.kind] || 'text';
        if (ph) i.placeholder = ph;
        if (f.defaultValue != null) i.value = String(f.defaultValue);
        return i;
      }
      case 'long-text': {
        const t = document.createElement('textarea');
        t.rows = 4;
        if (ph) t.placeholder = ph;
        if (f.defaultValue != null) t.value = String(f.defaultValue);
        return t;
      }
      case 'rich-text': {
        // Markdown editor: textarea source by default + a "Preview" toggle
        // that swaps in a renderMarkdown'd <div>. Mirrors the Uno
        // RichTextFieldEditor's two-mode UX. The wrapper exposes `.value`
        // (gets / sets the textarea text) so the form's existing readValue
        // path picks it up unchanged.
        const wrap = document.createElement('div');
        wrap.className = 'dm-rich-text-editor';

        const ta = document.createElement('textarea');
        ta.className = 'dm-rich-text-source';
        ta.rows = 6;
        ta.placeholder = ph || 'Markdown — # heading, **bold**, *italic*, - list, `code`';
        if (f.defaultValue != null) ta.value = String(f.defaultValue);

        const preview = document.createElement('div');
        preview.className = 'dm-rich-text-preview';
        preview.hidden = true;

        const toggle = document.createElement('button');
        toggle.type = 'button';
        toggle.className = 'dm-rich-text-toggle';
        toggle.textContent = t('preview', 'Preview');
        toggle.tabIndex = 0;
        toggle.addEventListener('click', e => {
          e.preventDefault();
          if (preview.hidden) {
            preview.innerHTML = renderMarkdown(ta.value);
            preview.hidden = false;
            ta.hidden = true;
            toggle.textContent = t('edit', 'Edit');
          } else {
            preview.hidden = true;
            ta.hidden = false;
            toggle.textContent = t('preview', 'Preview');
          }
        });

        wrap.append(toggle, ta, preview);

        // Expose .value so the existing readValue / change-event flow
        // works without special-casing rich-text. Re-fire the textarea's
        // input/change events on the wrap so the touched/calculated
        // pipeline ticks per keystroke.
        Object.defineProperty(wrap, 'value', {
          get: () => ta.value,
          set: v => { ta.value = v ?? ''; },
        });
        ta.addEventListener('input',  () => wrap.dispatchEvent(new Event('input',  { bubbles: false })));
        ta.addEventListener('change', () => wrap.dispatchEvent(new Event('change', { bubbles: false })));
        return wrap;
      }
      case 'number':
      case 'decimal':
      case 'money': {
        const i = document.createElement('input');
        // Format-on-blur applies whenever we have a defined fraction-digit
        // count to enforce: explicit NumberOptions.Format ("N2", "0.00", …),
        // explicit NumberOptions.DecimalPlaces, OR — for money — the
        // MoneyOptions.DecimalPlaces (defaults to 2). It needs a text input
        // so we can display grouped digits (1.234,50) without the browser's
        // type=number stripping non-numeric chars on display.
        const fmtStr = f.number && typeof f.number.format === 'string' && f.number.format.length > 0
          ? f.number.format : null;
        const numFd  = fmtStr ? netFormatFractionDigits(fmtStr)
                              : (f.number && typeof f.number.decimalPlaces === 'number' ? f.number.decimalPlaces : null);
        const moneyFd = f.kind === 'money' && f.money && typeof f.money.decimalPlaces === 'number'
                        ? f.money.decimalPlaces : (f.kind === 'money' ? 2 : null);
        const fd = moneyFd ?? numFd;
        const grp = fmtStr ? (netFormatGrouped(fmtStr) === true) : (f.kind === 'money');
        const useFmtOnBlur = fd !== null && fd !== undefined;

        if (useFmtOnBlur) {
          i.type = 'text';
          i.inputMode = 'decimal';
        } else {
          i.type = 'number';
          if (f.kind !== 'number') i.step = 'any';
        }
        // NumberOptions Min/Max — schema may set decimal bounds. Browser
        // attribute validation only applies to type=number, so we only set
        // them in that case; the intrinsic JS validator handles bounds for
        // the format-on-blur (text-input) path.
        if (f.number && !useFmtOnBlur) {
          if (f.number.min != null) i.min = String(f.number.min);
          if (f.number.max != null) i.max = String(f.number.max);
          if (typeof f.number.decimalPlaces === 'number' && f.number.decimalPlaces >= 0)
            i.step = f.number.decimalPlaces === 0 ? '1' : String(Math.pow(10, -f.number.decimalPlaces));
        }
        if (ph) i.placeholder = ph;
        if (f.defaultValue != null) i.value = String(f.defaultValue);

        if (useFmtOnBlur) {
          const fmtNum = raw => {
            const n = parseFloat(raw);
            if (!Number.isFinite(n)) return '';
            const opts = { useGrouping: grp };
            if (typeof fd === 'number') {
              opts.minimumFractionDigits = fd;
              opts.maximumFractionDigits = fd;
            }
            return n.toLocaleString(undefined, opts);
          };
          // Locale-aware loose parse. Strip whitespace (incl. NBSP via /\s/),
          // strip the locale's group separator, then convert the locale's
          // decimal separator to '.' so parseFloat reads it as a Number.
          // Result is invariant on the wire — JSON emits the JS number
          // verbatim — and the display is re-formatted on blur via fmtNum.
          const parseLoose = s => {
            if (typeof s !== 'string') return '';
            let cleaned = s.replace(/\s/g, '');
            if (_grpSep) cleaned = cleaned.split(_grpSep).join('');
            if (_decSep && _decSep !== '.') cleaned = cleaned.split(_decSep).join('.');
            const n = parseFloat(cleaned);
            return Number.isFinite(n) ? String(n) : '';
          };
          // Seed: parse the raw default, store canonical raw, display formatted.
          i.dataset.raw = parseLoose(i.value);
          i.value = fmtNum(i.dataset.raw);
          // While editing the user sees the raw number; on blur we re-format.
          i.addEventListener('focus', () => { i.value = i.dataset.raw || ''; });
          i.addEventListener('input', () => { i.dataset.raw = parseLoose(i.value); });
          i.addEventListener('blur',  () => {
            i.dataset.raw = parseLoose(i.value);
            i.value = fmtNum(i.dataset.raw);
          });
        }

        // Money: wrap the bare input in a flex row with a currency-symbol
        // prefix on the left (€ 1.234, not 1.234 €). The wrapper carries
        // a `_dmRawInput` pointer so readValue and the calculated-update
        // path can find the inner input without reaching through
        // `.querySelector` everywhere.
        if (f.kind === 'money') {
          const m = document.createElement('div');
          m.className = 'dm-money';
          const pfx = document.createElement('span');
          pfx.className = 'dm-money-prefix';
          pfx.textContent = currencySymbolFor((f.money && f.money.currency) || 'EUR');
          m.appendChild(pfx);
          m.appendChild(i);
          m._dmRawInput = i;
          return m;
        }
        return i;
      }
      case 'date':     return buildDateTimeField(f, false);
      case 'datetime': return buildDateTimeField(f, true);
      case 'boolean': {
        const i = document.createElement('input');
        i.type = 'checkbox';
        if (typeof f.defaultValue === 'boolean') i.checked = f.defaultValue;
        i.addEventListener('keydown', e => {
          if (e.key === 'Enter' || e.key === ' ') {
            e.preventDefault();
            i.checked = !i.checked;
            i.dispatchEvent(new Event('change', { bubbles: true }));
          }
        });
        return i;
      }
      case 'scale': {
        // Single-pick rating / Likert / NPS — a row of figures (Circle / Square
        // / Rounded / Diamond / Star, matching the wizard step bar). Stores the
        // chosen integer point; cumulative fills up to it (star-rating feel).
        const sc   = f.scale || {};
        const min  = (typeof sc.min === 'number') ? sc.min : 1;
        let   max  = (typeof sc.max === 'number') ? sc.max : 5;
        if (max <= min) max = min + 1;
        const shape = String(sc.shape || 'Circle').toLowerCase();
        const cumulative = !!sc.cumulative;
        const align = String(sc.alignment || 'Left').toLowerCase();

        const wrap = document.createElement('div');
        wrap.className = 'dm-scale dm-scale-' + shape;
        if (sc.highlightColor)  wrap.style.setProperty('--dm-scale-hl', sc.highlightColor);
        if (sc.unselectedColor) wrap.style.setProperty('--dm-scale-un', sc.unselectedColor);
        if (sc.labelColor && fieldWrap) {
          const _lbl = fieldWrap.querySelector('.dm-label');
          if (_lbl) _lbl.style.color = sc.labelColor;
        }
        let selected = (typeof f.defaultValue === 'number') ? f.defaultValue : null;

        const rowEl = document.createElement('div');
        rowEl.className = 'dm-scale-row';
        // Spacing is a flexible spacer between figures (capped at the configured
        // value): as the row narrows the spacers shrink FIRST (figures keep full
        // size), then the figures shrink to a floor, then the row scrolls.
        const scaleGap = (typeof sc.spacing === 'number' ? sc.spacing : 6);
        rowEl.style.justifyContent = align === 'center' ? 'center' : (align === 'right' ? 'flex-end' : 'flex-start');
        const pts = [];
        for (let v = min; v <= max; v++) {
          if (v > min) {
            const sp = document.createElement('span');
            sp.className = 'dm-scale-spacer';
            sp.style.maxWidth = scaleGap + 'px';
            rowEl.appendChild(sp);
          }
          const b = document.createElement('button');
          b.type = 'button';
          b.className = 'dm-scale-point';
          b.dataset.value = v;
          b.setAttribute('aria-label', String(v));
          const fig = document.createElement('span');
          fig.className = 'dm-scale-fig';
          b.appendChild(fig);
          if (shape !== 'star') {
            const num = document.createElement('span');
            num.className = 'dm-scale-num';
            num.textContent = String(v);
            b.appendChild(num);   // upright number on top of the (maybe-rotated) figure
          }
          b.addEventListener('click', () => {
            selected = (selected === v) ? null : v;   // re-tap to clear
            paint();
            wrap.dispatchEvent(new Event('change', { bubbles: false }));
          });
          pts.push(b);
          rowEl.appendChild(b);
        }
        wrap.appendChild(rowEl);

        if (sc.minLabel || sc.maxLabel) {
          const labels = document.createElement('div');
          labels.className = 'dm-scale-labels';
          const a = document.createElement('span'); a.className = 'dm-scale-min'; a.textContent = sc.minLabel || '';
          const z = document.createElement('span'); z.className = 'dm-scale-max'; z.textContent = sc.maxLabel || '';
          if (sc.minLabelColor) a.style.color = sc.minLabelColor;
          if (sc.maxLabelColor) z.style.color = sc.maxLabelColor;
          labels.appendChild(a); labels.appendChild(z);
          wrap.appendChild(labels);
        }

        function paint() {
          for (const b of pts) {
            const v = Number(b.dataset.value);
            const on = selected != null && (cumulative ? v <= selected : v === selected);
            b.classList.toggle('dm-scale-on', on);
          }
        }
        paint();

        Object.defineProperty(wrap, 'value', {
          get: () => selected == null ? '' : String(selected),
          set: x => { selected = (x === '' || x == null) ? null : Number(x); paint(); },
        });
        return wrap;
      }
      case 'choice': {
        const choices = f.choice?.choices || [];
        // ChoiceOptions.AllowCustom — the schema permits a value outside the
        // listed options. A fixed <select> can't express that, so render an
        // editable input backed by a <datalist> of the options: the user can
        // pick a suggestion or type any custom value. The wrapper exposes
        // `.value` (and re-dispatches input/change) so the value pipeline +
        // readValue treat it exactly like the <select> path.
        if (f.choice?.allowCustom) {
          const wrap = document.createElement('span');
          wrap.className = 'dm-choice-custom';
          const i = document.createElement('input');
          i.type = 'text';
          if (ph) i.placeholder = ph;
          const dl = document.createElement('datalist');
          dl.id = 'dm-dl-' + f.id;
          for (const opt of choices) {
            const o = document.createElement('option');
            o.value = opt.value;
            if (opt.label && opt.label !== opt.value) o.label = opt.label;
            dl.appendChild(o);
          }
          i.setAttribute('list', dl.id);
          if (f.defaultValue != null) i.value = String(f.defaultValue);
          wrap.appendChild(i);
          wrap.appendChild(dl);
          Object.defineProperty(wrap, 'value', {
            get: () => i.value,
            set: v => { i.value = v ?? ''; },
          });
          i.addEventListener('input',  () => wrap.dispatchEvent(new Event('input',  { bubbles: false })));
          i.addEventListener('change', () => wrap.dispatchEvent(new Event('change', { bubbles: false })));
          return wrap;
        }
        // Radios mode: an inline radio list (column-major via --dm-choice-cols)
        // instead of the dropdown. Exposes `.value` so readValue/setValue treat
        // it exactly like the <select>.
        if (f.choice?.display === 'Radios') {
          const wrap = document.createElement('div');
          wrap.className = 'dm-radios';
          const cols = f.choice.columns;
          if (typeof cols === 'number' && cols > 1) wrap.style.setProperty('--dm-choice-cols', cols);
          // Indicator size + selected colour drive the custom radio ring/dot
          // (styles.css), matching the Uno renderer's Choice.OptionSize/OptionColor.
          if (typeof f.choice.optionSize === 'number') wrap.style.setProperty('--dm-option-size', f.choice.optionSize + 'px');
          if (f.choice.optionColor) wrap.style.setProperty('--dm-option-color', f.choice.optionColor);
          const name = 'dm-radio-' + sanitizeIdToken(f.name || f.id);
          for (const opt of choices) {
            const lab = document.createElement('label');
            lab.className = 'dm-opt';
            const rb = document.createElement('input');
            rb.type = 'radio';
            rb.name = name;
            rb.value = opt.value;
            if (f.defaultValue != null && String(f.defaultValue) === opt.value) rb.checked = true;
            lab.appendChild(rb);
            if (opt.color) {
              const dot = document.createElement('span');
              dot.className = 'dm-choice-dot';
              dot.style.background = opt.color;
              lab.appendChild(dot);
            }
            if (opt.icon) {
              const ic = document.createElement('span');
              ic.className = 'dm-choice-icon';
              ic.textContent = opt.icon;
              lab.appendChild(ic);
            }
            lab.appendChild(document.createTextNode(' ' + opt.label));
            wrap.appendChild(lab);
          }
          Object.defineProperty(wrap, 'value', {
            get: () => { const c = wrap.querySelector('input[type="radio"]:checked'); return c ? c.value : ''; },
            set: v => { wrap.querySelectorAll('input[type="radio"]').forEach(r => { r.checked = (String(v) === r.value); }); },
          });
          return wrap;
        }
        const s = document.createElement('select');
        const blank = document.createElement('option');
        blank.value = '';
        blank.textContent = '—';
        s.appendChild(blank);
        for (const opt of choices) {
          const o = document.createElement('option');
          o.value = opt.value;
          o.textContent = opt.label;
          s.appendChild(o);
        }
        if (f.defaultValue != null) s.value = String(f.defaultValue);
        return s;
      }
      case 'multi-choice': {
        const wrap = document.createElement('div');
        wrap.className = 'dm-multi';
        wrap.tabIndex = -1;
        // Column-major layout into Choice.Columns columns (1 = single stack).
        const mcCols = f.choice?.columns;
        if (typeof mcCols === 'number' && mcCols > 1) wrap.style.setProperty('--dm-choice-cols', mcCols);
        const seeds = Array.isArray(f.defaultValue) ? f.defaultValue : null;
        for (const opt of (f.choice?.choices || [])) {
          const lab = document.createElement('label');
          lab.className = 'dm-opt';
          const cb = document.createElement('input');
          cb.type = 'checkbox';
          cb.tabIndex = 0;
          cb.value = opt.value;
          cb.dataset.optValue = opt.value;
          if (seeds && seeds.includes(opt.value)) cb.checked = true;
          cb.addEventListener('keydown', dmGuard('multi-keydown', e => {
            if (e.key === 'Enter' || e.key === ' ') {
              e.preventDefault();
              cb.checked = !cb.checked;
              cb.dispatchEvent(new Event('change', { bubbles: true }));
            }
          }));
          lab.appendChild(cb);
          // Optional Choice.Color renders as an 8px dot, Choice.Icon as a
          // text glyph (FA codepoint or any unicode char). Both before the
          // label so the visual ordering matches the Uno ItemTemplate.
          if (opt.color) {
            const dot = document.createElement('span');
            dot.className = 'dm-choice-dot';
            dot.style.background = opt.color;
            lab.appendChild(dot);
          }
          if (opt.icon) {
            const ic = document.createElement('span');
            ic.className = 'dm-choice-icon';
            ic.textContent = opt.icon;
            lab.appendChild(ic);
          }
          lab.appendChild(document.createTextNode(' ' + opt.label));
          wrap.appendChild(lab);
        }
        return wrap;
      }
      case 'list': {
        // Tag-style list: type, press Enter (or comma) to add a chip; click
        // × on a chip to remove. Read via readValue → array of strings.
        const wrap = document.createElement('div');
        wrap.className = 'dm-chips';
        const items = document.createElement('span');
        items.className = 'dm-chips-items';
        const empty = document.createElement('span');
        empty.className = 'dm-chips-empty';
        empty.textContent = t('no_items', 'No items');
        items.appendChild(empty);
        const inp = document.createElement('input');
        inp.type = 'text';
        inp.className = 'dm-chip-input';
        inp.placeholder = ph || t('add_and_press_enter', 'Add and press Enter');
        wrap.append(items, inp);

        function addChip(value) {
          const v = String(value).trim();
          if (!v) return;
          const chip = document.createElement('span');
          chip.className = 'dm-chip';
          chip.dataset.value = v;
          chip.append(document.createTextNode(v));
          const x = document.createElement('button');
          x.type = 'button';
          x.className = 'dm-chip-x';
          x.textContent = '×';
          x.addEventListener('click', () => {
            chip.remove();
            if (!items.querySelector('.dm-chip')) items.appendChild(empty);
            inp.dispatchEvent(new Event('input', { bubbles: false }));
          });
          chip.appendChild(x);
          if (empty.parentNode) empty.remove();
          items.appendChild(chip);
        }
        inp.addEventListener('keydown', e => {
          if (e.key === 'Enter' || e.key === ',') {
            e.preventDefault();
            addChip(inp.value);
            inp.value = '';
            inp.dispatchEvent(new Event('input', { bubbles: false }));
          }
        });
        // Blur-commit: a pending tag the user typed without pressing Enter
        // is otherwise lost on submit (readValue only sees committed
        // `.dm-chip` elements). Tabbing away or clicking Submit blurs the
        // input, so flushing here picks up the dangling value.
        inp.addEventListener('blur', () => {
          if (!inp.value.trim()) return;
          addChip(inp.value);
          inp.value = '';
          inp.dispatchEvent(new Event('input', { bubbles: false }));
        });
        if (Array.isArray(f.defaultValue)) {
          for (const v of f.defaultValue) addChip(v);
        }
        return wrap;
      }
      case 'geo': {
        // Geo control: address-search input with a Nominatim (OSM)
        // autocomplete dropdown, plus collapsible Lat / Lng inputs for
        // manual fine-tune. Mirrors the desktop records-grid inline
        // editor (DataMaker/Presentation/RecordList/InlineEditors/
        // GeoCellColumn.cs). Picking a suggestion fills both lat/lng
        // AND formattedAddress so the storage round-trip keeps the
        // address. Free-text in the search box that didn't match a
        // suggestion still wins as the formattedAddress, paired with
        // whatever lat/lng the user typed.
        const wrap = document.createElement('div');
        wrap.className = 'dm-geo';

        // Address row: input + suggestion list (absolute-positioned).
        const addrRow = document.createElement('div');
        addrRow.className = 'dm-geo-addr-row';
        const addr = document.createElement('input');
        addr.type = 'text';
        addr.placeholder = t('geo_address_placeholder', 'Type an address…');
        addr.className = 'dm-geo-addr';
        addrRow.appendChild(addr);
        const suggList = document.createElement('div');
        suggList.className = 'dm-geo-suggestions';
        suggList.hidden = true;
        addrRow.appendChild(suggList);
        wrap.appendChild(addrRow);

        // Lat / Lng row.
        const coords = document.createElement('div');
        coords.className = 'dm-geo-coords';
        const lat = document.createElement('input');
        lat.type = 'number';  lat.step = 'any';  lat.placeholder = 'Latitude';
        const lng = document.createElement('input');
        lng.type = 'number';  lng.step = 'any';  lng.placeholder = 'Longitude';
        coords.appendChild(lat);
        coords.appendChild(lng);
        wrap.appendChild(coords);

        // Hydrate from defaultValue (also the edit-flow seed path).
        if (f.defaultValue && typeof f.defaultValue === 'object') {
          if (Number.isFinite(f.defaultValue.lat)) lat.value = String(f.defaultValue.lat);
          if (Number.isFinite(f.defaultValue.lng)) lng.value = String(f.defaultValue.lng);
          if (f.defaultValue.formattedAddress) {
            wrap._dmFormattedAddress = f.defaultValue.formattedAddress;
            addr.value = f.defaultValue.formattedAddress;
          }
        }

        // Manual edit of the address box (without picking a suggestion)
        // counts as the user setting a free-text label — keep it.
        addr.addEventListener('change', () => {
          wrap._dmFormattedAddress = addr.value.trim() || null;
        });

        // Debounced Nominatim autocomplete. 350ms debounce + min 3 chars.
        // Browser can't set User-Agent (forbidden header) — Nominatim
        // policy accepts a Referer-identified browser caller for low
        // volume, which this is. No API key, no auth.
        let debounce = null;
        let lastQuery = '';
        addr.addEventListener('input', () => {
          const q = addr.value.trim();
          if (q === lastQuery) return;
          lastQuery = q;
          if (debounce) { clearTimeout(debounce); debounce = null; }
          if (q.length < 3) {
            suggList.hidden = true;
            suggList.innerHTML = '';
            return;
          }
          debounce = setTimeout(() => fetchSuggestions(q), 350);
        });

        async function fetchSuggestions(q) {
          try {
            const url = 'https://nominatim.openstreetmap.org/search?q='
                      + encodeURIComponent(q) + '&format=json&limit=6&addressdetails=0';
            const res = await fetch(url, { headers: { 'Accept': 'application/json' } });
            if (!res.ok) return;
            const list = await res.json();
            renderSuggestions(Array.isArray(list) ? list : []);
          } catch {
            // Network / rate-limit / quota — silently swallow, the user
            // can still fall back to manual lat/lng entry.
          }
        }

        function renderSuggestions(list) {
          suggList.innerHTML = '';
          if (list.length === 0) { suggList.hidden = true; return; }
          for (const r of list) {
            const item = document.createElement('div');
            item.className = 'dm-geo-suggestion';
            item.textContent = r.display_name || '';
            item.addEventListener('mousedown', (ev) => {
              ev.preventDefault();   // keep focus on input through the pick
              const la = parseFloat(r.lat);
              const ln = parseFloat(r.lon);
              if (Number.isFinite(la)) lat.value = String(la);
              if (Number.isFinite(ln)) lng.value = String(ln);
              addr.value = r.display_name || '';
              wrap._dmFormattedAddress = r.display_name || null;
              suggList.hidden = true;
              suggList.innerHTML = '';
            });
            suggList.appendChild(item);
          }
          suggList.hidden = false;
        }

        // Click outside the row closes the dropdown.
        document.addEventListener('mousedown', (ev) => {
          if (!addrRow.contains(ev.target)) {
            suggList.hidden = true;
          }
        });

        return wrap;
      }
      case 'image': {
        // The slot itself is the picker affordance — click anywhere inside
        // to browse, drop an image to upload. The picked file is read as a
        // data URI and stashed on wrap._dmValue as a typed ImageRef shape
        // ({dataUri, fileName, mime, sizeBytes}) — that's the schema's
        // serialized form (see ImageRef.cs), so values bag and any future
        // submit pipeline get the right shape directly.
        const wrap = document.createElement('div');
        wrap.className = 'dm-image-control';
        const slot = document.createElement('button');
        slot.type = 'button';
        slot.className = 'dm-image-slot';
        const empty = document.createElement('span');
        empty.className = 'dm-image-empty';
        empty.textContent = t('click_to_upload', 'Click to upload');
        const preview = document.createElement('img');
        preview.className = 'dm-image-preview';
        preview.hidden = true;
        slot.append(empty, preview);

        const file = document.createElement('input');
        file.type = 'file';
        // AttachmentOptions.AcceptedExtensions narrows the picker filter when
        // set; otherwise default to "any image MIME". The HTML5 spec accepts
        // a comma-list of extensions or MIME types.
        file.accept = (f.attachment && Array.isArray(f.attachment.acceptedExtensions) && f.attachment.acceptedExtensions.length > 0)
          ? f.attachment.acceptedExtensions.map(e => e.startsWith('.') ? e : '.' + e).join(',')
          : 'image/*';
        file.hidden = true;
        slot.addEventListener('click', () => file.click());
        file.addEventListener('change', async () => {
          const f0 = file.files && file.files[0];
          if (!f0) return;

          // Show local preview immediately for UX — independent of the
          // upload round-trip below. createObjectURL is cheap and lets
          // the user see what they picked while bytes are in flight.
          const localPreviewUrl = URL.createObjectURL(f0);
          preview.src = localPreviewUrl;
          preview.hidden = false;
          empty.hidden = true;

          // Submit gating: form-level runSchemaAction checks this flag.
          wrap._dmUploading = true;
          empty.textContent = t('uploading', 'Uploading…');
          empty.hidden = false;  // restore for status, sits over the preview

          try {
            const urlRef = await uploadFileToSlot(f0);
            if (urlRef) {
              wrap._dmValue = urlRef;
              empty.hidden = true;
            } else {
              // Host doesn't support upload-slot (designer preview, legacy
              // bridge) — fall back to the legacy inline data-URI path so
              // the form still has a value at submit-time.
              const reader = new FileReader();
              await new Promise((resolve, reject) => {
                reader.onload  = resolve;
                reader.onerror = reject;
                reader.readAsDataURL(f0);
              });
              wrap._dmValue = {
                dataUri:   reader.result,
                fileName:  f0.name,
                mime:      f0.type || null,
                sizeBytes: f0.size,
              };
              empty.hidden = true;
            }
          } catch (err) {
            // Upload failed mid-flight (network blip, expired URL, etc.).
            // Surface a retryable error in the slot affordance + leave
            // _dmValue null so submit stays blocked until the user picks
            // again. Clear local preview URL on failure so retry shows
            // empty state again.
            empty.hidden = false;
            empty.textContent = t('upload_failed', 'Upload failed — try again');
            preview.hidden = true;
            URL.revokeObjectURL(localPreviewUrl);
            wrap._dmValue = null;
          } finally {
            wrap._dmUploading = false;
            wrap.dispatchEvent(new Event('change', { bubbles: false }));
          }
        });
        wrap.append(slot, file);
        // Seed default — schema may carry an ImageRef object (URL or
        // legacy data-URI shape), or (legacy) a bare data-URI string
        // that the schema's converter still accepts.
        if (f.defaultValue) {
          const dv = typeof f.defaultValue === 'string'
            ? { dataUri: f.defaultValue }
            : f.defaultValue;
          const src = dv.url || dv.dataUri;
          if (src) {
            preview.src = src;
            preview.hidden = false;
            empty.hidden = true;
            wrap._dmValue = {
              dataUri:   dv.dataUri  ?? null,
              url:       dv.url      ?? null,
              hash:      dv.hash     ?? null,
              owned:     !!dv.owned,
              fileName:  dv.fileName  ?? null,
              mime:      dv.mime      ?? null,
              sizeBytes: dv.sizeBytes ?? null,
            };
          }
        }
        return wrap;
      }
      case 'attachment': {
        // Mirrors Uno's AttachmentFieldEditor: file glyph + filename
        // caption + Browse button (+ Clear when filled). Picked file is
        // read as a data URI and stashed on wrap._dmValue in the
        // AttachmentRef shape so submit serialises the bytes.
        const wrap = document.createElement('div');
        wrap.className = 'dm-attach-control';

        const icon = document.createElement('span');
        icon.className = 'dm-attach-icon';
        // Inline SVG file glyph (no font dep, themes via currentColor).
        icon.innerHTML = '<svg viewBox="0 0 24 24" width="18" height="18" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
                       + '<path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>'
                       + '<polyline points="14 2 14 8 20 8"/>'
                       + '</svg>';

        const slot = document.createElement('div');
        slot.className = 'dm-attach-slot';
        slot.textContent = t('no_file_selected', 'No file selected');

        const file = document.createElement('input');
        file.type = 'file';
        // Honour AcceptedExtensions on attachment too — empty/missing leaves
        // accept unset (any file allowed, matching the schema's "no
        // extension list" → "no client-side filter").
        if (f.attachment && Array.isArray(f.attachment.acceptedExtensions) && f.attachment.acceptedExtensions.length > 0) {
          file.accept = f.attachment.acceptedExtensions.map(e => e.startsWith('.') ? e : '.' + e).join(',');
        }
        file.hidden = true;
        const browse = document.createElement('button');
        browse.type = 'button';
        browse.className = 'dm-attach-browse';
        browse.textContent = t('browse', 'Browse…');
        browse.tabIndex = 0;
        browse.addEventListener('click', () => file.click());
        browse.addEventListener('keydown', e => {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); file.click(); }
        });

        const clear = document.createElement('button');
        clear.type = 'button';
        clear.className = 'dm-attach-clear';
        clear.textContent = t('clear', 'Clear');
        clear.tabIndex = 0;
        clear.hidden = true;
        function doClear() {
          file.value = '';
          slot.textContent = t('no_file_selected', 'No file selected');
          clear.hidden = true;
          wrap._dmValue = null;
          wrap.dispatchEvent(new Event('change', { bubbles: false }));
        }
        clear.addEventListener('click', doClear);
        clear.addEventListener('keydown', e => {
          if (e.key === 'Enter' || e.key === ' ') { e.preventDefault(); doClear(); }
        });

        file.addEventListener('change', async () => {
          const f0 = file.files && file.files[0];
          if (!f0) {
            slot.textContent = t('no_file_selected', 'No file selected');
            clear.hidden = true;
            wrap._dmValue = null;
            wrap.dispatchEvent(new Event('change', { bubbles: false }));
            return;
          }
          slot.textContent = t('uploading', 'Uploading…') + ' ' + f0.name;
          clear.hidden = true;
          wrap._dmUploading = true;
          try {
            const urlRef = await uploadFileToSlot(f0);
            if (urlRef) {
              wrap._dmValue = urlRef;
              slot.textContent = f0.name;
              clear.hidden = false;
            } else {
              // Host doesn't support upload-slot — fall back to inline so
              // the legacy bridge / designer-preview keeps working.
              const reader = new FileReader();
              await new Promise((resolve, reject) => {
                reader.onload  = resolve;
                reader.onerror = reject;
                reader.readAsDataURL(f0);
              });
              wrap._dmValue = {
                dataUri:   reader.result,
                fileName:  f0.name,
                mime:      f0.type || null,
                sizeBytes: f0.size,
              };
              slot.textContent = f0.name;
              clear.hidden = false;
            }
          } catch (_) {
            slot.textContent = t('upload_failed', 'Upload failed — try again');
            clear.hidden = false;
            wrap._dmValue = null;
          } finally {
            wrap._dmUploading = false;
            wrap.dispatchEvent(new Event('change', { bubbles: false }));
          }
        });
        wrap.append(icon, slot, browse, clear, file);
        if (f.defaultValue) {
          const dv = typeof f.defaultValue === 'string'
            ? { dataUri: f.defaultValue }
            : f.defaultValue;
          if (dv.dataUri || dv.url) {
            slot.textContent = dv.fileName || 'Attached file';
            clear.hidden = false;
            wrap._dmValue = {
              dataUri:   dv.dataUri  ?? null,
              url:       dv.url      ?? null,
              hash:      dv.hash     ?? null,
              owned:     !!dv.owned,
              fileName:  dv.fileName  ?? null,
              mime:      dv.mime      ?? null,
              sizeBytes: dv.sizeBytes ?? null,
            };
          }
        }
        return wrap;
      }
      case 'signature':
      case 'initials': {
        // Canvas signature pad — the signer draws with mouse / touch / pen.
        // Strokes are flattened to a transparent PNG data URI and stashed on
        // wrap._dmValue as a SignatureRef shape ({dataUri, mime, sizeBytes}),
        // matching SignatureRef.cs so the values bag gets the right shape.
        const compact = (f.kind || '').toLowerCase() === 'initials';
        const wrap = document.createElement('div');
        wrap.className = 'dm-signature-control';
        const pad = document.createElement('div');
        pad.className = 'dm-signature-pad';
        pad.style.position = 'relative';
        pad.style.width  = (compact ? 160 : 340) + 'px';
        pad.style.height = (compact ? 80 : 120) + 'px';

        const canvas = document.createElement('canvas');
        const cw = compact ? 160 : 340, ch = compact ? 80 : 120;
        // Render at 2× for crisp ink on HiDPI; CSS box stays logical size.
        const scale = (window.devicePixelRatio || 1);
        canvas.width  = Math.round(cw * scale);
        canvas.height = Math.round(ch * scale);
        canvas.style.width  = cw + 'px';
        canvas.style.height = ch + 'px';
        canvas.style.touchAction = 'none';
        canvas.className = 'dm-signature-canvas';

        const hint = document.createElement('span');
        hint.className = 'dm-signature-hint';
        hint.textContent = compact ? t('initials_here', 'Initials') : t('sign_here', 'Sign here');

        const clear = document.createElement('button');
        clear.type = 'button';
        clear.className = 'dm-signature-clear';
        clear.textContent = '✕';
        clear.hidden = true;
        clear.setAttribute('aria-label', t('clear_signature', 'Clear signature'));

        pad.append(canvas, hint, clear);
        wrap.append(pad);

        const ctx = canvas.getContext('2d');
        ctx.scale(scale, scale);
        ctx.lineWidth = 2.5;
        ctx.lineCap = 'round';
        ctx.lineJoin = 'round';
        // Display ink follows the theme — light on a dark pad, dark on a light
        // pad — mirroring the desktop SignatureFieldEditor. The persisted PNG
        // is ALWAYS rasterised in black (see commit()), so a signature captured
        // in dark mode still reads on the light design / PDF.
        const isDark = () => document.documentElement.classList.contains('dm-dark');
        const displayInk = () => {
          const v = getComputedStyle(document.documentElement)
            .getPropertyValue('--dm-ink').trim();
          return v || (isDark() ? '#e6e6e6' : '#000');
        };
        ctx.strokeStyle = displayInk();

        // Printed name (→ SignatureRef.typedName) + auto-stamped signed date.
        const nameInput = document.createElement('input');
        nameInput.type = 'text';
        nameInput.className = 'dm-signature-name';
        nameInput.maxLength = 120;
        nameInput.placeholder = t('printed_name', 'Printed name');
        nameInput.style.width = cw + 'px';
        const dateLine = document.createElement('div');
        dateLine.className = 'dm-signature-date';
        dateLine.hidden = true;
        wrap.append(nameInput, dateLine);

        let drawing = false, hasInk = false, last = null;
        let inkDataUri = null, inkUrl = null;
        // Captured stroke geometry (arrays of {x,y} logical px). Kept so the
        // committed PNG can be re-rasterised in black regardless of the on-
        // screen display ink — the desktop control does the same split.
        const strokes = [];
        let current = null;

        function pos(e) {
          const r = canvas.getBoundingClientRect();
          const pt = (e.touches && e.touches[0]) || e;
          return { x: pt.clientX - r.left, y: pt.clientY - r.top };
        }
        // Rasterise the captured strokes into a transparent black-ink PNG.
        function commit() {
          if (!strokes.length) { inkDataUri = null; inkUrl = null; return; }
          const off = document.createElement('canvas');
          off.width = canvas.width; off.height = canvas.height;
          const octx = off.getContext('2d');
          octx.scale(scale, scale);
          octx.lineWidth = 2.5;
          octx.lineCap = 'round';
          octx.lineJoin = 'round';
          octx.strokeStyle = '#000';
          for (const s of strokes) {
            if (!s.length) continue;
            octx.beginPath();
            octx.moveTo(s[0].x, s[0].y);
            if (s.length === 1) octx.lineTo(s[0].x + 0.01, s[0].y);  // a tap → dot
            else for (let i = 1; i < s.length; i++) octx.lineTo(s[i].x, s[i].y);
            octx.stroke();
          }
          inkDataUri = off.toDataURL('image/png');
          inkUrl = null;
        }
        // Combine ink + printed name into one SignatureRef, stamp the date.
        function rebuild() {
          const name = (nameInput.value || '').trim();
          if (!inkDataUri && !inkUrl && !name) {
            wrap._dmValue = null;
            dateLine.hidden = true;
          } else {
            const signedAt = new Date().toISOString();
            wrap._dmValue = {
              dataUri:   inkDataUri,
              url:       inkUrl,
              mime:      (inkDataUri || inkUrl) ? 'image/png' : null,
              sizeBytes: inkDataUri ? Math.round(inkDataUri.length * 0.75) : null,
              typedName: name || null,
              signedAt,
            };
            dateLine.textContent = t('signed', 'Signed') + ' ' + signedAt.slice(0, 10);
            dateLine.hidden = false;
          }
          wrap.dispatchEvent(new Event('change', { bubbles: false }));
        }
        function start(e) {
          e.preventDefault();
          drawing = true; hint.hidden = true; clear.hidden = false;
          ctx.strokeStyle = displayInk();
          last = pos(e);
          current = [last];
          strokes.push(current);
        }
        function move(e) {
          if (!drawing) return;
          e.preventDefault();
          const p = pos(e);
          ctx.beginPath();
          ctx.moveTo(last.x, last.y);
          ctx.lineTo(p.x, p.y);
          ctx.stroke();
          last = p; hasInk = true;
          if (current) current.push(p);
        }
        function end() {
          if (!drawing) return;
          drawing = false;
          if (hasInk) commit();
          current = null;
          rebuild();
        }
        canvas.addEventListener('mousedown', start);
        window.addEventListener('mousemove', move);
        window.addEventListener('mouseup', end);
        canvas.addEventListener('touchstart', start, { passive: false });
        canvas.addEventListener('touchmove', move, { passive: false });
        canvas.addEventListener('touchend', end);
        nameInput.addEventListener('input', rebuild);

        // The Clear ✕ is revealed mid-gesture (start() sets clear.hidden=false),
        // and the browser then dispatches the gesture's own click to it as well
        // as to the canvas — a phantom click whose target is the button even
        // though the pointer is nowhere near it. Unguarded, that wiped the
        // signature (and re-hid the button) the instant the pen lifted. Arm the
        // wipe only from a real pointerdown ON the button, so synthetic clicks
        // that never pressed it are ignored.
        let clearArmed = false;
        clear.addEventListener('pointerdown', () => { clearArmed = true; });
        clear.addEventListener('click', () => {
          if (!clearArmed) return;
          clearArmed = false;
          ctx.clearRect(0, 0, canvas.width, canvas.height);
          strokes.length = 0; current = null;
          hasInk = false; inkDataUri = null; inkUrl = null;
          hint.hidden = false; clear.hidden = true;
          rebuild();   // keeps a typed name; nulls the value only if name is empty too
        });

        // Seed default — schema may carry a SignatureRef object or a bare
        // data-URI string.
        if (f.defaultValue) {
          const dv = typeof f.defaultValue === 'string' ? { dataUri: f.defaultValue } : f.defaultValue;
          const src = dv.url || dv.dataUri;
          if (src) {
            // Stored PNG carries black ink; on a dark pad RGB-invert it
            // (black → white) for display, preserving alpha — matches the
            // desktop InvertPng path. The stored value stays black.
            const img = new Image();
            img.onload = () => {
              if (isDark()) {
                ctx.save();
                ctx.filter = 'invert(1)';
                ctx.drawImage(img, 0, 0, cw, ch);
                ctx.restore();
              } else {
                ctx.drawImage(img, 0, 0, cw, ch);
              }
            };
            img.src = src;
            hasInk = true; hint.hidden = true; clear.hidden = false;
            inkDataUri = dv.dataUri ?? null;
            inkUrl = dv.url ?? null;
          }
          if (dv.typedName) nameInput.value = dv.typedName;
          if (src || dv.typedName) rebuild();
        }
        return wrap;
      }
      case 'relation': {
        const i = document.createElement('input');
        i.type = 'text';
        i.placeholder = '(relation)';
        return i;
      }
      default: {
        const i = document.createElement('input');
        i.type = 'text';
        if (ph) i.placeholder = ph;
        return i;
      }
    }
  }

  // ─── Date / DateTime composite picker ───────────────────────
  // Custom Tempus-Dominus-style picker: bordered field + popup with a
  // calendar page and (when includeTime=true) a sliding time-edit page.
  // Mirrors the Uno DateTimePicker control for visual+behavioural parity
  // across the desktop preview and the recipient web view. State lives on
  // the wrap element (`_dmValue` = ISO string for the form's readValue;
  // `_state` = picker internals: Date object, hours, minutes, view month).
  function buildDateTimeField(f, includeTime) {
    const wrap = document.createElement('div');
    wrap.className = 'dm-dt-field' + (includeTime ? '' : ' dm-dt-no-time');
    const input = document.createElement('input');
    input.type = 'text';
    input.className = 'dm-dt-input';
    input.placeholder = f.placeholder || (includeTime ? 'Select date and time' : 'Select a date');
    const icon = document.createElement('span');
    icon.className = 'dm-dt-icon';
    // Inline SVG calendar — no font dependency, themes via
    // `currentColor`, renders identically with or without the brand
    // stylesheet loaded.
    icon.innerHTML = '<svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">'
                   + '<rect x="3" y="4" width="18" height="18" rx="2" ry="2"/>'
                   + '<line x1="16" y1="2" x2="16" y2="6"/>'
                   + '<line x1="8"  y1="2" x2="8"  y2="6"/>'
                   + '<line x1="3"  y1="10" x2="21" y2="10"/>'
                   + '</svg>';
    wrap.appendChild(input);
    wrap.appendChild(icon);

    // Auto-detect 12h/24h from the user's locale. Intl returns hour12=true
    // for en-US, false for nl-NL/de-DE/fr-FR — same heuristic the Uno
    // picker uses (LongTimePattern containing 't').
    const hour12 = !!new Intl.DateTimeFormat(undefined, { hour: 'numeric' }).resolvedOptions().hour12;

    const today = new Date();
    // Schema-side options (DateOptions): format string + min/max ISO bounds.
    // Strings are parsed once at build; null means "no constraint".
    const dateOpts = f.date || {};
    const state = {
      date: null,                           // selected Date | null
      hours: 0, minutes: 0,                 // 24h-internal
      viewMonth: today.getMonth(),
      viewYear:  today.getFullYear(),
      viewMode:  'days',                    // 'days' | 'months' | 'years' (zoom)
      hour12,
      minDate: parseDateOnly(dateOpts.min), // Date at midnight or null
      maxDate: parseDateOnly(dateOpts.max),
    };
    wrap._state = state;

    const popup = document.createElement('div');
    popup.className = 'dm-dt-popup';
    const pages = document.createElement('div');
    pages.className = 'dm-dt-pages';
    popup.appendChild(pages);

    const calPage  = document.createElement('div');
    calPage.className = 'dm-dt-page dm-dt-cal-page';
    pages.appendChild(calPage);
    let timePage = null;
    if (includeTime) {
      timePage = document.createElement('div');
      timePage.className = 'dm-dt-page dm-dt-time-page';
      pages.appendChild(timePage);
    }
    wrap.appendChild(popup);

    buildCalendarPage(calPage, state, () => commit(), includeTime, () => wrap.classList.add('dm-show-time'));
    if (timePage) buildTimePage(timePage, state, () => commit(), () => wrap.classList.remove('dm-show-time'));

    // Field click toggles the popup; clicks inside the popup don't bubble
    // (we stopPropagation on the popup wrapper).
    popup.addEventListener('click', e => e.stopPropagation());
    // Only the icon opens the popup; clicking the input focuses for typing.
    icon.addEventListener('click', e => {
      e.stopPropagation();
      const opening = !wrap.classList.contains('dm-open');
      wrap.classList.toggle('dm-open');
      if (opening) wrap.classList.remove('dm-show-time');
    });
    document.addEventListener('click', e => {
      if (!wrap.contains(e.target)) wrap.classList.remove('dm-open');
    });

    // Keyboard: Enter commits typed text, Alt+Down opens calendar.
    input.addEventListener('keydown', e => {
      if (e.key === 'Enter') {
        e.preventDefault();
        commitTypedInput();
      } else if (e.key === 'ArrowDown' && e.altKey) {
        e.preventDefault();
        wrap.classList.add('dm-open');
        wrap.classList.remove('dm-show-time');
      }
    });
    input.addEventListener('blur', () => commitTypedInput());

    function commitTypedInput() {
      const raw = input.value.trim();
      if (!raw) {
        state.date = null;
        state.hours = 0;
        state.minutes = 0;
        commit();
        return;
      }
      const d = parseLocalDate(raw);
      if (d) {
        state.date = new Date(d.getFullYear(), d.getMonth(), d.getDate());
        state.viewMonth = d.getMonth();
        state.viewYear  = d.getFullYear();
        if (includeTime) {
          state.hours   = d.getHours();
          state.minutes = d.getMinutes();
        }
        renderCalendarGrid(calPage, state, () => commit());
        if (timePage) renderTimeBoxes(timePage, state);
        commit();
      } else {
        paintField(wrap, input, state, includeTime);
      }
    }

    // Bridge: the calculated-fields pipeline sets `target.disabled = true`
    // on inputs. <div>s don't honour the disabled property natively, so we
    // intercept it here and reflect onto the .dm-dt-no-time / .disabled
    // class — CSS does the visual + click suppression.
    Object.defineProperty(wrap, 'disabled', {
      get()  { return wrap.classList.contains('disabled'); },
      set(v) { wrap.classList.toggle('disabled', !!v); },
      configurable: true,
    });

    // Seed default value if the schema provides one.
    if (f.defaultValue) seedFromIso(state, String(f.defaultValue), includeTime);
    renderCalendarGrid(calPage, state, () => commit());
    if (timePage) renderTimeBoxes(timePage, state);
    paintField(wrap, input, state, includeTime);

    function commit() {
      paintField(wrap, input, state, includeTime);
      wrap._dmValue = serializeIso(state, includeTime);
      // Form's value-change pipeline listens for `change` on the returned
      // element. Synthesize one so calculated fields + validation refresh.
      wrap.dispatchEvent(new Event('change', { bubbles: true }));
      wrap.dispatchEvent(new Event('input',  { bubbles: true }));
    }

    return wrap;
  }

  function seedFromIso(state, iso, includeTime) {
    const d = new Date(iso);
    if (isNaN(d.getTime())) return;
    state.date = new Date(d.getFullYear(), d.getMonth(), d.getDate());
    state.viewMonth = d.getMonth();
    state.viewYear  = d.getFullYear();
    if (includeTime) {
      state.hours   = d.getHours();
      state.minutes = d.getMinutes();
    }
  }

  function serializeIso(state, includeTime) {
    if (!state.date) return null;
    const y  = state.date.getFullYear();
    const m  = String(state.date.getMonth() + 1).padStart(2, '0');
    const dd = String(state.date.getDate()).padStart(2, '0');
    if (!includeTime) return `${y}-${m}-${dd}`;
    const hh = String(state.hours).padStart(2, '0');
    const mi = String(state.minutes).padStart(2, '0');
    // Local-time ISO with explicit offset so the round-trip preserves the
    // user's wall-clock — matches what the Uno picker writes.
    const off = -new Date(y, state.date.getMonth(), state.date.getDate(),
                         state.hours, state.minutes).getTimezoneOffset();
    const sign = off >= 0 ? '+' : '-';
    const oh = String(Math.floor(Math.abs(off) / 60)).padStart(2, '0');
    const om = String(Math.abs(off) % 60).padStart(2, '0');
    return `${y}-${m}-${dd}T${hh}:${mi}:00${sign}${oh}:${om}`;
  }

  function paintField(wrap, input, state, includeTime) {
    if (!state.date) {
      input.value = '';
      return;
    }
    const d = new Date(state.date);
    if (includeTime) d.setHours(state.hours, state.minutes, 0, 0);
    if (_datePattern) {
      const pat = includeTime && _timePattern
        ? _datePattern + ' ' + _timePattern
        : _datePattern;
      input.value = formatDateNet(d, pat);
    } else {
      input.value = includeTime
        ? d.toLocaleString(_locale, { dateStyle: 'short', timeStyle: 'short' })
        : d.toLocaleDateString(_locale, { dateStyle: 'short' });
    }
  }

  // ── Date helpers ─────────────────────────────────────────────

  // Culture-aware date parser. Accepts:
  //   ISO:       2025-01-23, 2025-01-23T14:30
  //   Locale:    23/01/2025, 23-01-2025, 23.01.2025 (DMY locales)
  //              01/23/2025, 01-23-2025              (MDY locales)
  //   Shorthand: 23/1/25, 1/23/25 (2-digit year → 2000+)
  // Returns a Date or null on failure.
  function parseLocalDate(raw) {
    // Try ISO / native first (handles "2025-01-23" and "Jan 23 2025" etc.)
    let d = new Date(raw);
    if (!isNaN(d.getTime())) return d;

    // Split on common separators.
    const parts = raw.split(/[\s]+/);
    const datePart = parts[0];
    const timePart = parts.length > 1 ? parts.slice(1).join(' ') : null;

    const nums = datePart.split(/[\/\-\.]/);
    if (nums.length < 3) return null;

    let day, month, year;
    // If first segment is 4 digits → YYYY-MM-DD
    if (nums[0].length === 4) {
      year = +nums[0]; month = +nums[1]; day = +nums[2];
    } else if (_dmy) {
      day = +nums[0]; month = +nums[1]; year = +nums[2];
    } else {
      month = +nums[0]; day = +nums[1]; year = +nums[2];
    }
    if (year < 100) year += 2000;
    if (month < 1 || month > 12 || day < 1 || day > 31) return null;

    d = new Date(year, month - 1, day);
    if (d.getMonth() !== month - 1) return null; // overflow (e.g. Feb 30)

    if (timePart) {
      const t = new Date('1970-01-01T' + timePart);
      if (!isNaN(t.getTime())) {
        d.setHours(t.getHours(), t.getMinutes(), t.getSeconds());
      }
    }
    return d;
  }

  // Parse an ISO date / datetime string into a Date at midnight (date-only
  // comparison). Returns null on bad / empty input — callers treat null
  // as "no constraint".
  function parseDateOnly(iso) {
    if (!iso) return null;
    const d = new Date(iso);
    if (isNaN(d.getTime())) return null;
    d.setHours(0, 0, 0, 0);
    return d;
  }

  // True iff `d` falls outside [min, max]. Either bound may be null. Both
  // sides compared at midnight so time-of-day doesn't accidentally exclude
  // the bound day itself.
  function outOfRange(d, min, max) {
    const t = new Date(d);
    t.setHours(0, 0, 0, 0);
    if (min && t < min) return true;
    if (max && t > max) return true;
    return false;
  }

  // .NET → JS format applier covering the documented subset (yyyy yy
  // MMMM MMM MM M dd d HH hh mm ss tt). Unknown chars pass through as
  // literals. Mirrors Uno's <see cref="DateTime.ToString(format)"/> behaviour
  // for the format strings the schema's DateOptions.Format actually uses.
  function formatDateNet(d, fmt) {
    const monShort = new Intl.DateTimeFormat(undefined, { month: 'short' }).format(d);
    const monLong  = new Intl.DateTimeFormat(undefined, { month: 'long'  }).format(d);
    let out = '', i = 0;
    while (i < fmt.length) {
      const rest = fmt.slice(i);
      if (rest.startsWith('yyyy')) { out += String(d.getFullYear()).padStart(4, '0'); i += 4; }
      else if (rest.startsWith('yy'))   { out += String(d.getFullYear()).slice(-2); i += 2; }
      else if (rest.startsWith('MMMM')) { out += monLong;  i += 4; }
      else if (rest.startsWith('MMM'))  { out += monShort; i += 3; }
      else if (rest.startsWith('MM'))   { out += String(d.getMonth() + 1).padStart(2, '0'); i += 2; }
      else if (rest.startsWith('M'))    { out += d.getMonth() + 1; i += 1; }
      else if (rest.startsWith('dd'))   { out += String(d.getDate()).padStart(2, '0'); i += 2; }
      else if (rest.startsWith('d'))    { out += d.getDate(); i += 1; }
      else if (rest.startsWith('HH'))   { out += String(d.getHours()).padStart(2, '0'); i += 2; }
      else if (rest.startsWith('hh'))   { out += String(((d.getHours() + 11) % 12) + 1).padStart(2, '0'); i += 2; }
      else if (rest.startsWith('mm'))   { out += String(d.getMinutes()).padStart(2, '0'); i += 2; }
      else if (rest.startsWith('ss'))   { out += String(d.getSeconds()).padStart(2, '0'); i += 2; }
      else if (rest.startsWith('tt'))   { out += d.getHours() >= 12 ? 'PM' : 'AM'; i += 2; }
      // .NET convention: '/' = date separator, ':' = time separator
      // (culture-specific, not literal). Replace with detected locale seps.
      else if (fmt[i] === '/') { out += _dateFormat.sep; i += 1; }
      else if (fmt[i] === ':') { out += ':'; i += 1; }
      else { out += fmt[i]; i += 1; }
    }
    return out;
  }

  // ── Calendar page ────────────────────────────────────────────
  function buildCalendarPage(host, state, onChange, includeTime, onGoTime) {
    const header = document.createElement('div');
    header.className = 'dm-dt-cal-header';
    const prev = document.createElement('button');
    prev.className = 'dm-dt-cal-nav';
    prev.type = 'button';
    prev.textContent = '‹';  // ‹
    // Clickable header label: cycles 'days' -> 'months' -> 'years' on each
    // click — same zoom-out pattern as Tempus Dominus / WinUI CalendarView.
    // Cell click drills back in. Once at 'years' the header click is a no-op.
    const month = document.createElement('button');
    month.className = 'dm-dt-cal-month';
    month.type = 'button';
    const next = document.createElement('button');
    next.className = 'dm-dt-cal-nav';
    next.type = 'button';
    next.textContent = '›';  // ›
    header.appendChild(prev); header.appendChild(month); header.appendChild(next);
    host.appendChild(header);

    const weekdays = document.createElement('div');
    weekdays.className = 'dm-dt-cal-weekdays';
    // Locale-aware short weekday names, starting Sunday (matches CalendarView's default).
    const dayFmt = new Intl.DateTimeFormat(undefined, { weekday: 'narrow' });
    for (let i = 0; i < 7; i++) {
      const sample = new Date(2024, 0, 7 + i);  // 7 Jan 2024 was a Sunday
      const el = document.createElement('span');
      el.textContent = dayFmt.format(sample);
      weekdays.appendChild(el);
    }
    host.appendChild(weekdays);

    const grid = document.createElement('div');
    grid.className = 'dm-dt-cal-grid';
    grid.dataset.mode = 'days';
    host.appendChild(grid);

    if (includeTime) {
      const goTime = document.createElement('button');
      goTime.className = 'dm-dt-go-time';
      goTime.type = 'button';
      // FA clock () wrapped in .icon → font-family FontAwesome via the
      // CSS rule. Text outside the span uses the system font.
      goTime.innerHTML = '<span class="icon"><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><polyline points="12 6 12 12 16 14"/></svg></span> Set time';
      goTime.addEventListener('click', e => { e.stopPropagation(); onGoTime(); });
      host.appendChild(goTime);
    }

    month.addEventListener('click', e => {
      e.stopPropagation();
      // Zoom out one level. 'years' stays at 'years' (top of the stack).
      if (state.viewMode === 'days') state.viewMode = 'months';
      else if (state.viewMode === 'months') state.viewMode = 'years';
      renderCalendarGrid(host, state, onChange);
    });

    prev.addEventListener('click', e => {
      e.stopPropagation();
      stepView(state, -1);
      renderCalendarGrid(host, state, onChange);
    });
    next.addEventListener('click', e => {
      e.stopPropagation();
      stepView(state, +1);
      renderCalendarGrid(host, state, onChange);
    });
  }

  /**
   * Prev/next button step. The unit depends on the current zoom level:
   * one month in days view, one year in months view, one decade in years
   * view. State's view{Year,Month} update accordingly.
   */
  function stepView(state, delta) {
    if (state.viewMode === 'days') {
      state.viewMonth += delta;
      if (state.viewMonth < 0)  { state.viewMonth = 11; state.viewYear--; }
      if (state.viewMonth > 11) { state.viewMonth = 0;  state.viewYear++; }
    } else if (state.viewMode === 'months') {
      state.viewYear += delta;
    } else {
      state.viewYear += delta * 10;
    }
  }

  function renderCalendarGrid(host, state, onChange) {
    // Dispatch by zoom level. State's viewMode controls whether the grid
    // shows individual days, the 12 months of a year, or a decade of
    // years. The header label + navigation step adjust to match.
    const grid = host.querySelector('.dm-dt-cal-grid');
    const weekdays = host.querySelector('.dm-dt-cal-weekdays');
    grid.dataset.mode = state.viewMode;
    if (weekdays) weekdays.style.display = state.viewMode === 'days' ? '' : 'none';

    if (state.viewMode === 'months') return renderMonthsGrid(host, state, onChange);
    if (state.viewMode === 'years')  return renderYearsGrid(host, state, onChange);

    // ── Days view ────────────────────────────────────────────────
    const monthFmt = new Intl.DateTimeFormat(undefined, { month: 'long', year: 'numeric' });
    host.querySelector('.dm-dt-cal-month').textContent =
      monthFmt.format(new Date(state.viewYear, state.viewMonth, 1));
    grid.innerHTML = '';

    const firstOfMonth = new Date(state.viewYear, state.viewMonth, 1);
    const startWeekday = firstOfMonth.getDay();   // 0=Sun
    const daysInMonth  = new Date(state.viewYear, state.viewMonth + 1, 0).getDate();
    const today = new Date();
    today.setHours(0, 0, 0, 0);

    // Pad the front with the trailing days of the previous month so the
    // first row aligns under the correct weekday column. 6 weeks x 7 days
    // = 42 cells covers any month layout.
    for (let i = 0; i < 42; i++) {
      const dayNum = i - startWeekday + 1;
      const cell = document.createElement('button');
      cell.type = 'button';
      cell.className = 'dm-dt-day';
      const cellDate = new Date(state.viewYear, state.viewMonth, dayNum);
      if (dayNum < 1 || dayNum > daysInMonth) cell.classList.add('dm-other-month');
      cell.textContent = cellDate.getDate();
      if (cellDate.getTime() === today.getTime()) cell.classList.add('dm-today');
      if (state.date && sameDay(cellDate, state.date)) cell.classList.add('dm-selected');
      // Min/Max gate — schema's DateOptions.Min/Max disable cells outside
      // the allowed range. Disabled cells stay visible (so the user can see
      // the calendar layout) but don't accept clicks.
      if (outOfRange(cellDate, state.minDate, state.maxDate)) {
        cell.disabled = true;
        cell.classList.add('dm-out-of-range');
      } else {
        cell.addEventListener('click', e => {
          e.stopPropagation();
          state.date = cellDate;
          state.viewMonth = cellDate.getMonth();
          state.viewYear  = cellDate.getFullYear();
          renderCalendarGrid(host, state, onChange);
          onChange();
        });
      }
      grid.appendChild(cell);
    }
  }

  // Months zoom: header shows the year, body shows 12 month buttons. Click
  // drills back to days view at the chosen month.
  function renderMonthsGrid(host, state, onChange) {
    host.querySelector('.dm-dt-cal-month').textContent = String(state.viewYear);
    const grid = host.querySelector('.dm-dt-cal-grid');
    grid.innerHTML = '';

    const monthFmt = new Intl.DateTimeFormat(undefined, { month: 'short' });
    const today = new Date();
    for (let m = 0; m < 12; m++) {
      const cell = document.createElement('button');
      cell.type = 'button';
      cell.className = 'dm-dt-day';
      cell.textContent = monthFmt.format(new Date(state.viewYear, m, 1));
      if (state.viewYear === today.getFullYear() && m === today.getMonth())
        cell.classList.add('dm-today');
      if (state.date && state.date.getFullYear() === state.viewYear && state.date.getMonth() === m)
        cell.classList.add('dm-selected');
      // Disable an entire month if every day in it is out of range.
      const monthStart = new Date(state.viewYear, m, 1);
      const monthEnd   = new Date(state.viewYear, m + 1, 0);
      if (outOfRange(monthStart, null, state.maxDate) ||
          outOfRange(monthEnd,   state.minDate, null)) {
        cell.disabled = true;
        cell.classList.add('dm-out-of-range');
      } else {
        cell.addEventListener('click', e => {
          e.stopPropagation();
          state.viewMonth = m;
          state.viewMode = 'days';
          renderCalendarGrid(host, state, onChange);
        });
      }
      grid.appendChild(cell);
    }
  }

  // Years zoom: header shows the decade range, body shows 12 cells = 10
  // years of the decade plus one boundary year either side (rendered as
  // .dm-other-month for the dim style). Click drills back to months view.
  function renderYearsGrid(host, state, onChange) {
    const decadeStart = Math.floor(state.viewYear / 10) * 10;
    host.querySelector('.dm-dt-cal-month').textContent =
      decadeStart + ' - ' + (decadeStart + 9);
    const grid = host.querySelector('.dm-dt-cal-grid');
    grid.innerHTML = '';

    const today = new Date();
    for (let i = -1; i <= 10; i++) {
      const year = decadeStart + i;
      const cell = document.createElement('button');
      cell.type = 'button';
      cell.className = 'dm-dt-day';
      cell.textContent = String(year);
      if (i < 0 || i >= 10) cell.classList.add('dm-other-month');
      if (year === today.getFullYear()) cell.classList.add('dm-today');
      if (state.date && state.date.getFullYear() === year) cell.classList.add('dm-selected');
      // Disable a year if its entire span is out of range.
      const yearStart = new Date(year, 0, 1);
      const yearEnd   = new Date(year, 11, 31);
      if (outOfRange(yearStart, null, state.maxDate) ||
          outOfRange(yearEnd,   state.minDate, null)) {
        cell.disabled = true;
        cell.classList.add('dm-out-of-range');
      } else {
        cell.addEventListener('click', e => {
          e.stopPropagation();
          state.viewYear = year;
          state.viewMode = 'months';
          renderCalendarGrid(host, state, onChange);
        });
      }
      grid.appendChild(cell);
    }
  }

  function sameDay(a, b) {
    return a.getFullYear() === b.getFullYear()
        && a.getMonth()    === b.getMonth()
        && a.getDate()     === b.getDate();
  }

  // ── Time page ────────────────────────────────────────────────
  function buildTimePage(host, state, onChange, onGoCal) {
    const goCal = document.createElement('button');
    goCal.className = 'dm-dt-go-cal';
    goCal.type = 'button';
    goCal.innerHTML = '<span class="icon"><svg viewBox="0 0 24 24" width="14" height="14" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="3" y="4" width="18" height="18" rx="2" ry="2"/><line x1="16" y1="2" x2="16" y2="6"/><line x1="8" y1="2" x2="8" y2="6"/><line x1="3" y1="10" x2="21" y2="10"/></svg></span> Pick date';
    goCal.addEventListener('click', e => { e.stopPropagation(); onGoCal(); });
    host.appendChild(goCal);

    const cluster = document.createElement('div');
    cluster.className = 'dm-dt-time-cluster';

    const hourCol   = makeTimeCol('hour',   state, onChange);
    const colon     = document.createElement('span'); colon.className = 'dm-dt-colon'; colon.textContent = ':';
    const minuteCol = makeTimeCol('minute', state, onChange);
    cluster.appendChild(hourCol);
    cluster.appendChild(colon);
    cluster.appendChild(minuteCol);

    if (state.hour12) {
      const ampm = document.createElement('button');
      ampm.className = 'dm-dt-ampm';
      ampm.type = 'button';
      ampm.dataset.role = 'ampm';
      ampm.addEventListener('click', e => {
        e.stopPropagation();
        // Flip across the AM↔PM boundary by ±12 hours; minute unchanged.
        state.hours = state.hours >= 12 ? state.hours - 12 : state.hours + 12;
        renderTimeBoxes(host, state);
        onChange();
      });
      cluster.appendChild(ampm);
    }
    host.appendChild(cluster);
  }

  function makeTimeCol(kind, state, onChange) {
    const col  = document.createElement('div');
    col.className = 'dm-dt-time-col';
    const up   = document.createElement('button'); up.className   = 'dm-dt-spin-btn'; up.type   = 'button'; up.textContent   = '▲';   // ▲
    const inp  = document.createElement('input');  inp.className  = 'dm-dt-time-input'; inp.type = 'text'; inp.maxLength = 2; inp.inputMode = 'numeric';
    const down = document.createElement('button'); down.className = 'dm-dt-spin-btn'; down.type = 'button'; down.textContent = '▼';   // ▼
    inp.dataset.role = kind;
    col.appendChild(up); col.appendChild(inp); col.appendChild(down);

    up.addEventListener('click',   e => { e.stopPropagation(); step(kind, +1, state); paintCol(inp, kind, state); onChange(); });
    down.addEventListener('click', e => { e.stopPropagation(); step(kind, -1, state); paintCol(inp, kind, state); onChange(); });
    inp.addEventListener('change', e => {
      e.stopPropagation();
      const raw = parseInt(inp.value, 10);
      if (Number.isNaN(raw)) { paintCol(inp, kind, state); return; }
      if (kind === 'hour') {
        if (state.hour12) {
          if (raw < 1 || raw > 12) { paintCol(inp, kind, state); return; }
          state.hours = (raw % 12) + (state.hours >= 12 ? 12 : 0);
        } else {
          if (raw < 0 || raw > 23) { paintCol(inp, kind, state); return; }
          state.hours = raw;
        }
      } else {
        if (raw < 0 || raw > 59) { paintCol(inp, kind, state); return; }
        state.minutes = raw;
      }
      paintCol(inp, kind, state);
      onChange();
    });
    return col;
  }

  function step(kind, delta, state) {
    if (kind === 'hour') {
      if (state.hour12) {
        // Wrap *within* the AM/PM half — only the explicit AM/PM toggle
        // crosses the boundary. (Tempus Dominus convention.)
        const isPm = state.hours >= 12;
        const h12 = ((state.hours + 11) % 12) + 1;            // 1..12
        const newH12 = ((h12 - 1 + 12 + delta) % 12) + 1;
        state.hours = (newH12 % 12) + (isPm ? 12 : 0);
      } else {
        state.hours = (state.hours + 24 + delta) % 24;
      }
    } else {
      state.minutes = (state.minutes + 60 + delta) % 60;
    }
  }

  function paintCol(inp, kind, state) {
    if (kind === 'hour') {
      const h = state.hour12 ? (((state.hours + 11) % 12) + 1) : state.hours;
      inp.value = String(h).padStart(2, '0');
    } else {
      inp.value = String(state.minutes).padStart(2, '0');
    }
  }

  function renderTimeBoxes(host, state) {
    const hourInp   = host.querySelector('input[data-role="hour"]');
    const minuteInp = host.querySelector('input[data-role="minute"]');
    const ampmBtn   = host.querySelector('button[data-role="ampm"]');
    if (hourInp)   paintCol(hourInp,   'hour',   state);
    if (minuteInp) paintCol(minuteInp, 'minute', state);
    if (ampmBtn)   ampmBtn.textContent = state.hours >= 12 ? 'PM' : 'AM';
  }

  function readValue(el, f) {
    if (!el) return null;
    const k = (f.kind || '').toLowerCase();
    if (k === 'boolean') return el.checked;
    if (k === 'scale') { const v = el.value; return (v === '' || v == null) ? null : Number(v); }
    if (k === 'multi-choice') {
      return Array.from(el.querySelectorAll('input[type="checkbox"]'))
        .filter(c => c.checked)
        .map(c => c.dataset.optValue);
    }
    if (k === 'geo') {
      // Scoped to .dm-geo-coords so the new address-search <input> in
      // .dm-geo-addr-row doesn't shift the indices (would otherwise
      // grab [addr, lat] instead of [lat, lng]).
      const [lat, lng] = el.querySelectorAll('.dm-geo-coords input');
      const la = parseFloat(lat.value);
      const ln = parseFloat(lng.value);
      // Schema's GeoJsonConverter requires both lat AND lng — partial entries
      // would throw on submit. Treat partial as null so the user has to commit
      // both halves; matches the converter's "incomplete = malformed" stance.
      if (!Number.isFinite(la) || !Number.isFinite(ln)) return null;
      const fa = el._dmFormattedAddress;
      return fa ? { lat: la, lng: ln, formattedAddress: fa } : { lat: la, lng: ln };
    }
    if (k === 'list') {
      return Array.from(el.querySelectorAll('.dm-chip'))
        .map(c => c.dataset.value);
    }
    if (k === 'image' || k === 'attachment') {
      // Typed object shape — see ImageRef.cs / AttachmentRef.cs converters.
      // Both accept a legacy bare-string data URI on read, but writes always
      // emit the object form, so we always return an object (or null).
      return el._dmValue ?? null;
    }
    if (k === 'signature' || k === 'initials') {
      // SignatureRef object shape ({dataUri, mime, sizeBytes}) — see SignatureRef.cs.
      return el._dmValue ?? null;
    }
    if (k === 'date' || k === 'datetime') {
      // Custom Tempus-Dominus picker stashes the canonical ISO string on
      // _dmValue (yyyy-MM-dd or yyyy-MM-ddTHH:mm:ss±HH:MM). Empty until the
      // user picks a date — return null so validation treats it as missing.
      return el._dmValue ?? null;
    }
    if (k === 'number' || k === 'decimal' || k === 'money') {
      // Money wraps its input in a `.dm-money` div with a currency-suffix span;
      // `_dmRawInput` points to the inner <input>. Number/decimal still get
      // the bare input as `el`.
      const inp = el && el._dmRawInput ? el._dmRawInput : el;
      if (!inp || !('value' in inp)) return null;
      // Format-on-blur stores the canonical raw string in `dataset.raw` so
      // we don't have to parse a grouped/formatted display value on every read.
      const raw = inp.dataset && inp.dataset.raw != null && inp.dataset.raw !== ''
        ? inp.dataset.raw
        : inp.value;
      if (raw === '' || raw == null) return null;
      const n = parseFloat(raw);
      return Number.isFinite(n) ? n : null;
    }
    return el.value;
  }

  /// Inverse of `readValue` — seeds a rendered field's DOM with a value
  /// from a saved-submission round-trip (edit flow). Mirrors the kind
  /// branches in readValue so what we wrote on submit can be restored on
  /// the next visit. Complex pickers (date, image, attachment) restore
  /// the canonical value model (`_dmValue`) but UI previews that depend
  /// on the picker re-rendering may need an extra prod via the renderer's
  /// own evaluateAll pass (called after hydration).
  function setValue(el, f, value) {
    if (!el || value == null) return;
    const k = (f.kind || '').toLowerCase();
    if (k === 'boolean') { el.checked = !!value; return; }
    if (k === 'scale') { el.value = (value == null ? '' : value); return; }
    if (k === 'multi-choice') {
      const arr = Array.isArray(value) ? value.map(String) : [];
      el.querySelectorAll('input[type="checkbox"]').forEach(cb => {
        cb.checked = arr.indexOf(cb.dataset.optValue) >= 0;
      });
      return;
    }
    if (k === 'geo' && typeof value === 'object') {
      // Scoped — see readValue note above.
      const inputs = el.querySelectorAll('.dm-geo-coords input');
      if (inputs.length >= 2) {
        inputs[0].value = (value.lat  != null) ? String(value.lat)  : '';
        inputs[1].value = (value.lng  != null) ? String(value.lng)  : '';
      }
      const addr = el.querySelector('.dm-geo-addr');
      if (value.formattedAddress) {
        el._dmFormattedAddress = value.formattedAddress;
        if (addr) addr.value = value.formattedAddress;
      }
      return;
    }
    if (k === 'list' && Array.isArray(value)) {
      // List builds chips on Enter — rebuild the chip strip directly so
      // both the visual and the readValue path agree post-hydration.
      const strip = el.querySelector('.dm-chip-strip') || el;
      strip.querySelectorAll('.dm-chip').forEach(n => n.remove());
      for (const item of value) {
        const chip = document.createElement('span');
        chip.className = 'dm-chip';
        chip.dataset.value = String(item);
        chip.textContent = String(item);
        strip.appendChild(chip);
      }
      return;
    }
    if (k === 'date' || k === 'datetime') {
      // Picker stores the canonical ISO string on `_dmValue` — readValue
      // (and validation) read from there, so seeding it is enough for a
      // correct submit. The picker's visible display catches up the
      // first time the user opens it.
      el._dmValue = typeof value === 'string' ? value : null;
      return;
    }
    if (k === 'image' || k === 'attachment') {
      el._dmValue = value;
      return;
    }
    if (k === 'number' || k === 'decimal' || k === 'money') {
      const raw = el._dmRawInput || el;
      raw.value = String(value);
      if (raw.dataset) raw.dataset.raw = String(value);
      return;
    }
    // text / long-text / rich-text / email / url / phone / choice (select or radio fallback)
    if ('value' in el) el.value = String(value);
  }

  // ─── Schema buttons + runtime styling ────────────────────────

  /// Place the form-level issues banner directly below the submit
  /// affordance. Preference order:
  ///   1. Auto submit row (`.dm-submit-row`) — inserted as its next sibling.
  ///   2. Schema ButtonColumn whose Action is Submit or Save — banner goes
  ///      after the closest enclosing `.dm-row` so it spans the layout width
  ///      instead of sitting in the same grid cell as the button.
  ///   3. Any schema ButtonColumn — same row-anchored insert.
  ///   4. Fallback: append at the end of root.
  function insertBannerAfterSubmit(root, banner) {
    const autoRow = root.querySelector('.dm-submit-row');
    if (autoRow) { autoRow.after(banner); return; }
    const submitBtn = root.querySelector('.dm-btn[data-action="Submit"], .dm-btn[data-action="Save"]')
                    || root.querySelector('.dm-btn');
    if (submitBtn) {
      const row = submitBtn.closest('.dm-row') || submitBtn.parentElement;
      if (row && row.parentNode) { row.after(banner); return; }
    }
    root.appendChild(banner);
  }

  /// True iff the form schema declares at least one ButtonColumn anywhere
  /// in its layout. When true, the renderer skips its auto-injected Submit
  /// row — the author owns the action surface.
  // Wizard nav actions the author already placed in a step (lowercased set of
  // 'prevstep' / 'nextstep' / 'submit'), so the auto Back/Next/Submit can skip
  // the ones they own. Mirrors the Uno renderer's NavActionsInStep.
  function stepNavActions(stepIndex) {
    const set = new Set();
    const step = (form.steps || [])[stepIndex];
    if (!step) return set;
    const scan = (row) => {
      for (const col of (row.columns || [])) {
        const k = (col.kind || col.Kind || '').toLowerCase();
        if (k === 'button') {
          const a = (col.action || col.Action || 'None').toLowerCase();
          if (a === 'prevstep' || a === 'nextstep' || a === 'submit') set.add(a);
        } else if (k === 'group') {
          for (const r of (col.rows || [])) scan(r);
        }
      }
    };
    for (const sec of (step.sections || []))
      for (const row of (sec.rows || [])) scan(row);
    return set;
  }

  function hasSchemaButton(f) {
    for (const step of (f.steps || []))
      for (const sec of (step.sections || []))
        for (const row of (sec.rows || []))
          if (rowHasButton(row)) return true;
    return false;
  }
  function rowHasButton(row) {
    for (const col of (row.columns || [])) {
      const k = (col.kind || col.Kind || '').toLowerCase();
      if (k === 'button') return true;
      if (k === 'group')
        for (const r of (col.rows || []))
          if (rowHasButton(r)) return true;
    }
    return false;
  }

  /// Append a single <style> element with the palette CSS-vars block plus
  /// synthesised :hover / :active rules for every button whose hover/pressed
  /// state was emitted by FormBundleBuilder. CSS-rule-keyed pseudo-class
  /// styling can't live on an inline `style="..."` attribute, so we have to
  /// build a <style> element on the fly.
  function installRuntimeStyles(palette, elCss) {
    const parts = [];
    if (palette) parts.push(palette);
    for (const key in elCss) {
      const m = /^button\/([^/]+)\/(hover|pressed)$/.exec(key);
      if (!m) continue;
      const sel = '.dm-btn[data-col-id="' + cssEscape(m[1]) + '"]:' +
                  (m[2] === 'hover' ? 'hover' : 'active');
      // The base button style is applied as an inline `style="..."`
      // attribute via applyElementCss — inline styles always beat
      // selector-matched rules regardless of specificity. Stamp
      // !important on every declaration in the hover/pressed body so
      // these CSS rules actually override the inline base.
      parts.push(sel + '{' + bangify(elCss[key]) + '}');
    }
    if (!parts.length) return;
    const el = document.createElement('style');
    el.setAttribute('data-dm-runtime', 'true');
    el.textContent = parts.join('\n');
    document.head.appendChild(el);
  }

  /// Append `!important` to every declaration in a semicolon-separated
  /// CSS body. Used by installRuntimeStyles so hover/pressed beat inline.
  function bangify(css) {
    return (css || '')
      .split(';')
      .map(d => d.trim())
      .filter(Boolean)
      .map(d => d + ' !important')
      .join(';') + ';';
  }
  function cssEscape(s) {
    // Only used on bundle-supplied ids (GUIDs / slugs), but be defensive.
    return String(s).replace(/[^A-Za-z0-9_-]/g, '\\$&');
  }

  /// Submit/Save/Reset/None dispatcher shared by the auto submit row and
  /// every schema-declared ButtonColumn. <paramref name="dm-col"/> is the
  /// schema record for schema buttons, null for the auto row. Status sink
  /// is optional — if present, a one-line status string is written to it.
  function runSchemaAction(action, col, statusSink) {
    const a = (action || 'none').toLowerCase();

    // Wizard step navigation — author-placed Next/Prev buttons drive the same
    // nav the auto buttons do. Handled up front: PrevStep must NOT gate on
    // validation (matches the auto Back); NextStep validates via goToStep.
    if (a === 'nextstep') {
      if ((wizardStepEls || []).length > 1) goToStep(wizardCurrent + 1);
      if (statusSink) statusSink.textContent = '';
      return;
    }
    if (a === 'prevstep') {
      if (wizardCurrent > 0) { validationCtx = null; showStep(wizardCurrent - 1); evaluateAll(); }
      if (statusSink) statusSink.textContent = '';
      return;
    }
    // Storage v2: block submit/save while any image/attachment field is
    // still uploading bytes to its pre-signed S3 slot. Without this the
    // submission would carry a half-populated value (or null) and the
    // receiver would store an invalid record. Reset is allowed during
    // upload — clearing the field implicitly cancels the in-flight PUT
    // (the orphan blob gets cleaned by the bucket's pending/* lifecycle
    // rule).
    if (a === 'submit' || a === 'save') {
      const fields = (form.fields || []);
      for (const f of fields) {
        const el = fieldInputEls[f.id];
        if (el && el._dmUploading) {
          if (statusSink) statusSink.textContent = t('still_uploading', 'Still uploading attachments — try again in a moment.');
          return;
        }
      }
    }
    if (a === 'reset') {
      for (const f of (form.fields || [])) {
        const def = f.defaultValue;
        values[f.name] = def == null ? '' : def;
        const inputEl = fieldInputEls[f.id];
        const target = inputEl && inputEl._dmRawInput ? inputEl._dmRawInput : inputEl;
        if (target && 'value' in target) target.value = (def == null ? '' : def);
        touched[f.name] = false;
      }
      evaluateAll();
      hooks.onReset({ form, col });
      if (statusSink) statusSink.textContent = '';
      return;
    }
    // Re-pull every field straight from its DOM before we evaluate.
    // Composite controls (list chips, multi-choice, image picker, etc.)
    // dispatch their state-change events with `bubbles:false`, so the
    // wrap-level 'input'/'change' listeners that normally seed the
    // `values` bag can miss the latest entry — typing a tag + pressing
    // Enter would leave `values["tags"]` stale. Re-reading here is cheap
    // and bullet-proof.
    for (const f of (form.fields || [])) {
      const el = fieldInputEls[f.id];
      if (!el) continue;
      values[f.name] = readValue(el, f);
      touched[f.name] = true;
    }
    if (a === 'submit' || a === 'save') validationCtx = 'submit';
    evaluateAll();
    const firstInvalid = root.querySelector('.dm-field.dm-invalid');
    if (firstInvalid) {
      // The boxed form-level banner (shown by evaluateAll) carries the message —
      // no inline duplicate here.
      // A11y: move keyboard focus + scroll to the first invalid input so
      // screen-reader and keyboard users land at the actual problem,
      // not the unchanged submit button. Prefer the registered input
      // (handles composite wraps that aren't focusable themselves).
      const fid = firstInvalid.dataset.fieldId;
      const inp = (fid && fieldInputEls[fid]) || firstInvalid.querySelector('input, select, textarea, button');
      const focusTarget = inp && inp._dmRawInput ? inp._dmRawInput : inp;
      if (focusTarget && typeof focusTarget.focus === 'function') {
        try { focusTarget.focus({ preventScroll: false }); } catch (_) { focusTarget.focus(); }
      }
      try { firstInvalid.scrollIntoView({ behavior: 'smooth', block: 'center' }); } catch (_) {}
      return;
    }
    const payload = { form, col, values: { ...values } };
    if      (a === 'submit') hooks.onSubmit(payload);
    else if (a === 'save')   hooks.onSave(payload);
    else                      hooks.onAction(Object.assign(payload, { name: col ? col.name : null }));
    if (statusSink) statusSink.textContent = '';
  }

  // ─── Eval pipeline ───────────────────────────────────────────

  function onValueChanged(f, newValue, isTouchEvent) {
    values[f.name] = newValue;
    if (isTouchEvent) touched[f.name] = true;
    evaluateAll();
  }

  function evaluateAll() {
    // 1. Column-level visibility — groups + decorative columns. Walk these
    //    first so a field's ancestor-hidden test in the validation phase
    //    sees the latest hidden state.
    for (const key in columnEls) {
      // key is 'group/<id>' / 'richtext/<id>' / etc.
      // The compiled function is registered under '<base>/<id>/visibleWhen'
      // on the bundle's compiled map (group case has 'groups/<id>/...').
      const [base, id] = key.split('/', 2);
      const exprKey = (base === 'group' ? 'groups/' : base + '/') + id + '/visibleWhen';
      const fn = fns[exprKey];
      const el = columnEls[key];
      if (fn === undefined) { el.hidden = false; continue; }
      if (fn === null) { el.hidden = false; continue; }  // server-only — fail open
      try { el.hidden = !fn(values); }
      catch (e) { el.hidden = false; }
    }

    // 2. Field-level VisibleWhen — own expression only; ancestor cascade is
    //    handled by walking up the DOM ancestors below.
    for (const f of (form.fields || [])) {
      const key = 'fields/' + f.id + '/visibleWhen';
      const fn = fns[key];
      const wrap = fieldEls[f.id];
      if (!wrap) continue;
      if (fn === undefined) { wrap.hidden = false; continue; }
      let visible = true;
      if (fn === null) {
        visible = true;
        ensureServerOnlyHint(wrap);
      } else {
        try { visible = !!fn(values); }
        catch (e) { visible = true; /* fail open */ }
      }
      wrap.hidden = !visible;
    }

    // 3. Calculated expressions — write the resolved value into the input
    for (const f of (form.fields || [])) {
      const key = 'fields/' + f.id + '/calculated';
      const fn = fns[key];
      if (!fn) continue;
      try {
        const val = fn(values);
        const inputEl = fieldInputEls[f.id];
        // For money, the registered element is the `.dm-money` wrapper — the
        // actual input lives at `_dmRawInput`. Resolving here keeps every
        // other branch unchanged.
        const target = inputEl && inputEl._dmRawInput ? inputEl._dmRawInput : inputEl;
        // type=number inputs reject locale-formatted strings ("0,00" →
        // "value cannot be parsed"). Branch on the input's actual type:
        // number → write the JS number raw (browser displays per locale);
        // text (format-on-blur path) → use the locale-formatted display.
        let display;
        if (target && target.tagName === 'INPUT' && target.type === 'number') {
          display = (val == null) ? '' : String(val);
        } else {
          display = formatCalculatedValue(val, f);
        }
        if (target && 'value' in target && target.value !== display) {
          target.value = display;
          target.disabled = true;   // calculated fields aren't user-editable
          values[f.name] = val;
        }
      } catch (e) { /* ignore */ }
    }

    // 4. Validation. Errors only render for fields the user has touched
    //    (blur or change), AND that aren't hidden by their own VisibleWhen
    //    OR any ancestor group's VisibleWhen — the cascade matches
    //    FormEvaluator.IsFieldVisible on the Uno side, so a group hidden by
    //    its expression also skips validating every nested field.
    for (const f of (form.fields || [])) {
      const wrap = fieldEls[f.id];
      if (!wrap) continue;
      const errEl = wrap.querySelector('.dm-err');
      const inputEl = fieldInputEls[f.id];
      const hidden = wrap.hidden || hasHiddenAncestorGroup(wrap);
      const error  = (!hidden && touched[f.name]) ? validate(f, values[f.name]) : null;
      if (error) {
        wrap.classList.add('dm-invalid');
        errEl.textContent = error;
        if (inputEl) inputEl.setAttribute('aria-invalid', 'true');
      } else {
        wrap.classList.remove('dm-invalid');
        errEl.textContent = '';
        if (inputEl) inputEl.removeAttribute('aria-invalid');
      }
    }

    // 5. Form-level issues banner. Visible whenever any touched field is
    //    currently invalid. Text is sourced from the form schema's
    //    Messages["validationBanner"] slot so authors / WP admins can
    //    translate it; falls back to the engine default English.
    const banner = root.querySelector('.dm-form-issues');
    if (banner) {
      const anyError = root.querySelectorAll('.dm-field.dm-invalid').length > 0;
      // Only after a Next/Submit attempt (validationCtx) — never on plain blur —
      // and the SAME boxed banner carries the step or the submit message.
      banner.hidden = !(validationCtx && anyError);
      if (validationCtx && anyError) {
        const text = banner.querySelector('.dm-form-issues-text');
        if (text) {
          if (validationCtx === 'step') {
            text.textContent = t('please_fix_step', 'Please complete the required fields on this step.');
          } else {
            const custom = form.messages && form.messages.validationBanner;
            text.textContent = (typeof custom === 'string' && custom.trim().length > 0)
              ? custom
              : t('validation_banner_default', 'Please fix the highlighted fields before submitting.');
          }
        }
      }
    }
  }

  /// Walk up the DOM tree from a field wrapper, returning true if any
  /// ancestor `.dm-group` element is hidden. This is how the field-validation
  /// cascade implements "dm-field inside hidden dm-group skips validation"
  /// without needing a precomputed ancestor map: the DOM already encodes
  /// the layout tree.
  function hasHiddenAncestorGroup(wrap) {
    for (let n = wrap.parentNode; n && n !== document.body; n = n.parentNode) {
      if (n.classList && n.classList.contains('dm-group') && n.hidden) return true;
    }
    return false;
  }

  function validate(f, val) {
    // Iterate every rule in declared order. Discriminators MUST match the
    // schema's $kind values exactly (see ValidationRule.cs:11-17) — the
    // earlier implementation lowercased everything and mismatched property
    // names, which silently dropped almost every user-authored rule.
    const isEmpty = val == null || val === '' ||
                    (Array.isArray(val) && val.length === 0);

    // The boolean .required flag is a shorthand for "dm-field has a RequiredRule".
    // We honour both: the flag triggers the field's customizable "required"
    // message (engine default "Required"); an explicit RequiredRule below
    // can override it with its own .message.
    if (f.required && isEmpty) return msg(f, 'required', 'Required');

    // Kind-driven intrinsic checks (email/url/phone/date/choice membership
    // etc.) — port of DataMaker.Schema.Validation.IntrinsicValidators. Run
    // BEFORE user rules so the error surfaces a more specific message
    // before a generic pattern-rule complaint kicks in.
    const intrinsic = intrinsicError(f, val);
    if (intrinsic) return intrinsic;

    for (let i = 0; i < (f.validation || []).length; i++) {
      const rule = f.validation[i];
      const kind = rule.$kind || rule.kind || '';

      // ValidationRule.When — rule applies only when its boolean expression
      // evaluates true. A null compiled fn means JsCompiler couldn't port it
      // to JS; fail open (apply the rule) rather than silently skipping —
      // server-side will catch real failures.
      const whenFn = fns['fields/' + f.id + '/rules/' + i + '/when'];
      if (whenFn !== undefined && whenFn !== null) {
        try { if (!whenFn(values)) continue; }
        catch (e) { /* eval error — fail open, apply the rule */ }
      }

      switch (kind) {
        case 'required':
          if (isEmpty) return rule.message || 'Required';
          break;
        case 'pattern':
          if (val != null && val !== '' && rule.regex) {
            try {
              if (!new RegExp(rule.regex).test(String(val)))
                return rule.message || 'Invalid format';
            } catch (e) { /* malformed regex — skip */ }
          }
          break;
        case 'minLength':
          if (val != null && String(val).length < (rule.length || 0))
            return rule.message || 'Too short';
          break;
        case 'maxLength':
          if (val != null && String(val).length > (rule.length || 0))
            return rule.message || 'Too long';
          break;
        case 'min':
          if (typeof val === 'number' && val < Number(rule.value))
            return rule.message || 'Too small';
          break;
        case 'max':
          if (typeof val === 'number' && val > Number(rule.value))
            return rule.message || 'Too large';
          break;
        case 'expression': {
          const exprFn = fns['fields/' + f.id + '/rules/' + i + '/expression'];
          if (exprFn === undefined || exprFn === null) break;  // not portable → server-only
          try {
            if (!exprFn(values)) return rule.message || 'Invalid';
          } catch (e) { /* eval error → treat as invalid? no, fail open */ }
          break;
        }
      }
    }
    return null;
  }

  // ─── Intrinsic validators ────────────────────────────────────
  // Port of DataMaker.Schema.Validation.IntrinsicValidators. Server runs
  // the C# version on save; this mirror gives the user immediate feedback
  // in the browser. Empty/null values pass silently — a missing-required
  // is the boolean .required flag's job, not the kind-driven check.

  // Look up a customizable error message: per-field override on `f.messages`
  // (slot id → text) wins, otherwise the engine default. Empty/whitespace
  // override falls through so a blanked entry doesn't silently swallow the
  // error.
  function msg(f, slotId, defaultMsg) {
    const m = f && f.messages && f.messages[slotId];
    return (typeof m === 'string' && m.trim().length > 0) ? m : defaultMsg;
  }

  // NumberOptions.Min/Max bounds for number/decimal. Runs on the already-
  // parsed numeric value, so it enforces the same on both input paths — the
  // native type=number input AND the format-on-blur text input (which the
  // browser's min/max attributes never covered). `f.number` is absent for
  // money (it uses MoneyOptions, which has no bounds), so money is unaffected.
  function numberBoundError(f, val) {
    const n = f && f.number;
    if (!n) return null;
    if (typeof n.min === 'number' && val < n.min) return msg(f, 'number.min', `Must be at least ${n.min}.`);
    if (typeof n.max === 'number' && val > n.max) return msg(f, 'number.max', `Must be at most ${n.max}.`);
    return null;
  }

  function intrinsicError(f, val) {
    if (val == null || val === '') return null;

    const k = (f.kind || '').toLowerCase();

    // TextOptions (min/max length, pattern) — applies to every text-shaped
    // kind whose Text options block is set. Diverges from MinLengthRule
    // semantics on purpose: silent on empty values, kicks in only after
    // the user has typed something.
    if ((k === 'text' || k === 'long-text' || k === 'email' ||
         k === 'phone' || k === 'url') && f.text) {
      if (typeof val === 'string') {
        const t = f.text;
        if (typeof t.minLength === 'number' && t.minLength > 0 && val.length < t.minLength)
          return msg(f, 'text.minLength', `Must be at least ${t.minLength} character${t.minLength === 1 ? '' : 's'}.`);
        if (typeof t.maxLength === 'number' && t.maxLength > 0 && val.length > t.maxLength)
          return msg(f, 'text.maxLength', `Must be at most ${t.maxLength} character${t.maxLength === 1 ? '' : 's'}.`);
        if (t.pattern) {
          try { if (!new RegExp(t.pattern).test(val)) return msg(f, 'text.pattern', 'Value does not match the required pattern.'); }
          catch (e) { /* malformed pattern — skip */ }
        }
      }
    }

    switch (k) {
      case 'email':
        return typeof val === 'string' && !EMAIL_RX.test(val) ? msg(f, 'email', 'Not a valid email address.') : null;
      case 'phone':
        return typeof val === 'string' && !PHONE_RX.test(val) ? msg(f, 'phone', 'Not a valid phone number.') : null;
      case 'url': {
        if (typeof val !== 'string') return null;
        try {
          const u = new URL(val);
          if (u.protocol !== 'http:' && u.protocol !== 'https:') return msg(f, 'url', 'Not a valid URL.');
          return null;
        } catch (e) { return msg(f, 'url', 'Not a valid URL.'); }
      }
      case 'number':
        if (!(Number.isFinite(val) && Number.isInteger(val))) return msg(f, 'number', 'Not a whole number.');
        return numberBoundError(f, val);
      case 'decimal':
        if (!Number.isFinite(val)) return msg(f, 'decimal', 'Not a valid decimal number.');
        return numberBoundError(f, val);
      case 'money':
        if (!Number.isFinite(val)) return msg(f, 'money', 'Not a valid monetary amount.');
        return numberBoundError(f, val);
      case 'date':
        return typeof val === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(val) ? null : msg(f, 'date', 'Not a valid date.');
      case 'datetime':
        // <input type=datetime-local> emits 'YYYY-MM-DDTHH:MM' — Date()
        // accepts anything ISO-ish, so a parse-then-check round-trip
        // catches malformed values without a stricter regex.
        return typeof val === 'string' && !isNaN(Date.parse(val)) ? null : msg(f, 'datetime', 'Not a valid date-time.');
      case 'choice': {
        const opts = f.choice;
        if (!opts || !Array.isArray(opts.choices) || opts.choices.length === 0) return null;
        if (opts.allowCustom) return null;
        const allowed = opts.choices.map(c => c.value);
        return typeof val === 'string' && allowed.includes(val) ? null : msg(f, 'choice', 'Value is not in the allowed list.');
      }
      case 'multi-choice': {
        const opts = f.choice;
        if (!opts || !Array.isArray(opts.choices) || opts.choices.length === 0) return null;
        if (opts.allowCustom) return null;
        if (!Array.isArray(val)) return msg(f, 'multichoice', 'Some items are not in the allowed list.');
        const allowed = new Set(opts.choices.map(c => c.value));
        return val.every(v => allowed.has(v)) ? null : msg(f, 'multichoice', 'Some items are not in the allowed list.');
      }
      case 'geo': {
        if (typeof val !== 'object' || val === null) return null;
        if (typeof val.lat !== 'number' || val.lat < -90  || val.lat > 90)  return msg(f, 'geo.lat', 'Latitude must be between -90 and 90.');
        if (typeof val.lng !== 'number' || val.lng < -180 || val.lng > 180) return msg(f, 'geo.lng', 'Longitude must be between -180 and 180.');
        return null;
      }
      case 'image':
      case 'attachment': {
        const opts = f.attachment;
        const fileName = (val && typeof val === 'object') ? val.fileName : null;
        if (!fileName) return null;
        const acceptedExts = opts && Array.isArray(opts.acceptedExtensions) && opts.acceptedExtensions.length > 0
          ? opts.acceptedExtensions
          : (k === 'image' ? ['.jpg', '.jpeg', '.png', '.gif', '.webp', '.heic'] : null);
        if (acceptedExts) {
          const dot = fileName.lastIndexOf('.');
          const ext = dot >= 0 ? fileName.slice(dot).toLowerCase() : '';
          const ok = ext && acceptedExts.some(e => {
            const norm = (e.startsWith('.') ? e : '.' + e).toLowerCase();
            return ext === norm;
          });
          if (!ok) return msg(f, 'attachment.ext', 'File extension not allowed.');
        }
        // MaxSizeBytes — server-side enforced too; mirror as soft client check.
        if (opts && typeof opts.maxSizeBytes === 'number' && opts.maxSizeBytes > 0
            && typeof val.sizeBytes === 'number' && val.sizeBytes > opts.maxSizeBytes) {
          const mb = (opts.maxSizeBytes / (1024 * 1024)).toFixed(1);
          return `File exceeds ${mb} MB limit.`;
        }
        return null;
      }
    }
    return null;
  }

  // ─── Markdown rendering ──────────────────────────────────────
  // Tiny subset matching DataMaker.Schema.Layout.Markdown.cs in C# —
  // both implementations need to produce equivalent output so designer
  // preview and recipient view stay aligned. Out of scope: links,
  // images, blockquotes, raw HTML inlining.
  function renderMarkdown(md) {
    if (!md) return '';
    // Normalise CRLF + bare CR. WinUI TextBox emits CR-only when AcceptsReturn
    // is true, so without the second branch paragraph splitting fails.
    const src = md.replace(/\r\n|\r/g, '\n');
    const blocks = src.split('\n\n');
    const out = [];
    for (const raw of blocks) {
      const block = raw.replace(/^\n+|\n+$/g, '');
      if (!block) continue;
      out.push(blockToHtml(block));
    }
    return out.join('');
  }

  function blockToHtml(block) {
    const lines = block.split('\n');
    if (lines.length === 1) {
      const l = lines[0];
      if (l.startsWith('### ')) return '<h3>' + inline(l.slice(4)) + '</h3>';
      if (l.startsWith('## '))  return '<h2>' + inline(l.slice(3)) + '</h2>';
      if (l.startsWith('# '))   return '<h1>' + inline(l.slice(2)) + '</h1>';
    }
    if (lines.every(l => l.startsWith('- ') || l.startsWith('* '))) {
      return '<ul>' + lines.map(l => '<li>' + inline(l.slice(2)) + '</li>').join('') + '</ul>';
    }
    if (lines.every(l => /^\d+\.\s+/.test(l))) {
      return '<ol>' + lines.map(l => {
        const i = l.indexOf('.');
        return '<li>' + inline(l.slice(i + 1).replace(/^\s+/, '')) + '</li>';
      }).join('') + '</ol>';
    }
    return '<p>' + lines.map(l => inline(l)).join('<br>') + '</p>';
  }

  function inline(text) {
    // HTML-escape first so user-authored angle brackets stay literal.
    let out = text
      .replace(/&/g, '&amp;')
      .replace(/</g, '&lt;')
      .replace(/>/g, '&gt;');
    out = out.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>');
    out = out.replace(/(?<!\*)\*([^*]+)\*(?!\*)/g, '<em>$1</em>');
    out = out.replace(/_([^_]+)_/g, '<em>$1</em>');
    out = out.replace(/`([^`]+)`/g, '<code>$1</code>');
    return out;
  }

  function ensureServerOnlyHint(wrap) {
    if (wrap.querySelector('.dm-server-only-hint')) return;
    const chip = document.createElement('span');
    chip.className = 'dm-server-only-hint';
    chip.textContent = 'evaluated server-side';
    wrap.querySelector('.dm-label')?.appendChild(chip);
  }
}
