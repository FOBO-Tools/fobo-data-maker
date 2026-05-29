=== Data Maker Renderer ===
Contributors: fobo
Tags: forms, datamaker, renderer
Requires at least: 6.4
Tested up to: 6.7
Requires PHP: 8.1
Stable tag: 0.1.0
License: BSD-3-Clause
License URI: https://opensource.org/license/bsd-3-clause

Render Data Maker forms on a WordPress site from signed .dmf bundles.

== Description ==

The Data Maker Renderer plugin lets you host Data Maker forms on a WordPress site. Upload a signed `.dmf` bundle, paste the shortcode onto any page, and submissions are sealed against the form's recipient pubkey and forwarded to the Data Maker API.

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
