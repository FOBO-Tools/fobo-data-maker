'use strict';

const test = require('node:test');
const assert = require('node:assert/strict');
const JSZip = require('jszip');
const sodium = require('libsodium-wrappers-sumo');
const dm = require('../src/index');

// Build a real, signed .dmf in memory so the tests exercise the exact verify +
// seal path a production bundle hits — no fixtures, no network.
async function makeDmf({ tamperForm = false, withRecipient = true } = {}) {
  await sodium.ready;

  const signer = sodium.crypto_sign_keypair(); // Ed25519 — signs the manifest
  const recipient = sodium.crypto_box_keypair(); // X25519 — receives submissions

  const form = {
    id: 'contact_form',
    name: 'Contact form',
    description: 'Say hello',
    schemaVersion: 4,
    submitPolicy: 'Anonymous',
    fields: [
      { id: 'f1', name: 'email', label: 'Email', kind: 'email', required: true },
      { id: 'f2', name: 'full_name', label: 'Full name', kind: 'text', required: false },
      { id: 'f3', name: 'age', label: 'Age', kind: 'number', required: false },
      { id: 'f4', name: 'subscribe', label: 'Subscribe', kind: 'boolean', required: false },
      {
        id: 'f5',
        name: 'topic',
        label: 'Topic',
        kind: 'choice',
        required: false,
        choice: { allowCustom: false, choices: [{ value: 'sales', label: 'Sales' }, { value: 'support' }] },
      },
      {
        id: 'f6',
        name: 'tags',
        label: 'Tags',
        kind: 'multi-choice',
        required: false,
        choice: { allowCustom: false, choices: [{ value: 'a' }, { value: 'b' }, { value: 'c' }] },
      },
      { id: 'f7', name: 'total', label: 'Total', kind: 'calc', required: false },
    ],
  };

  const formBytes = new TextEncoder().encode(JSON.stringify(form));
  const formHash = sodium.to_hex(sodium.crypto_hash_sha256(formBytes));

  const manifest = {
    envelopeVersion: 3,
    signedAt: '2026-05-28T00:00:00Z',
    signer: {
      publicKey: sodium.to_base64(signer.publicKey, sodium.base64_variants.ORIGINAL),
      identity: { name: 'Tester', email: 't@example.com' },
    },
    recipient: withRecipient
      ? {
          publicKey: sodium.to_base64(recipient.publicKey, sodium.base64_variants.ORIGINAL),
          userId: 'user-123',
        }
      : null,
    files: [{ path: 'form.json', sha256: formHash, size: formBytes.length }],
  };

  const manifestBytes = new TextEncoder().encode(JSON.stringify(manifest));
  const signature = sodium.crypto_sign_detached(manifestBytes, signer.privateKey);

  const storedForm = tamperForm
    ? new TextEncoder().encode(JSON.stringify({ ...form, name: 'TAMPERED' }))
    : formBytes;

  const zip = new JSZip();
  zip.file('manifest.json', manifestBytes);
  zip.file('signature.bin', signature);
  zip.file('form.json', storedForm);
  const bytes = await zip.generateAsync({ type: 'nodebuffer' });

  return { bytes, recipient, signer };
}

function openSealed(ciphertextBase64, recipient) {
  const ct = sodium.from_base64(ciphertextBase64, sodium.base64_variants.ORIGINAL);
  const plain = sodium.crypto_box_seal_open(ct, recipient.publicKey, recipient.privateKey);
  return JSON.parse(new TextDecoder().decode(plain));
}

test('readForm verifies and extracts the descriptor', async () => {
  const { bytes } = await makeDmf();
  const form = await dm.readForm(bytes);

  assert.equal(form.formId, 'contact_form');
  assert.equal(form.name, 'Contact form');
  assert.equal(form.schemaVersion, 4);
  assert.equal(form.recipientUserId, 'user-123');
  assert.ok(form.recipientPublicKey);
  assert.equal(form.verified, true);

  const email = form.fields.find((f) => f.key === 'email');
  assert.equal(email.required, true);
  const topic = form.fields.find((f) => f.key === 'topic');
  assert.deepEqual(topic.choices.map((c) => c.value), ['sales', 'support']);
});

