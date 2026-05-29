'use strict';

const { sealPayload } = require('./encrypt');
const { parseDmf } = require('./dmf');
const { validateValues } = require('./validate');
const { ValidationError, SubmissionError, DataMakerError } = require('./errors');

const DEFAULT_API_BASE_URL = 'https://datamaker-api.fobo-tools.com';

// RFC 4122 v4 with dashes stripped — matches the desktop's GUID.ToString("n")
// idempotency-key format every other submitter uses. Uses Web Crypto so the
// same code runs in the browser and in Node (>=18.20 exposes globalThis.crypto).
function newSubmissionId() {
  const c = globalThis.crypto;
  if (c && typeof c.randomUUID === 'function') return c.randomUUID().replace(/-/g, '');
  if (c && typeof c.getRandomValues === 'function') {
    const b = c.getRandomValues(new Uint8Array(16));
    b[6] = (b[6] & 0x0f) | 0x40;
    b[8] = (b[8] & 0x3f) | 0x80;
    return Array.from(b, (x) => x.toString(16).padStart(2, '0')).join('');
  }
  throw new DataMakerError('no secure random source available for submissionId', 'NO_RNG');
}

// Build the sealed SubmissionEnvelope without sending it. Useful when the
// caller wants to inspect, persist, or post it through their own transport.
// Returns { submissionId, envelope, payload, values }.
//
// `form` is a descriptor from parseDmf (or an equivalent object with formId,
// schemaVersion, recipientPublicKey, recipientUserId, fields).
async function buildSubmission(form, input, opts = {}) {
  if (!form || !form.recipientPublicKey) {
    throw new DataMakerError(
      'form has no recipient public key — share-only bundles cannot receive submissions',
      'NO_RECIPIENT'
    );
  }
  if (!form.recipientUserId) {
    throw new DataMakerError('form has no recipient userId', 'NO_RECIPIENT');
  }

  let values = input || {};
  if (opts.validate !== false) {
    const result = validateValues(form.fields || [], input, { allowUnknown: opts.allowUnknown });
    if (result.issues.length) throw new ValidationError(result.issues);
    values = result.values;
  }

  // SubmissionPayload — the plaintext sealed inside the envelope. Shape matches
  // DataMaker.Sync.Shared.SubmissionPayload (camelCase, Mode as a string enum).
  // formSchema is a legacy field no current receiver reads; left empty.
  const payload = {
    values,
    submittedAt: opts.submittedAt || new Date().toISOString(),
    formVersion: form.schemaVersion ?? 1,
    formSchema: '',
    mode: 'Create',
  };

  const ciphertext = await sealPayload(payload, form.recipientPublicKey);
  const submissionId = opts.submissionId || newSubmissionId();

  // SubmissionEnvelope — plaintext routing metadata + ciphertext. submitterId
  // is null for anonymous submissions (the public-survey path); editToken/
  // receivedAt are omitted (server-stamped / create-only).
  const envelope = {
    submissionId,
    formId: form.formId,
    recipientUserId: form.recipientUserId,
    submitterId: opts.submitterId ?? null,
    ciphertext,
  };

  return { submissionId, envelope, payload, values };
}

// POST a built envelope to the public /submissions endpoint. Returns the
// server's { submissionId, editToken }.
async function postSubmission(envelope, opts = {}) {
  const baseUrl = (opts.apiBaseUrl || DEFAULT_API_BASE_URL).replace(/\/+$/, '');
  const doFetch = opts.fetch || globalThis.fetch;
  if (typeof doFetch !== 'function') {
    throw new DataMakerError(
      'no fetch implementation available — use Node 18+ or pass opts.fetch',
      'NO_FETCH'
    );
  }

  const resp = await doFetch(`${baseUrl}/submissions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify(envelope),
  });

  const text = await resp.text();
  if (!resp.ok) throw new SubmissionError(resp.status, text);

  let body;
  try {
    body = JSON.parse(text);
  } catch {
    throw new SubmissionError(resp.status, `unparseable response body: ${text}`);
  }
  return { submissionId: body.submissionId, editToken: body.editToken };
}

// One-shot: read a .dmf (or take a pre-parsed form), validate + seal values,
// and post. Returns { submissionId, editToken, formId }.
async function submit(opts = {}) {
  let form = opts.form;
  if (!form) {
    if (!opts.dmf) {
      throw new DataMakerError('submit requires either opts.form or opts.dmf', 'NO_FORM');
    }
    form = await parseDmf(opts.dmf, { verify: opts.verify });
  }

  const { envelope } = await buildSubmission(form, opts.values, opts);
  const result = await postSubmission(envelope, opts);
  return { ...result, formId: form.formId };
}

module.exports = {
  DEFAULT_API_BASE_URL,
  newSubmissionId,
  buildSubmission,
  postSubmission,
  submit,
};
