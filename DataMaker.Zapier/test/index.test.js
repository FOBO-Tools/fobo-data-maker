const zapier = require('zapier-platform-core');
require('should');

const App = require('../index');
const appTester = zapier.createAppTester(App);

zapier.tools.env.inject();

describe('App smoke', () => {
  it('exposes the auth + version metadata', () => {
    App.version.should.be.a.String();
    App.platformVersion.should.be.a.String();
    App.authentication.type.should.equal('oauth2');
    App.authentication.test.url.should.equal('https://datamaker-api.fobo-tools.com/zapier/me');
  });

  it('addBearerHeader adds Authorization when access_token present', () => {
    const fakeBundle = { authData: { access_token: 'abc' } };
    const fakeRequest = {};
    const fakeZ = {};
    const beforeFn = App.beforeRequest[0];
    const result = beforeFn(fakeRequest, fakeZ, fakeBundle);
    result.headers.Authorization.should.equal('Bearer abc');
  });

  it('appTester wires the app cleanly', async () => {
    // Resource-cheap sanity check that zapier-platform-core can load
    // the app without exploding (catches accidental top-level errors).
    appTester.should.be.a.Function();
  });
});
