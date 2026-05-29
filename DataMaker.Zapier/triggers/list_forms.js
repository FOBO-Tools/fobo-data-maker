// Dynamic-dropdown helper consumed by every trigger + create that
// asks the user "which DataMaker form?". Calls /zapier/forms which
// only returns forms the signed-in user has explicitly published to
// Zapier from their desktop (Connect to Zapier button in the
// Outegrations tab). Forms that haven't been published won't appear
// — by design, so Zapier never sees the user's full form library.

const fobo = require('../utils/fobo');

const listForms = async (z, bundle) => {
  const forms = await fobo.get(z, bundle, '/zapier/forms');
  return forms || [];
};

module.exports = {
  key:         'list_forms',
  noun:        'Form',
  display: {
    label:                'List Data Maker forms',
    description:          'Internal helper that powers the form dropdown in other triggers and actions.',
    hidden:               true,
  },
  operation: {
    perform:              listForms,
    canPaginate:          false,
    sample:               { id: '00000000000000000000000000000000', name: 'Sample form' },
    outputFields:         [
      { key: 'id',          label: 'Form ID',   type: 'string' },
      { key: 'name',        label: 'Form name', type: 'string' },
      { key: 'publishedAt', label: 'Published', type: 'datetime' },
    ],
  },
};
