=== Data Maker Forms ===
Contributors: fobo
Tags: forms, datamaker, renderer
Requires at least: 6.4
Tested up to: 7.0
Requires PHP: 8.1
Stable tag: 0.1.0
License: GPLv2 or later
License URI: https://www.gnu.org/licenses/gpl-2.0.html

Render Data Maker forms on a WordPress site from signed .dmf bundles.

== Description ==

The Data Maker Forms plugin lets you host Data Maker forms on a WordPress site. Upload a signed `.dmf` bundle, paste the shortcode onto any page, and submissions are sealed against the form's recipient pubkey and forwarded to the Data Maker API.

Features:

* Full parity with the desktop renderer for the v1 column set: text, number, date, choice, multi-choice, checkbox, attachment, image, divider, spacer, group, rich-text, heading, button.
* Pre-compiled VisibleWhen / CalculatedExpression / ValidationRule expressions ship with the .dmf, so cascade fires entirely in the browser — no server round-trip per keystroke.
* libsodium-backed sealed submissions; the WP server never sees plaintext values once they leave the browser.
* Optional browser-localStorage edit flow lets submitters return and amend their previous submission via a one-time edit token.
* Theme switch: use the form's own Data Maker palette, or inherit the active WordPress theme.

Out of scope for v1: multi-step (Steps), signature/initials fields, relation fields, charts, nested record entry.

== Installation ==

1. Upload the `datamaker-renderer` folder to `/wp-content/plugins/`.
2. Activate the plugin under **Plugins**.
3. Configure the **Data Maker API URL** under **Data Maker Forms → Settings**.
4. Upload a `.dmf` bundle under **Data Maker Forms → Upload .dmf** with a slug.
5. Embed in a page: `[datamaker_form id="your-slug"]`.

== Requirements ==

* PHP 8.1+ with the `sodium` extension (default in PHP 7.2+).
* The `zip` extension.
* HTTPS on the WordPress site is strongly recommended.

== External services ==

This plugin relies on the following third-party services. No data is sent to any of them until a visitor submits a form, or until an administrator turns on the optional Turnstile feature.

**Data Maker API** (`https://datamaker-api.fobo-tools.com`)
The form's destination. When a visitor submits a form, the plugin seals (end-to-end encrypts) the submitted values against the form publisher's public key in the browser and POSTs the sealed blob plus minimal metadata (the form id, schema version, and a submission timestamp) to this endpoint, which routes it to the form's publisher. The WordPress server never sees the plaintext field values. File attachments are encrypted in the browser and PUT directly to the publisher's storage via a short-lived upload slot issued by this API. This service is operated by FOBO Tools.
Terms: https://fobo-tools.com/terms — Privacy: https://fobo-tools.com/privacy

**Cloudflare Turnstile** (`https://challenges.cloudflare.com`)
A privacy-friendly CAPTCHA, used only on forms where an administrator has enabled the per-form Turnstile toggle. When enabled, the visitor's browser loads Cloudflare's widget script and obtains a challenge token; that token is sent with the submission and verified server-side against Cloudflare's `siteverify` endpoint before the submission is sealed. No form field values are sent to Cloudflare. This service is operated by Cloudflare, Inc.
Terms: https://www.cloudflare.com/website-terms/ — Privacy: https://www.cloudflare.com/privacypolicy/

== Content Security Policy ==

The renderer evaluates pre-compiled VisibleWhen / Calculated / Validation expressions on the client at every keystroke. The compiled bodies are plain JavaScript and run via `Function(...)` constructor, which a strict `Content-Security-Policy: script-src 'self'` header rejects.

If the host site ships a CSP header, allow:

* `script-src 'self' 'unsafe-eval'` — to permit the compiled expression evaluator.
* `script-src 'self' https://challenges.cloudflare.com` — if the per-form Turnstile toggle is on (Cloudflare's widget loads from this origin).
* `connect-src 'self' https://datamaker-api.fobo-tools.com https://challenges.cloudflare.com` — for the sealed submit endpoint and the Turnstile siteverify call.
* `frame-src https://challenges.cloudflare.com` — for the Turnstile iframe.

Sites that cannot grant `'unsafe-eval'` can still upload forms with no VisibleWhen / Calculated / per-field Validation expressions; static layouts work without the expression evaluator running.

== Local development ==

A Docker harness ships in `docker-compose.yml` (WordPress 6.7 + PHP 8.2 + MariaDB 11) with the plugin folder bind-mounted into `wp-content/plugins/datamaker-renderer/`.

    make up           # start WP on http://localhost:8089
    make wp-install   # one-shot scripted install + activate (admin/admin)
    make logs         # tail WordPress error log
    make down         # stop containers
    make down ARGS=-v # stop + nuke volumes

PHP edits are picked up immediately. JS/CSS edits to assets/ are too — refresh the page in the browser.

== Changelog ==

= 0.1.0 =
* Initial release. Reads .dmf envelope v3 (compiled.json + elementCss.json + palette.css extras). Sealed POST/PUT submissions. localStorage edit flow.
