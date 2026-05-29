// Create a DataMaker record from any Zap. Submits via the existing
// anonymous-encrypted /submissions endpoint, encrypting the values
// dict client-side against the recipient pubkey shipped on
// /zapier/forms/{id}. The receiver's desktop drains the submission
// and inserts a record into the local SQLite — identical path the
// web renderer + WP plugin take.
//
// File inputs (Image / Attachment kinds) → fetched from Zapier's
// URL, uploaded to FOBO submission-blobs storage via the same
// pre-signed PUT slot the public renderers use, and the resulting
// {url, hash, owned} ref slots into the values dict in-place.

const fobo       = require('../utils/fobo');
const dm         = require('@fobo-tools/datamaker');
const { uploadBlob }              = require('../utils/blob_upload');

const SYNC_BASE = 'https://datamaker-api.fobo-tools.com';

// DataMaker FieldKind → Zapier inputField `type`.
const KIND_TO_ZAPIER_TYPE = {
  text:        'string',
  longtext:    'text',     // Zapier's multi-line variant
  email:       'string',
  phone:       'string',
  url:         'string',
  number:      'number',
  decimal:     'number',
  money:       'number',
  boolean:     'boolean',
  date:        'datetime',
  datetime:    'datetime',
  choice:      'string',
  multichoice: 'string',
  image:       'file',
  attachment:  'file',
};

// Kinds that don't accept user input (calculated, headings, layout-only).
const SKIPPED_KINDS = new Set(['calc', 'heading', 'signature']);

/**
 * Resolves form schema → Zapier inputFields. Called by Zapier on
 * "Set up" of the action step so the user gets per-form mapping UI.
 */
const buildInputFields = async (z, bundle) => {
  if (!bundle.inputData.formId) return [];

  const form = await fobo.get(z, bundle, `/zapier/forms/${encodeURIComponent(bundle.inputData.formId)}`);
  const fields = (form && form.schema && form.schema.fields) || [];

  const out = [];
  for (const f of fields) {
    const kind = (f.kind || '').toLowerCase();
    if (SKIPPED_KINDS.has(kind)) continue;
    const type = KIND_TO_ZAPIER_TYPE[kind] || 'string';

    const entry = {
      key:      f.key,
      label:    f.label || f.key,
      type,
      required: !!f.required,
    };
    if (kind === 'multichoice') entry.list = true;
    if (f.description) entry.helpText = f.description;
    if (f.placeholder) entry.placeholder = f.placeholder;
    if (f.choices && f.choices.length) {
      entry.choices = {};
      for (const c of f.choices) entry.choices[c.value] = c.label || c.value;
    }
    out.push(entry);
  }
  return out;
};

const perform = async (z, bundle) => {
  if (!bundle.inputData.formId)
    throw new z.errors.Error('formId is required.', 'BadInput', 400);

  // /zapier/forms/{id} = full metadata + recipient pubkey. JWT-authed,
  // 404 if the caller hasn't published the form — also doubles as the
  // match-attested gate (a Zapier user can't submit into a form they
  // don't own).
  const form = await fobo.get(z, bundle, `/zapier/forms/${encodeURIComponent(bundle.inputData.formId)}`);
  if (!form || !form.recipientPubkey)
    throw new z.errors.Error('Form not found or not published to Zapier.', 'FormNotPublished', 404);

  // Recipient = the form's publisher. /zapier/forms/{id} echoes the
  // userId so the submission envelope has a reliable source —
  // bundle.authData isn't populated with the /zapier/me test
  // response on OAuth2 connections (zapier-platform-core only
  // surfaces token endpoint fields there).
  const recipientUserId = form.userId;
  if (!recipientUserId)
    throw new z.errors.Error('Form metadata missing userId — Lambda response shape may be stale.', 'NoUserId', 500);

  // Pull every inputData key the user filled in. Schema's `fields`
  // is the canonical list of accepted keys; everything else gets
  // dropped to avoid passing accidental Zapier-internal metadata
  // through to the receiver.
  const schemaFields = (form.schema && form.schema.fields) || [];
  const values = {};
  for (const f of schemaFields) {
    const kind = (f.kind || '').toLowerCase();
    if (SKIPPED_KINDS.has(kind)) continue;
    const raw = bundle.inputData[f.key];
    if (raw === undefined || raw === null || raw === '') continue;

    if (kind === 'image' || kind === 'attachment') {
      values[f.key] = await uploadBlob(z, raw, recipientUserId);
      continue;
    }
    if (kind === 'multichoice' && Array.isArray(raw)) {
      values[f.key] = raw;
    } else {
      values[f.key] = raw;
    }
  }

  // Build + seal the SubmissionEnvelope via the shared SDK. `values` is
  // already filtered + blob-uploaded above, so validate:false keeps the
  // exact bytes Zapier produced. submitterId = the publisher: OAuth2
  // connections don't surface /zapier/me in bundle.authData, and form.version
  // (publisher's structural version at publish time) feeds formVersion — a
  // mismatch sends the row to the receiver's error_submissions for triage.
  const { envelope } = await dm.buildSubmission(
    {
      formId:             form.id,
      schemaVersion:      form.version || 1,
      recipientPublicKey: form.recipientPubkey,
      recipientUserId,
      fields:             schemaFields,
    },
    values,
    { validate: false, submitterId: recipientUserId }
  );

  // /submissions is anonymous in the existing pipeline; we send
  // through datamaker-api.fobo-tools.com which CloudFront path-routes
  // /submissions* → Sync Lambda origin.
  const resp = await z.request({
    url:     `${SYNC_BASE}/submissions`,
    method:  'POST',
    body:    envelope,
    headers: { 'content-type': 'application/json' },
  });
  if (resp.status < 200 || resp.status >= 300)
    throw new z.errors.Error(`submissions POST failed: ${resp.status} ${resp.content}`, 'SubmissionError', resp.status);

  const body = z.JSON.parse(resp.content);
  return {
    submissionId: body.submissionId,
    editToken:    body.editToken,
    formId:       form.id,
  };
};

module.exports = {
  key:   'create_record',
  noun:  'Record',
  display: {
    label:       'Create record',
    description: 'Submit a new record to a Data Maker form. The form must be published to Zapier first (Form home screen → Publish → Zapier on the desktop).',
  },
  operation: {
    perform,
    inputFields: [
      {
        key:                 'formId',
        label:               'Form',
        type:                'string',
        required:            true,
        dynamic:             'list_forms.id.name',
        altersDynamicFields: true,
        helpText:            'Pick a Data Maker form you have published to Zapier.',
      },
      buildInputFields,
    ],
    sample: {
      submissionId: '00000000000000000000000000000000',
      editToken:    'sample-token',
      formId:       '00000000000000000000000000000000',
    },
    outputFields: [
      { key: 'submissionId', label: 'Submission ID', type: 'string' },
      { key: 'editToken',    label: 'Edit token',    type: 'string' },
      { key: 'formId',       label: 'Form ID',       type: 'string' },
    ],
  },
};
