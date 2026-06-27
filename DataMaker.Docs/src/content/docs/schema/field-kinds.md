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
| `scale` | number (integer) | `scale` | rating / Likert / NPS; `min`, `max`, `minLabel`, `maxLabel`, `shape`, `cumulative` |
| `choice` | string | `choice` | single select; `allowCustom` permits a free value |
| `multi-choice` | string[] | `choice` | multi select |
| `list` | string[] | — | free-entry list (chips) |
| `email` | string | — | validated email |
| `phone` | string | — | |
| `url` | string | — | validated URL |
| `geo` | `{ lat, lng, formattedAddress? }` | — | both lat+lng required |
| `image` | `{ url \| dataUri, hash, owned }` | — | uploaded blob ref |
| `attachment` | `{ url \| dataUri, hash, owned }` | `attachment` | `acceptedExtensions`, `maxSizeBytes` |
| `signature` | `{ dataUri \| url, hash, owned, typedName? }` | — | drawn-ink PNG, picked image, or typed name |
| `initials` | `{ dataUri \| url, hash, owned, typedName? }` | — | same shape as `signature` |
| `relation` | string \| string[] | `relation` | `targetFormId`, `displayFieldId`, `multiple` |

`email`, `phone` and `url` are validated intrinsically by kind — they carry no
option block.

### Calculated (derived) fields

There is no `calc` kind. **Any** field whose `calculatedExpression` is set is
derived: its value is computed from an [expression](/fobo-data-maker/schema/expressions/)
and is read-only. Skip these when collecting input.

### Decorative content is not a field

Headings, dividers, rich-text blocks, images, buttons and groups are **layout
columns**, not field kinds — see the [form model](/fobo-data-maker/schema/form-model/).
They never appear in `Form.fields` and never produce a submitted value.

## Value coercion (what to put in `values`)

When building a submission, coerce per kind so the wire shape is consistent:

- **number / scale** — a JSON integer.
- **decimal / money** — a JSON number. Parse strings; money honours its
  `decimalPlaces`.
- **boolean** — `true`/`false`. Accept `"yes"`/`"true"`/`1` → true,
  `"no"`/`"false"`/`0` → false.
- **multi-choice / list** — always an array of strings.
- **choice** — a string. If the field has `choices` and **not** `allowCustom`,
  the value must be one of the `choices[].value`.
- **text family / date / datetime / email / phone / url** — a string.
- **image / attachment / signature / initials** — a ref object
  (`{ url, hash, owned, ... }`, or `{ dataUri }` inline). For drawn signatures
  the ink is rasterised to a transparent PNG carried as `dataUri` or uploaded
  (`url` + `hash`); a `typedName` is accepted where there's no drawing surface.
  Uploaded bytes go up separately **before** you submit — see
  [Files & attachments](/fobo-data-maker/concepts/files/) for the upload-slot
  flow. The SDKs pass the ref through as-is.
- **geo / relation** — the structured value above, passed through as-is.

The SDKs do this coercion for you and reject: missing required fields, unknown
keys, values for derived (calculated) fields, and out-of-list choices.

## Choice / scale option blocks

A `choice` / `multi-choice` field's `choice` block carries the option list plus
layout: `choices[]`, `allowCustom`, `columns`, `display` (`Dropdown` |
`Radios`), `optionSize`, `optionColor`.

Each entry in `choices[]`:

```json
{ "value": "sales", "label": "Sales", "color": "#04c8ff", "icon": "f0e0" }
```

`value` is what's submitted; `label` is shown. `color`/`icon` are optional
display hints (icon is a Font Awesome codepoint).

A `scale` field's `scale` block carries `min` (default 1), `max` (default 5),
`minLabel`, `maxLabel`, `shape` (Circle/Square/Rounded/Diamond/Star),
`cumulative`, plus optional figure colours, alignment and spacing. The submitted
value is the chosen integer point.

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
