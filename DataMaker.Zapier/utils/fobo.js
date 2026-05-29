// Thin DataMaker Lambda + sync API client. Pulls the bearer token
// out of bundle.authData (populated by Zapier-platform-core from the
// OAuth dance) and prefixes URLs onto the right base host. Errors
// throw with the body for the platform-core retry logic to surface.

const AGENT_BASE = 'https://datamaker-api.fobo-tools.com';

/** GET <agentBase><path>, JSON in / JSON out. */
async function get(z, bundle, path) {
  return req(z, bundle, 'GET', AGENT_BASE + path);
}

/** POST <agentBase><path>, JSON in / JSON out. */
async function post(z, bundle, path, body) {
  return req(z, bundle, 'POST', AGENT_BASE + path, body);
}

/** DELETE <agentBase><path>. Returns null on 204. */
async function del(z, bundle, path) {
  return req(z, bundle, 'DELETE', AGENT_BASE + path);
}

async function req(z, bundle, method, url, body) {
  const opts = {
    method,
    url,
    headers: {
      accept: 'application/json',
    },
  };
  if (body !== undefined) {
    opts.body = JSON.stringify(body);
    opts.headers['content-type'] = 'application/json';
  }
  const resp = await z.request(opts);
  if (resp.status === 204 || resp.content === '') return null;
  if (resp.status >= 200 && resp.status < 300) {
    try { return z.JSON.parse(resp.content); }
    catch { return resp.content; }
  }
  throw new z.errors.Error(
    `DataMaker API ${method} ${url} → ${resp.status}: ${resp.content || '(empty)'}`,
    'DataMakerApiError',
    resp.status);
}

module.exports = { get, post, del, AGENT_BASE };