test('buildSubmission seals a payload the recipient can open', async () => {
  const { bytes, recipient } = await makeDmf();
  const form = await dm.readForm(bytes);

  const { envelope, payload } = await dm.buildSubmission(form, {
    email: 'ada@example.com',
    full_name: 'Ada',
    age: '37', // string in → number out
    subscribe: 'yes', // truthy string → boolean true
    tags: ['a', 'c'],
  });

  assert.equal(envelope.formId, 'contact_form');
  assert.equal(envelope.recipientUserId, 'user-123');
  assert.equal(envelope.submitterId, null);
  assert.equal(envelope.submissionId.length, 32); // GUID "n" format, no dashes

  const opened = openSealed(envelope.ciphertext, recipient);
  assert.equal(opened.formVersion, 4);
  assert.equal(opened.mode, 'Create');
  assert.equal(opened.formSchema, '');
  assert.equal(opened.values.email, 'ada@example.com');
  assert.equal(opened.values.age, 37);
  assert.equal(opened.values.subscribe, true);
  assert.deepEqual(opened.values.tags, ['a', 'c']);
  // The coerced payload matches what we sealed.
  assert.deepEqual(opened.values, payload.values);
});

test('validation rejects missing required, unknown keys, read-only and bad choices', async () => {
  const { bytes } = await makeDmf();
  const form = await dm.readForm(bytes);

  await assert.rejects(
    () => dm.buildSubmission(form, { full_name: 'NoEmail' }),
    (err) => err.code === 'VALIDATION_FAILED' && err.issues.some((i) => i.field === 'email')
  );

  await assert.rejects(
    () => dm.buildSubmission(form, { email: 'a@b.com', nope: 'x' }),
    (err) => err.issues.some((i) => i.field === 'nope')
  );

  await assert.rejects(
    () => dm.buildSubmission(form, { email: 'a@b.com', total: 5 }),
    (err) => err.issues.some((i) => i.field === 'total' && /read-only/.test(i.message))
  );

  await assert.rejects(
    () => dm.buildSubmission(form, { email: 'a@b.com', topic: 'marketing' }),
    (err) => err.issues.some((i) => i.field === 'topic')
  );
});

test('tampered form.json fails the hash check', async () => {
  const { bytes } = await makeDmf({ tamperForm: true });
  await assert.rejects(() => dm.readForm(bytes), (err) => err.code === 'DMF_INVALID');
});

test('share-only bundle cannot be submitted', async () => {
  const { bytes } = await makeDmf({ withRecipient: false });
  const form = await dm.readForm(bytes);
  assert.equal(form.recipientPublicKey, null);
  await assert.rejects(
    () => dm.buildSubmission(form, { email: 'a@b.com' }),
    (err) => err.code === 'NO_RECIPIENT'
  );
});

test('submit posts the envelope and returns the server result', async () => {
  const { bytes } = await makeDmf();

  let captured;
  const fakeFetch = async (url, init) => {
    captured = { url, init };
    return {
      ok: true,
      status: 200,
      text: async () => JSON.stringify({ submissionId: 'srv-id', editToken: 'tok-abc' }),
    };
  };

  const result = await dm.submit({
    dmf: bytes,
    values: { email: 'ada@example.com' },
    apiBaseUrl: 'https://example.test/',
    fetch: fakeFetch,
  });

  assert.equal(captured.url, 'https://example.test/submissions');
  assert.equal(captured.init.method, 'POST');
  const sent = JSON.parse(captured.init.body);
  assert.equal(sent.formId, 'contact_form');
  assert.ok(sent.ciphertext);
  assert.equal(result.editToken, 'tok-abc');
  assert.equal(result.formId, 'contact_form');
});

