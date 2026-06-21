---
title: Email outegration
description: Send a branded transactional email per saved record — the Default FOBO template, Markdown, or your own Raw HTML, with {{field}} placeholders and {{#each fields}} loops.
---

Most of these docs cover sending data **into** DataMaker. An **outegration**
goes the other way: when a record is saved, DataMaker sends it **out**. The
**email** outegration sends one transactional email per saved record through
the FOBO relay (AWS SES underneath). No SMTP credentials — the shared FOBO
mailbox handles delivery; you configure the recipient, subject, and body in
the desktop app.

## Body formats

The form owner picks one of three body formats:

| Format | What it is |
|---|---|
| **Default (FOBO template)** | A branded, dark-mode email that auto-lays-out every filled field of the record under the FOBO logo and form name. Zero config — no body to write. |
| **Markdown** | Write the body in Markdown; it renders **inside the same FOBO dark shell** (logo header + footer + padding) — you just supply the content. `{{field_name}}` placeholders and `{{#each fields}}` loops work. |
| **Raw HTML** | Paste your own HTML — full control, no wrapper. The full template engine runs here (`{{field}}`, `{{#each fields}}`, `{{#if}}`), so you can copy the template below and customize it. |

Default is the out-of-box choice. Default and Markdown share the FOBO shell;
Raw HTML gives you the whole document.

## Placeholders

Reference a field by its **Name** (not its label or id): `{{full_name}}`.
Missing or empty fields resolve to an empty string.

Signatures render specially: in an HTML body they become an inline image
(delivered as a CID attachment so Gmail and iCloud don't strip them); in the
subject and the plaintext part they render as `Name signed on: dd-MM-yyyy`.

Top-level scalars available anywhere in a template:

| Token | Value |
|---|---|
| `{{form_name}}` | The form's display name |
| `{{record_id}}` | The saved record's id |
| `{{saved_at}}` | Save timestamp (`yyyy-MM-dd HH:mm`) |
| `{{to}}` | The resolved recipient address |
| `{{header_title}}` | Localized "New record in {form}" heading |
| `{{footer_text}}` | Localized "Sent to {to} by Data Maker" line |

## Loops

Iterate every filled field with `{{#each fields}} … {{/each}}`. Inside the
loop these tokens resolve against the current field:

| Token | Value |
|---|---|
| `{{label}}` | Field display label |
| `{{value}}` | Rendered value — kind-aware (signatures → inline image, scalars → escaped text) |
| `{{name}}` | Field Name (storage key) |
| `{{kind}}` | Field kind (`text`, `signature`, …) |

Two conditionals branch on the current field (valid only inside `{{#each fields}}`):

```
{{#if signature}} … {{else}} … {{/if}}
{{#if wide}} … {{/if}}
```

`signature` is true for Signature/Initials fields; `wide` is true for the
kinds that span a full row (LongText, RichText, Image, Attachment, MultiChoice,
List, Geo) plus signatures — everything else pairs two-across on wide viewports.
Malformed tags degrade gracefully — they never break a send.

## The FOBO default template

The Default format renders the template below: every filled field auto-laid-out
(scalars two-across, wide kinds and signatures full-row), dark-mode, table-based
for maximum email-client compatibility.

To customize it, switch the body format to **Raw HTML** and paste this in, then
edit. Two notes on the pasted version:

- The logo points at the hosted FOBO logo
  (`https://fobo-tools.github.io/fobo-data-maker/fobo-logo.png`). The built-in
  Default instead embeds it as a CID attachment.
- The built-in Default also paints its surfaces with embedded solid-colour
  **background-image tiles**, which stop Outlook/Apple Mail dark-mode from
  recolouring them. A pasted template can't ship those attachments, so it uses
  `background-color` only — host your own colour tiles if you need the same
  lock.

```html
<!doctype html>
<html lang="en" xmlns:v="urn:schemas-microsoft-com:vml" xmlns:o="urn:schemas-microsoft-com:office:office">
<head>
  <meta charset="utf-8">
  <meta name="viewport" content="width=device-width,initial-scale=1">
  <meta name="color-scheme" content="dark">
  <meta name="supported-color-schemes" content="dark">
  <!--[if mso]><style>table{border-collapse:collapse;}td{font-family:Arial,sans-serif;}</style><![endif]-->
  <title></title>
</head>
<body style="margin:0;padding:0;background-color:#0e1520;">
  <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#0e1520" style="background-color:#0e1520;margin:0;padding:0;">
    <tr>
      <td align="center" style="padding:24px 12px;">
        <!--[if mso]><table role="presentation" width="600" cellpadding="0" cellspacing="0" border="0"><tr><td><![endif]-->
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" border="0" bgcolor="#151c28" style="width:100%;max-width:600px;background-color:#151c28;border:1px solid #243041;border-radius:12px;">
          <tr>
            <td align="center" style="padding:36px 32px 22px 32px;border-bottom:1px solid #243041;">
              <img src="https://fobo-tools.github.io/fobo-data-maker/fobo-logo.png" alt="FOBO" width="96" height="96" style="display:block;width:96px;height:96px;border:0;outline:none;text-decoration:none;">
              <div style="margin-top:18px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;font-size:22px;line-height:1.3;font-weight:700;color:#ffffff;">{{header_title}}</div>
            </td>
          </tr>
          <tr>
            <td style="padding:0 22px 18px 22px;">
              <div style="font-size:0;">
                {{#each fields}}
                {{#if wide}}
                <div style="display:inline-block;width:100%;max-width:100%;vertical-align:top;box-sizing:border-box;">
                {{else}}
                <div style="display:inline-block;width:100%;max-width:50%;min-width:220px;vertical-align:top;box-sizing:border-box;">
                {{/if}}
                  <div style="padding:14px 10px;border-bottom:1px solid #243041;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
                    <div style="font-size:11px;letter-spacing:1px;text-transform:uppercase;color:#8a93a3;margin-bottom:6px;">{{label}}</div>
                    {{#if signature}}{{value}}{{else}}<div style="font-size:15px;line-height:1.5;color:#ffffff;word-break:break-word;">{{value}}</div>{{/if}}
                  </div>
                </div>
                {{/each}}
              </div>
            </td>
          </tr>
          <tr>
            <td align="center" style="padding:20px 32px 30px 32px;border-top:1px solid #243041;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Helvetica,Arial,sans-serif;">
              <div style="font-size:12px;line-height:1.55;color:#8a93a3;">{{footer_text}}</div>
            </td>
          </tr>
        </table>
        <!--[if mso]></td></tr></table><![endif]-->
      </td>
    </tr>
  </table>
</body></html>
```

Signatures sit on a light "paper" card so the black ink reads against the dark
email. Everything is inline-styled and table-based; the MSO conditional comments
pin the 600px column in Outlook, and the body cells reflow from two-across to a
single column on narrow screens.
