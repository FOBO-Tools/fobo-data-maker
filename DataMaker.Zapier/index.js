// DataMaker Zapier integration root. See ../docs/PLAN-ZAPIER.md for
// architecture + phasing.
//
// v1 surface (phase 2 — auth shell only):
//   - OAuth2 against fobo-tools.com → Cognito tokens
//   - Smoke test against /zapier/me on the DataMaker Lambda
//
// Phases 3+ extend this with triggers/new_submission, creates/
// create_record, and the dynamic-form-picker dropdown.

const authentication = require('./authentication');
const listFormsTrigger        = require('./triggers/list_forms');
const listFormsFanOutTrigger  = require('./triggers/list_forms_fanout');
const newSubmissionTrigger    = require('./triggers/new_submission');
const createRecordAction      = require('./creates/create_record');

// Middleware: every authenticated request to the DataMaker Lambda
// gets the Cognito access token in the Authorization header. The
// Lambda's existing JwtBearer middleware validates against user pool
// eu-west-1_sKuDMzzoN.
const addBearerHeader = (request, z, bundle) => {
  if (bundle.authData && bundle.authData.access_token) {
    request.headers = request.headers || {};
    request.headers.Authorization = `Bearer ${bundle.authData.access_token}`;
  }
  return request;
};

const App = {
  version:         require('./package.json').version,
  platformVersion: require('zapier-platform-core').version,

  authentication,

  beforeRequest: [addBearerHeader],
  afterResponse: [],

  triggers: {
    [listFormsTrigger.key]:       listFormsTrigger,
    [listFormsFanOutTrigger.key]: listFormsFanOutTrigger,
    [newSubmissionTrigger.key]:   newSubmissionTrigger,
  },
  searches: {},
  creates: {
    [createRecordAction.key]: createRecordAction,
  },
};

module.exports = App;
