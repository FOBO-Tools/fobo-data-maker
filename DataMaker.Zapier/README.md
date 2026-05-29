# DataMaker Zapier integration

Native Zapier CLI app for the DataMaker form platform. Lets users:

- **Trigger Zaps** when a new record is submitted to one of their
  DataMaker forms (REST-hook subscription, instant fire).
- **Create DataMaker records** from any of Zapier's 6000+ apps.

See `../docs/PLAN-ZAPIER.md` for the full architecture + phasing
(OAuth2 via `fobo-tools.com`, match-attested ownership, hybrid
publish flow, fan-out via the desktop `OutegrationDispatcher`).

## Local development

```bash
npm install
npm test
npx zapier-platform link  # bind to the registered app id (one-off)
npx zapier-platform invoke triggers.list_forms
npx zapier-platform push  # deploy a private version to Zapier
```

Requires Node ≥ 18.20 (`zapier-platform-core` 19.0.0 engine pin).

## Deploy

Push to `main` runs `.github/workflows/deploy-zapier.yml` which
pushes a new private version to Zapier. Promotion to public versions
is manual via `npx zapier-platform promote $VERSION`.

## Status

- [x] Phase 1 — OAuth gateway live on `fobo-tools.com`
      (`/oauth/authorize` + `/oauth/token`).
- [ ] Phase 2 — Zapier app shell (this directory).
- [ ] Phase 3 — `/zapier/*` endpoints on DataMaker Lambda.
- [ ] Phase 4 — Trigger `new_submission` + desktop
      `ZapierTriggerOutegration` adapter.
- [ ] Phase 5 — Create `create_record`.
- [ ] Phase 6 — Zapier app review + public listing.
