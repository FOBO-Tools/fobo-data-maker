// Reader for the .dmf signed-bundle format. Delegates to
// @fobo-tools/datamaker (which verifies the Ed25519 signature over the
// manifest and the form.json hash) and adapts its descriptor to the legacy
// shape this app already passes to /zapier/publish-form:
//   { formId, name, schema:{fields}, recipientPubkey, publisherUserId, envelopeVersion }

const { readForm } = require('@fobo-tools/datamaker');

async function parseDmf(bytes) {
  const form = await readForm(bytes); // throws on tamper / bad signature

  if (!form.recipientPublicKey || !form.recipientUserId) {
    throw new Error(
      ".dmf has no recipient block — share-only bundles can't be published to Zapier"
    );
  }

  // Minimal field subset Zapier's dynamic inputs + publish-form expect.
  const fields = form.fields.map((f) => {
    const entry = { id: f.id, key: f.key, label: f.label, kind: f.kind, required: f.required };
    if (f.description) entry.description = f.description;
    if (f.placeholder) entry.placeholder = f.placeholder;
    if (f.choices) entry.choices = f.choices.map((c) => ({ value: c.value, label: c.label || c.value }));
    return entry;
  });

  return {
    formId: form.formId,
    name: form.name,
    schema: { fields },
    recipientPubkey: form.recipientPublicKey,
    publisherUserId: form.recipientUserId,
    envelopeVersion: form.envelopeVersion || 0,
  };
}

module.exports = { parseDmf };
