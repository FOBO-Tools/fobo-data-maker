---
title: WordPress plugin
description: Host DataMaker forms on WordPress — upload a .dmf, drop a shortcode or block, submissions stay end-to-end encrypted.
---

The **Data Maker Forms** plugin hosts forms on a WordPress site. Upload a
signed `.dmf`, place a shortcode or block, and submissions are sealed in the
browser and forwarded to the DataMaker API — the WP server never sees plaintext.

Requires WordPress ≥ 6.4 and PHP ≥ 8.1.

## Use it

1. Install + activate the plugin.
2. Go to **Data Maker Forms → Upload .dmf** and upload a `.dmf` bundle. The
   plugin verifies the signature, stores the form, and lists it by id.
3. Place the form on a page:

   **Shortcode**
   ```text
   [datamaker_form id="contact_form"]
   ```

   The optional `theme` attribute (`on` / `off`) overrides theming per
   placement: `[datamaker_form id="contact_form" theme="off"]` renders
   structure-only so the active WP theme styles it.

   **Block** — add the *Data Maker Form* block (`datamaker/form`) and pick a
   form.

That's it — the form renders with full cascade (VisibleWhen / Calculated /
validation run in the browser from the bundle's compiled expressions), and
submissions seal against the form's recipient public key.

## How submission works

The plugin ships the same JS renderer as the [web embed](/fobo-data-maker/renderers/web-embed/).
Values are sealed in the browser (libsodium) and posted to the DataMaker API at
`https://datamaker-api.fobo-tools.com/` (overridable via the
`DM_RENDERER_SYNC_API_URL` constant). A built-in submit proxy + upload-slot flow
handles file fields. An optional localStorage edit token lets submitters return
and amend a prior submission.

## Theming

Choose per form: use the form's own DataMaker palette, or inherit the active
WordPress theme (structure-only render).

## Permissions

Admin screens are gated by the `manage_datamaker_forms` capability **or**
`manage_options` — so site admins always have access. Grant the custom
capability to give non-admin roles access:

```php
get_role('editor')->add_cap('manage_datamaker_forms');
```

## Scope (v1)

Supported: text, number, scale, date, choice, multi-choice, checkbox, list,
geo, attachment, image, signature, initials, divider, spacer, group, rich-text,
heading, button — and multi-step (wizard) forms.

Not yet: relation fields (placeholder only), charts, nested record entry.
