'use strict';

// Glue between the Data Maker web renderer (renderer.js) and this SDK's submit
// path. renderer.js calls hooks.onSubmit({ form, col, values }) when the user
// submits; createSubmitHandler returns a function with exactly that signature
// that seals the values and POSTs them to /submissions.
//
// The renderer's bundle.form carries the form id, schema version, and field
// schema, but NOT the recipient's public key — that lives in the .dmf manifest
// and is known to the host (the desktop/WP/embed page that published the form).
// So the host supplies { recipientPublicKey, recipientUserId } here; the rest
// is read off the form the renderer is already showing.

const { extractFields } = require('./dmf');
const { buildSubmission, postSubmission, DEFAULT_API_BASE_URL } = require('./submit');
const { ValidationError } = require('./errors');

// Push render-time options onto the renderer's global namespace before mount.
// Only `applyFormStyle` so far: false → renderer drops the .dmf's author design
// (palette + per-element CSS) and renders structure-only. No-op outside a
// browser (Node, tests) and a no-op when the option is left undefined so the
// renderer keeps its default (design on).
function setRenderOptions(opts = {}) {
  if (typeof window === 'undefined') return;
  const ns = (window.DataMakerRenderer = window.DataMakerRenderer || {});
  if (opts.applyFormStyle !== undefined) ns.applyFormStyle = opts.applyFormStyle;
}

// config:
//   recipientPublicKey  (required) — base64 X25519 key from the .dmf manifest
//   recipientUserId     (required) — form owner's userId from the manifest
//   apiBaseUrl          — defaults to the public endpoint
//   submitterId         — null (anonymous) unless the host knows the submitter
//   validate            — client-side validate/coerce before sealing (default true)
//   applyFieldErrors    — fn({ fieldName: message }) to surface validation issues
//                         (renderer.js exposes exactly this hook)
//   onSuccess(result)   — called with { submissionId, editToken } after a 2xx
//   onError(error)      — called on any non-validation failure (network, 4xx/5xx)
//   applyFormStyle      — when false, tells the renderer to skip the form
//                         author's design baked in the .dmf (palette + per-element
//                         CSS) and render structure-only so the host's own styles
//                         apply. Honored in the browser; a no-op under Node.
//
// Returns: async (payload) => result. `payload` is the renderer's
// { form, col, values }. Resolves to { ok, submissionId?, editToken?, issues?, error? }.
function createSubmitHandler(config = {}) {
  if (!config.recipientPublicKey || !config.recipientUserId) {
    throw new Error('createSubmitHandler needs recipientPublicKey and recipientUserId');
  }

  setRenderOptions({ applyFormStyle: config.applyFormStyle });

  return async function onSubmit(payload) {
    const form = (payload && payload.form) || {};
    const values = (payload && payload.values) || {};

    const descriptor = {
      formId: form.id,
      schemaVersion: form.schemaVersion != null ? form.schemaVersion : 1,
      recipientPublicKey: config.recipientPublicKey,
      recipientUserId: config.recipientUserId,
      fields: extractFields(form),
    };

    let envelope;
    try {
      ({ envelope } = await buildSubmission(descriptor, values, {
        validate: config.validate !== false,
        submitterId: config.submitterId ?? null,
      }));
    } catch (err) {
      if (err instanceof ValidationError) {
        const map = {};
        for (const i of err.issues) map[i.field] = i.message;
        if (typeof config.applyFieldErrors === 'function') config.applyFieldErrors(map);
        return { ok: false, issues: err.issues };
      }
      if (typeof config.onError === 'function') config.onError(err);
      return { ok: false, error: err };
    }

    try {
      const result = await postSubmission(envelope, {
        apiBaseUrl: config.apiBaseUrl || DEFAULT_API_BASE_URL,
        fetch: config.fetch,
      });
      if (typeof config.onSuccess === 'function') config.onSuccess(result);
      return { ok: true, ...result };
    } catch (err) {
      if (typeof config.onError === 'function') config.onError(err);
      return { ok: false, error: err };
    }
  };
}

module.exports = { createSubmitHandler, setRenderOptions };
