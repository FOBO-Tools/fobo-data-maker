// OAuth2 authentication against the FOBO website. The dance never
// touches Cognito Hosted UI directly — fobo-tools.com hosts the
// authorize + token endpoints (see FOBO.Website/Controllers/
// AccountController.cs: Authorize + Token + the OAuthCallback branch)
// so users see FOBO branding through the whole sign-in flow. The
// tokens we hand back are real Cognito access + refresh tokens, so
// the DataMaker Lambda's existing JWT validators accept them
// unchanged.
//
// Match-attested rule: triggers + creates all enforce
// `dmf.publisherUserId === JWT.sub` server-side, which means a Zap
// can only operate on forms the signed-in user actually owns. The
// app itself does no extra checks — the Lambda is the source of
// truth.

const authentication = {
  type: 'oauth2',

  oauth2Config: {
    authorizeUrl: {
      url: 'https://fobo-tools.com/oauth/authorize',
      params: {
        client_id:             'zapier',
        state:                 '{{bundle.inputData.state}}',
        redirect_uri:          '{{bundle.inputData.redirect_uri}}',
        response_type:         'code',
        scope:                 'openid email profile',
        code_challenge_method: 'S256',
        code_challenge:        '{{bundle.inputData.code_challenge}}',
      },
    },

    getAccessToken: {
      url:    'https://fobo-tools.com/oauth/token',
      method: 'POST',
      body: {
        grant_type:    'authorization_code',
        code:          '{{bundle.inputData.code}}',
        client_id:     'zapier',
        redirect_uri:  '{{bundle.inputData.redirect_uri}}',
        code_verifier: '{{bundle.inputData.code_verifier}}',
      },
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
    },

    refreshAccessToken: {
      url:    'https://fobo-tools.com/oauth/token',
      method: 'POST',
      body: {
        grant_type:    'refresh_token',
        refresh_token: '{{bundle.authData.refresh_token}}',
        client_id:     'zapier',
      },
      headers: { 'content-type': 'application/x-www-form-urlencoded' },
    },

    autoRefresh: true,
    // PKCE must be enabled explicitly — without this, Zapier-platform-core
    // doesn't generate code_verifier / code_challenge, the template vars
    // expand to empty strings, and fobo-tools.com /oauth/authorize rejects
    // the request with "PKCE S256 code_challenge is required."
    enablePkce:  true,
    scope:       'openid email profile',
  },

  // Smoke-call after the access token is exchanged. /zapier/me is a
  // tiny endpoint on the DataMaker Lambda that returns the caller's
  // Cognito sub + email straight from the JWT — confirms the token is
  // valid against the real user pool AND gives us a display label.
  test: {
    url: 'https://datamaker-api.fobo-tools.com/zapier/me',
  },

  // The label Zapier shows under the connection on the user's
  // dashboard. Zapier-platform-core auto-merges the /zapier/me test
  // response into `bundle.inputData` for connectionLabel lookup AND
  // into `bundle.authData` for subsequent perform calls — no manual
  // afterResponse caching needed.
  connectionLabel: '{{bundle.inputData.email}}',
};

module.exports = authentication;