test('SubmissionError surfaces non-2xx responses', async () => {
  const { bytes } = await makeDmf();
  const fakeFetch = async () => ({ ok: false, status: 413, text: async () => 'too big' });
  await assert.rejects(
    () => dm.submit({ dmf: bytes, values: { email: 'a@b.com' }, fetch: fakeFetch }),
    (err) => err.code === 'SUBMISSION_REJECTED' && err.status === 413
  );
});

test('createSubmitHandler seals + posts a renderer payload', async () => {
  const { bytes, recipient } = await makeDmf();
  const form = await dm.readForm(bytes);

  let captured;
  const fakeFetch = async (url, init) => {
    captured = { url, body: JSON.parse(init.body) };
    return { ok: true, status: 200, text: async () => JSON.stringify({ submissionId: 's', editToken: 't' }) };
  };

  let succeeded;
  const onSubmit = dm.createSubmitHandler({
    recipientPublicKey: form.recipientPublicKey,
    recipientUserId: form.recipientUserId,
    apiBaseUrl: 'https://example.test',
    fetch: fakeFetch,
    onSuccess: (r) => { succeeded = r; },
  });

  // Shape renderer.js passes to hooks.onSubmit: { form (bundle.form), col, values }.
  const result = await onSubmit({
    form: { id: 'contact_form', schemaVersion: 4, fields: [{ id: 'f1', name: 'email', kind: 'email', required: true }] },
    col: null,
    values: { email: 'ada@example.com' },
  });

  assert.equal(result.ok, true);
  assert.equal(result.editToken, 't');
  assert.equal(succeeded.editToken, 't');
  assert.equal(captured.url, 'https://example.test/submissions');
  assert.equal(captured.body.formId, 'contact_form');

  const opened = sodium.crypto_box_seal_open(
    sodium.from_base64(captured.body.ciphertext, sodium.base64_variants.ORIGINAL),
    recipient.publicKey,
    recipient.privateKey
  );
  const payload = JSON.parse(new TextDecoder().decode(opened));
  assert.equal(payload.values.email, 'ada@example.com');
  assert.equal(payload.formVersion, 4);
});

test('createSubmitHandler routes validation issues to applyFieldErrors', async () => {
  let fieldErrors;
  const onSubmit = dm.createSubmitHandler({
    recipientPublicKey: (await dm.readForm((await makeDmf()).bytes)).recipientPublicKey,
    recipientUserId: 'user-123',
    applyFieldErrors: (m) => { fieldErrors = m; },
    fetch: async () => { throw new Error('should not POST when validation fails'); },
  });

  const result = await onSubmit({
    form: { id: 'f', schemaVersion: 1, fields: [{ id: 'f1', name: 'email', kind: 'email', required: true }] },
    values: {}, // missing required email
  });

  assert.equal(result.ok, false);
  assert.ok(result.issues.some((i) => i.field === 'email'));
  assert.equal(fieldErrors.email, 'required field is missing');
});

test('setRenderOptions / applyFormStyle is a no-op under Node (no window)', async () => {
  assert.equal(typeof globalThis.window, 'undefined');
  // Must not throw without a DOM, and createSubmitHandler must still build.
  dm.setRenderOptions({ applyFormStyle: false });
  const onSubmit = dm.createSubmitHandler({
    recipientPublicKey: (await dm.readForm((await makeDmf()).bytes)).recipientPublicKey,
    recipientUserId: 'user-123',
    applyFormStyle: false,
    fetch: async () => ({ ok: true, status: 200, text: async () => '{"submissionId":"s","editToken":"t"}' }),
  });
  const r = await onSubmit({ form: { id: 'f', schemaVersion: 1, fields: [] }, values: {} });
  assert.equal(r.ok, true);
});
