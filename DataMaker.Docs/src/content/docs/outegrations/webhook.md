---
title: Webhook outegration
description: The on-the-wire contract DataMaker pushes to your URL when a record is saved — payload modes, headers, HMAC signing, and retry behaviour.
---

Most of these docs cover sending data **into** DataMaker. An **outegration**
goes the other way: when a record is saved, DataMaker pushes it **out** to a
destination you control. The **webhook** outegration POSTs the record to a URL.

This page is the on-the-wire contract for anyone writing a receiver — a Zapier
/ Make / n8n step, your own backend, or a third-party endpoint. The form owner
configures the destination in the desktop app; you implement the receiving end.

## Payload modes

The form owner picks one of four body formats. There's no universal webhook
payload spec — every receiver is different — so you pick the mode that matches
what yours expects.

| Mode | Body shape | Content-Type |
|---|---|---|
| **DataMaker default** | Flat JSON: keys = field ids, plus a `_*` metadata envelope (below). | `application/json` |
| **Custom JSON template** | Whatever JSON shape the owner writes, with `{{fieldId}}` placeholders interpolated. | `application/json` |
| **Custom XML template** | Whatever XML shape they write. Values are XML-escaped on substitution so content can't break the structure. | `application/xml` |
| **Form-encoded** | `key=value&key=value`, URL-encoded. Same key set as default mode. | `application/x-www-form-urlencoded` |

- **Default** — best when you feed a Zapier / Make / n8n pipeline that parses
  any JSON, or you control the receiver and want the simplest shape.
- **Custom JSON** — best when the receiver is a CRM with a fixed payload
  contract, or you want nested structure (`contact.email`, `meta.source`)
  rather than the flat-keyed default.
- **Custom XML** — best for a legacy SOAP / WSDL endpoint or another XML-first
  system.
- **Form-encoded** — best for a classic form-post endpoint (Twilio SMS
  callbacks, legacy PHP scripts, OAuth-style POSTs).

## Transport

- **Method** — POST (default), PUT, or PATCH; set by the form owner.
- **Content-Type** — per the table above; defaults to `application/json; charset=utf-8`.
- **Authorization** — `Bearer <token>` when the owner filled in the optional
  bearer field. Omitted otherwise.
- **Custom headers** — any `Key: Value` lines the owner added. They're applied
  *before* the bearer, so a custom line can't shadow the canonical
  `Authorization`.
