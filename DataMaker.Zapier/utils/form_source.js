// Resolves a Zap step's "which form?" input into a unified form-source
// record. Two paths:
//
//   1. User uploaded a `.dmf` file on the step → parse it, then UPSERT
//      the parsed metadata into the FOBO Zapier registry so downstream
//      /zapier/hooks subscribes + /submissions POSTs work. Idempotent
//      on the Lambda side — re-runs are safe.
//
//   2. User picked a form from the dropdown → fetch metadata from
//      /zapier/forms/{id} (already in the FOBO registry).
//
// Match-attested rule is enforced server-side: the Lambda compares
// JWT.sub to dmf.publisherUserId on publish + form-scoped calls, so
// the Zapier user can only operate on forms they actually own. No
// extra client-side check needed.

const fobo = require('./fobo');
const { parseDmf } = require('./dmf');

const SYNC_BASE = 'https://datamaker-api.fobo-tools.com';

/**
 * Returns `{ id, name, schema, recipientPubkey, userId, source }`
 * where `source` = 'dmf' | 'dropdown'. The shape mirrors what
 * /zapier/forms/{id} returns plus a `source` discriminator so
 * callers can adjust telemetry / logs.
 *
 * @param hasFanOut — if true and we end up calling publish-form
 *   (the dmf path), pass this through so the form lands in the
 *   trigger picker too. Triggers pass true, creates pass false.
 */
async function resolveFormSource(z, bundle, { hasFanOut = false } = {}) {
  const inputData = bundle.inputData || {};

  if (inputData.dmf) {
    return resolveFromDmf(z, bundle, inputData.dmf, hasFanOut);
  }

  if (!inputData.formId) {
    throw new z.errors.Error(
      'Pick a form from the dropdown OR upload a .dmf bundle.',
      'NoFormSource', 400);
  }

  const form = await fobo.get(z, bundle, `/zapier/forms/${encodeURIComponent(inputData.formId)}`);
  if (!form)
    throw new z.errors.Error('Form not found.', 'FormNotFound', 404);
  return { ...form, source: 'dropdown' };
}

async function resolveFromDmf(z, bundle, dmfUrl, hasFanOut) {
  // Download the .dmf bytes from the URL Zapier supplied for the
  // file input.
  const fileResp = await z.request({ url: dmfUrl, method: 'GET', raw: true });
  if (fileResp.status < 200 || fileResp.status >= 300)
    throw new z.errors.Error(`Couldn't fetch .dmf: ${fileResp.status}`, 'DmfFetchError', fileResp.status);
  const bytes = await streamToBuffer(fileResp.body);

  let parsed;
  try {
    parsed = await parseDmf(bytes);
  } catch (e) {
    throw new z.errors.Error(`.dmf parse failed: ${e.message}`, 'DmfParseError', 400);
  }

  // UPSERT into ZapierPublishedForms so downstream subscribe + create
  // calls find the form in the registry. Lambda enforces match-
  // attested: dmf.publisherUserId must equal JWT.sub, otherwise the
  // publish call returns 403 / will surface as a clear error in the
  // Zap editor.
  await fobo.post(z, bundle, '/zapier/publish-form', {
    formId:          parsed.formId,
    name:            parsed.name,
    schema:          parsed.schema,
    recipientPubkey: parsed.recipientPubkey,
    hasFanOut,
  });

  return {
    id:              parsed.formId,
    name:            parsed.name,
    schema:          parsed.schema,
    recipientPubkey: parsed.recipientPubkey,
    userId:          parsed.publisherUserId,
    fobo:            parsed.fobo || null,
    source:          'dmf',
  };
}

async function streamToBuffer(stream) {
  if (!stream) return Buffer.alloc(0);
  if (typeof stream.getReader === 'function') {
    const reader = stream.getReader();
    const chunks = [];
    while (true) {
      const { done, value } = await reader.read();
      if (done) break;
      chunks.push(Buffer.from(value));
    }
    return Buffer.concat(chunks);
  }
  const chunks = [];
  for await (const chunk of stream) chunks.push(Buffer.from(chunk));
  return Buffer.concat(chunks);
}

module.exports = { resolveFormSource };
