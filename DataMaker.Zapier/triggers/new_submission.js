// REST-hook trigger: user picks a published DataMaker form; we POST
// to /zapier/hooks to subscribe; DataMaker's desktop fan-out
// (ZapierTriggerOutegration) POSTs every saved record to the hook
// URL Zapier supplied. The trigger itself doesn't poll — Zapier's
// platform replays the hook payloads.

const fobo = require('../utils/fobo');

const subscribeHook = async (z, bundle) => {
  return fobo.post(z, bundle, '/zapier/hooks', {
    formId:  bundle.inputData.formId,
    hookUrl: bundle.targetUrl,
  });
};

const unsubscribeHook = async (z, bundle) => {
  const hookId = bundle.subscribeData && bundle.subscribeData.id;
  if (!hookId) return null;
  return fobo.del(z, bundle, `/zapier/hooks/${encodeURIComponent(hookId)}`);
};

// `perform` for a REST hook = "render the payload that the desktop
// just POSTed us into a Zapier-style sample". The body comes in
// pre-parsed by zapier-platform-core via bundle.cleanedRequest.
const handleHook = (z, bundle) => {
  return [bundle.cleanedRequest];
};

// `performList` is Zapier's fallback poll when no real hook payload
// has been seen yet (e.g. for the "load test sample" button in the
// Zap editor). Returns the most recent few records via a future
// /zapier/forms/{id}/recent-submissions endpoint — out of v1 scope,
// so for now we return a synthetic sample built from the published
// form's schema so the user can still wire downstream actions.
const handleListFallback = async (z, bundle) => {
  if (!bundle.inputData.formId) return [];
  const form = await fobo.get(z, bundle, `/zapier/forms/${encodeURIComponent(bundle.inputData.formId)}`);
  if (!form) return [];
  return [buildSyntheticSample(form)];
};

function buildSyntheticSample(form) {
  const sample = {
    recordId: '00000000000000000000000000000000',
    formId:   form.id,
    savedAt:  new Date().toISOString(),
    reason:   'insert',
    fields:   {},
  };
  const fields = (form.schema && form.schema.fields) || [];
  for (const f of fields) {
    if (!f.key) continue;
    sample.fields[f.key] = sampleValueForKind(f.kind);
  }
  return sample;
}

function sampleValueForKind(kind) {
  switch ((kind || '').toLowerCase()) {
    case 'number':
    case 'decimal':       return 42;
    case 'boolean':       return true;
    case 'date':          return new Date().toISOString();
    case 'email':         return 'alice@example.com';
    case 'phone':         return '+15551234567';
    case 'url':           return 'https://example.com/';
    case 'longtext':      return 'Lorem ipsum dolor sit amet.';
    case 'choice':        return 'option-1';
    // Image / Attachment fan out as an object with a fetchable
    // downloadUrl (fresh pre-signed GET minted desktop-side). A
    // downstream Zap file-step can map `fields.<key>.downloadUrl` to
    // pull the bytes.
    case 'image':
    case 'attachment':
      return {
        fileName:    'example.png',
        mime:        'image/png',
        sizeBytes:   12345,
        hash:        '0'.repeat(64),
        owned:       true,
        downloadUrl: 'https://datamaker-api.fobo-tools.com/submissions/blob/…',
      };
    default:              return 'Sample value';
  }
}

module.exports = {
  key:    'new_submission',
  noun:   'Submission',
  display: {
    label:       'New form submission',
    description: 'Fires when a new record is saved to a Data Maker form you have published to Zapier from your desktop.',
  },
  operation: {
    type:                 'hook',
    performSubscribe:     subscribeHook,
    performUnsubscribe:   unsubscribeHook,
    perform:              handleHook,
    performList:          handleListFallback,
    inputFields: [
      {
        key:           'formId',
        label:         'Form',
        type:          'string',
        required:      true,
        dynamic:       'list_forms_fanout.id.name',
        altersDynamicFields: false,
        helpText:      'Pick a Data Maker form. Only forms with the Zapier outegration enabled on your desktop appear here.',
      },
    ],
    sample: {
      recordId: '00000000000000000000000000000000',
      formId:   '00000000000000000000000000000000',
      savedAt:  '2026-05-27T12:00:00Z',
      reason:   'insert',
      fields:   { firstName: 'Alice', lastName: 'Smith', email: 'alice@example.com' },
    },
    outputFields: [
      { key: 'recordId', label: 'Record ID', type: 'string' },
      { key: 'formId',   label: 'Form ID',   type: 'string' },
      { key: 'savedAt',  label: 'Saved at',  type: 'datetime' },
      { key: 'reason',   label: 'Reason',    type: 'string' },
      { key: 'fields',   label: 'Fields',    type: 'string' },
    ],
  },
};