- **Signature header** — present only when an HMAC signing secret is set (see
  [HMAC signing](#hmac-signing)).

Don't filter on `User-Agent` — it's whatever the .NET runtime sets.

## Body — default mode

A single flat JSON object in two layers.

### 1. Metadata keys (always present, `_`-prefixed)

| Key | Type | Description |
|---|---|---|
| `_recordId` | string | Stable per-record id. |
| `_formId` | string | Stable per-form id. |
| `_savedAt` | string | ISO-8601 UTC timestamp of the save (e.g. `2026-05-26T12:00:00.0000000+00:00`). |
| `_reason` | string | One of `insert`, `update`, `delete`. See [reason semantics](#reason-semantics). |

### 2. Record field values

One key per field. If the owner left mappings empty, every field is emitted
under its raw field id (`firstName`, `email`, …). If they configured mappings,
only mapped fields are emitted under the target key they chose
(`first_name`, `email_address`, …) and unmapped fields are dropped.

Metadata keys are written first. If a mapping targets a `_`-prefixed key, the
mapping wins.

#### Field value types

| Form field kind | JSON shape |
|---|---|
| Text / Email / URL / Phone / RichText | string |
| Number | number (`double`) |
| Boolean | `true` / `false` |
| Date / DateTime | ISO-8601 string (`2026-05-26T00:00:00.0000000+00:00`) |
| Choice (single) | string |
| Choice (multi) | array of strings |
| List (tags) | array of strings |
| Geo | string (`"lat,lng"`) |
| Image / Attachment | string (blob URL) |
| Empty / unset | `null` |

## Custom template modes — `{{token}}` placeholders

In **Custom JSON** and **Custom XML** modes the owner writes the body template
directly. Each `{{fieldId}}` is replaced with the matching record value before
the request goes out. These built-ins are always available regardless of the
form's fields:

| Token | Resolves to |
|---|---|
| `{{_recordId}}` | Stable record id. |
| `{{_formId}}` | Stable form id. |
| `{{_savedAt}}` | ISO-8601 UTC save timestamp. |
| `{{_reason}}` | `insert`, `update`, or `delete`. |

### XML mode — automatic escaping

Every substituted value is XML-escaped (`<` → `&lt;`, `&` → `&amp;`, …), so a
value of `<script>alert(1)</script>` lands as
`&lt;script&gt;alert(1)&lt;/script&gt;`. Your XML structure stays intact no
matter what the form collects.

### JSON mode — wrap placeholders in `"..."`

To keep the template valid JSON, wrap every placeholder in double quotes:

```json
{
  "name":   "{{firstName}}",
  "age":    "{{age}}",
  "active": "{{active}}",
  "tags":   "{{tags}}"
}
```

At render time the adapter emits the JSON type the field actually carries:

| Field value | Rendered as |
|---|---|
| Number / decimal | Raw number (`42`, `3.14`) — surrounding quotes stripped. |
| Boolean | `true` / `false` — quotes stripped. |
| Missing / null | `null` — quotes stripped. |
| String / date / list | JSON string, properly escaped — quotes kept. |
| Built-in `_*` tokens | JSON string (always). |

With `age=30`, `active=true`, `tags=["a","b"]` the receiver gets:

```json
{"name":"Ada","age":30,"active":true,"tags":"a, b"}
```

Bare placeholders (`{{firstName}} {{lastName}}`, not wrapped) interpolate inside
the surrounding string and JSON-escape the value — useful for composing strings,
but you lose the typed-value strip. Placeholders that match no field render as
`null` (wrapped JSON) or empty string (bare / XML). No error is raised.

## Sample request

```http
POST /your-endpoint HTTP/1.1
Host: hooks.example.com
Content-Type: application/json; charset=utf-8
Authorization: Bearer ey…
X-DataMaker-Signature: sha256=2f9b1c…
X-Env: prod

{
  "_recordId": "rec-01HXY3K8Q2A6B7",
  "_formId":   "form-01HXY3K8Q2A6B7",
  "_savedAt":  "2026-05-26T12:00:00.0000000+00:00",
  "_reason":   "insert",
  "first_name": "Ada",
  "last_name":  "Lovelace",
  "email":      "ada@example.com",
  "tags":       ["beta", "vip"],
  "score":      42
}
```

## HMAC signing

When the owner sets an optional signing secret, DataMaker computes:

```
signature = "sha256=" + lower(hex(hmac_sha256(secret, raw_request_body)))
```

…and sends it in the `X-DataMaker-Signature` header (or a custom header name
the owner picked). Same shape as GitHub's `X-Hub-Signature-256` and Stripe's
`Stripe-Signature` (without the `t=` timestamp prefix).

To verify:

1. Read the full **raw body before parsing JSON** — parsing then
   re-stringifying changes whitespace and breaks the HMAC.
2. Compute your own `sha256=...` digest with the shared secret.
3. Compare in **constant time**.
4. Reject on mismatch.

### Node.js

```js
const crypto = require('node:crypto');

function verify(req, secret) {
  const sig = req.headers['x-datamaker-signature'];
  if (!sig || !sig.startsWith('sha256=')) return false;
  const expected = 'sha256=' + crypto
    .createHmac('sha256', secret)
    .update(req.rawBody)
    .digest('hex');
  return crypto.timingSafeEqual(Buffer.from(sig), Buffer.from(expected));
}
```

### Python

```python
import hmac, hashlib

def verify(raw_body: bytes, header_value: str, secret: str) -> bool:
    if not header_value or not header_value.startswith("sha256="):
        return False
    expected = "sha256=" + hmac.new(
        secret.encode("utf-8"), raw_body, hashlib.sha256
    ).hexdigest()
    return hmac.compare_digest(header_value, expected)
```

## Response handling

DataMaker classifies your response and reacts:

| Status | Treatment |
|---|---|
| 2xx | Success. |
| 3xx | Counted as success, but DataMaker does **not** follow the redirect — point your URL at the final destination. |
| 408, 429, any 5xx | Transient. Re-queued with exponential backoff; a `Retry-After` header (delta-seconds or HTTP-date) is honoured. |
| any other 4xx | Permanent. Flagged for the owner's attention in the desktop app until they fix the config or retry. |
| Network error / timeout | Transient. Same backoff as 5xx. |

Up to 200 chars of your response body (newlines collapsed) are surfaced to the
owner, so a clear error message helps them see *why* you rejected the request.

## Reason semantics

| `_reason` | When |
|---|---|
| `insert` | Record was just created. |
| `update` | Record edited after creation — delivered with `_reason: "update"`. |
| `delete` | Record deleted — **consumed but not delivered**: the webhook adapter no-ops deletes (no HTTP call). |

The form owner chooses which live events fire (create / update / delete) per
outegration. `insert` and `update` produce real webhook calls when enabled;
`delete` is currently never sent over the wire even if enabled.

## Idempotency

The webhook is **not** idempotent. DataMaker de-duplicates on its side, but on a
transient failure + retry your endpoint **may** see the same `_recordId` twice.
Make your receiver idempotent on `_recordId` if duplicate-tolerance matters
(e.g. UPSERT into your store by that key).

## Secrets & sharing

- Outbound config (URL, bearer, signing secret) lives only on the install that
  created it. `.dmf` exports carry **zero** outbound config — sharing a form
  does not hand your webhook destination or keys to anyone else.
- Prefer a native adapter over a raw webhook when one exists for your
  destination — native adapters add a Test button, destination-aware mapping,
  and OAuth instead of a pasted bearer.
