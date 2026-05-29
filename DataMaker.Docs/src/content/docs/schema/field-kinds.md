---
title: Field kinds
description: Every built-in field kind, its options, and the value shape it produces in a submission.
---

`FieldDefinition.kind` selects the editor and the **submitted value shape**.
Kinds are lowercase kebab-case. Older bundles may use PascalCase
(`LongText`) or no-dash (`longtext`) spellings — normalize by lowercasing and
folding to the canonical id below.

## The kinds

| Kind | Submitted value | Option block | Notes |
|---|---|---|---|
| `text` | string | `text` | `minLength`, `maxLength`, `pattern` |
| `long-text` | string | `text` | multi-line |
| `rich-text` | string | `text` | Markdown |
| `number` | number (integer) | `number` | `min`, `max`, `decimalPlaces`, `format` |
| `decimal` | number | `number` | fractional |
| `money` | number | `money` | `currency` (default `EUR`), `decimalPlaces` (default 2) |
| `date` | string `YYYY-MM-DD` | `date` | `min`, `max` |
| `datetime` | string ISO-8601 | `date` | `min`, `max` |
| `boolean` | boolean | — | |
| `choice` | string | `choice` | single select; `allowCustom` permits a free value |
| `multi-choice` | string[] | `choice` | multi select |
| `list` | string[] | — | free-entry list (chips) |
| `email` | string | `text` | validated email |
| `phone` | string | `text` | |
| `url` | string | `text` | validated URL |
| `geo` | `{ lat, lng, formattedAddress? }` | — | both lat+lng required |
| `image` | `{ url, hash, owned }` | `attachment` | uploaded blob ref |
| `attachment` | `{ url, hash, owned }` | `attachment` | `acceptedExtensions`, `maxSizeBytes` |
| `relation` | string \| string[] | `relation` | `targetFormId`, `multiple` |

### Read-only / non-input kinds

`calc` (calculated, derived), `heading`, and `signature` never accept a
submitted value — skip them when collecting values.

## Value coercion (what to put in `values`)

When building a submission, coerce per kind so the wire shape is consistent:

- **number / decimal / money** — a JSON number. Parse strings; emit integral
  values as integers.
- **boolean** — `true`/`false`. Accept `"yes"`/`"true"`/`1` → true,
  `"no"`/`"false"`/`0` → false.
- **multi-choice** — always an array of strings.
- **choice** — a string. If the field has `choices` and **not** `allowCustom`,
  the value must be one of the `choices[].value`.
- **text family / date / datetime / email / phone / url** — a string.
- **image / attachment / geo / relation / list** — the structured value above
  (the SDKs pass these through as-is; file upload is out of scope for the v1
  SDKs).

The SDKs do this coercion for you and reject: missing required fields, unknown
keys, values for read-only kinds, and out-of-list choices.

## ChoiceOption

```json
{ "value": "sales", "label": "Sales", "color": "#04c8ff", "icon": "f0e0" }
```

`value` is what's submitted; `label` is shown. `color`/`icon` are optional
display hints (icon is a Font Awesome codepoint).

## Validation rules

Beyond the option-block constraints, `FieldDefinition.validation[]` holds
explicit rules:

```json
{ "id": "r1", "type": "required", "config": { } }
```

Built-in rule types include `required`, `pattern`, `minLength`, `maxLength`,
`min`, `max`, and `expression` (a DSL expression — see
[expressions](/fobo-data-maker/schema/expressions/)). Custom messages live in
`FieldDefinition.messages` keyed by check id.
