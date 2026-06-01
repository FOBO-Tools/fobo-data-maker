'use strict';

// @fobo-tools/datamaker — submit records to a Data Maker form from anywhere.
//
//   const dm = require('@fobo-tools/datamaker');
//   const fs = require('node:fs');
//
//   const dmf  = fs.readFileSync('contact.dmf');
//   const form = await dm.readForm(dmf);          // verified descriptor
//   const res  = await dm.submit({ form, values: { email: 'a@b.com', name: 'Ada' } });
//   // → { submissionId, editToken, formId }
//
// Everything except the final POST runs offline: the .dmf carries the form id,
// schema version, field schema and the recipient's X25519 public key, so the
// payload is validated and sealed client-side before a single byte leaves.

const { parseDmf, extractFields } = require('./dmf');
const { initSodium, sealPayload, sha256Hex, sealBlob } = require('./encrypt');
const { validateValues } = require('./validate');
const {
  DEFAULT_API_BASE_URL,
  newSubmissionId,
  buildSubmission,
  postSubmission,
  submit,
} = require('./submit');
const { createSubmitHandler, setRenderOptions } = require('./render');
const fieldKinds = require('./fieldKinds');
const errors = require('./errors');

module.exports = {
  // .dmf reading — readForm is the friendly alias for parseDmf.
  readForm: parseDmf,
  parseDmf,
  extractFields,

  // Submission.
  submit,
  buildSubmission,
  postSubmission,
  newSubmissionId,
  DEFAULT_API_BASE_URL,

  // Web-renderer glue — turn renderer.js's onSubmit({form,col,values}) into a
  // sealed POST, and toggle whether the .dmf's author design is rendered.
  createSubmitHandler,
  setRenderOptions,

  // Validation + field-kind helpers.
  validateValues,
  ...fieldKinds,

  // Crypto primitives (sealed box).
  initSodium,
  sealPayload,
  sha256Hex,
  sealBlob,

  // Typed errors.
  ...errors,
};
