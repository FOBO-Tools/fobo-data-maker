---
title: Form model
description: The JSON shape of form.json — fields, layout (steps/sections/rows/columns), style, and submit policy.
---

`form.json` (inside the [`.dmf`](/concepts/dmf/)) is the canonical form
definition. It separates **fields** (the data model) from **layout** (how
they're arranged). A minimal renderer only needs `fields`; a full one walks
`steps`.

## Top-level

```json
{
  "id": "contact_form",
  "name": "Contact form",
  "description": "Say hello",
  "schemaVersion": 4,
  "submitPolicy": "Anonymous",
  "fields": [ /* FieldDefinition[] */ ],
  "steps": [ /* Step[] — layout */ ],
  "style": { /* FormStyle */ },
  "messages": { "<slotId>": "custom message" }
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | string | Stable form id (snake_case). |
| `name` | string | Display name. |
| `description` | string? | Optional. |
| `schemaVersion` | int | Bumped on breaking changes. Goes into the submission `formVersion`. |
| `submitPolicy` | string | `"Anonymous"` or `"Authenticated"`. |
| `fields` | array | The data model — see [field kinds](/schema/field-kinds/). |
| `steps` | array | Layout tree (below). |
| `style` | object? | Form-level theme/style (see palette/elementCss in the bundle). |

## FieldDefinition

Each entry in `fields[]`:

```json
{
  "id": "f1",
  "name": "email",
  "label": "Email",
  "description": null,
  "placeholder": null,
  "kind": "email",
  "required": true,
  "defaultValue": null,
  "calculatedExpression": null,
  "visibleWhen": null,
  "isPrimaryDisplay": false,
  "indexed": false,
  "validation": [ /* ValidationRule[] */ ],
  "messages": { "<checkId>": "custom error" },
  "choice":  { "allowCustom": false, "choices": [ { "value": "a", "label": "A" } ] },
  "number":  { "min": 0, "max": 100, "decimalPlaces": 0, "format": "N0" },
  "money":   { "currency": "EUR", "decimalPlaces": 2 },
  "text":    { "minLength": 0, "maxLength": 200, "pattern": null },
  "date":    { "min": "2020-01-01", "max": "2030-12-31" },
  "attachment": { "acceptedExtensions": [".pdf"], "maxSizeBytes": 5000000 },
  "relation": { "targetFormId": "...", "displayFieldId": "...", "multiple": false }
}
```

- `name` is the **storage key** used in submission `values` and expressions.
- `kind` selects the editor + value shape — see [field kinds](/schema/field-kinds/).
- Exactly one kind-specific option block (`choice`/`number`/`money`/…) is
  populated, matching `kind`.
- `calculatedExpression` / `visibleWhen` are DSL strings — see
  [expressions](/schema/expressions/).
- `isPrimaryDisplay` marks the record's title field; `indexed` requests a DB
  index (receiver-side hint).

## Layout: steps → sections → rows → columns

```
Form
└─ steps[]            (one step = one wizard page; render inline if you don't do wizards)
   └─ sections[]
      └─ rows[]        ( columnsPerRow: int = 12 )
         └─ columns[]  ( polymorphic — discriminated by "kind" )
```

Each **Row** has `columnsPerRow` (grid track count, default 12). Each **Column**
carries `span` (tracks, default 12) and an optional `stackBelowPx` (stack
full-width below that viewport width, default 640). Column kinds (discriminated
by `kind`):

| Column kind | Renders |
|---|---|
| `field` | a field, by `fieldId` → `fields[].id` |
| `group` | a titled, optionally-collapsible container with nested `rows[]` + its own `visibleWhen` |
| `richtext` | Markdown block |
| `image` | a static image (`source`, `altText`, `maxHeight`) |
| `divider` | an `<hr>` (`thickness`, `color`) |
| `spacer` | vertical space (`height`) |
| `heading` | a heading (`level` 1–4) |
| `button` | a submit/save/reset/action button (`variant`, `action`, `iconGlyph`) |

A field referenced by a `field` column is rendered where the column sits;
fields not placed in any column are typically auto-appended (renderer choice).

## Styling

Authoring tools bake resolved styles into the `.dmf v3`:

- `elementCss.json` — element-key → CSS string (e.g. `field/<id>`, `form/<id>`).
- `palette.css` — `:root` light/dark theme variables.
- `fonts.css` — `@font-face` for embedded fonts.

A renderer applies these on top of a structural layout layer. Rendering
structure-only (ignoring the author's design) is a valid mode — drop the
palette + elementCss and your host's CSS applies.

See [Build your own](/build-your-own/) for a minimal renderer walkthrough.
