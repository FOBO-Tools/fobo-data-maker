// Hidden helper for the "New form submission" trigger's form
// dropdown. Filters /zapier/forms by HasFanOut=true so users never
// subscribe a Zap to a published-but-silent form (one without a
// matching Zapier outegration on the desktop side).
//
// The Create-record action uses the non-filtering `list_forms`
// helper — submissions into a form work fine without fan-out
// configured.

const fobo = require('../utils/fobo');

const listFormsFanOut = async (z, bundle) => {
  const forms = await fobo.get(z, bundle, '/zapier/forms?withFanOut=true');
  return forms || [];
};

module.exports = {
  key:    'list_forms_fanout',
  noun:   'Form',
  display: {
    label:       'List Data Maker forms with fan-out',
    description: 'Internal helper — powers the form dropdown on the New-submission trigger. Only lists forms whose desktop side has the Zapier outegration enabled.',
    hidden:      true,
  },
  operation: {
    perform:      listFormsFanOut,
    canPaginate:  false,
    sample:       { id: '00000000000000000000000000000000', name: 'Sample form' },
    outputFields: [
      { key: 'id',          label: 'Form ID',   type: 'string' },
      { key: 'name',        label: 'Form name', type: 'string' },
      { key: 'hasFanOut',   label: 'Fan-out',   type: 'boolean' },
      { key: 'publishedAt', label: 'Published', type: 'datetime' },
    ],
  },
};
