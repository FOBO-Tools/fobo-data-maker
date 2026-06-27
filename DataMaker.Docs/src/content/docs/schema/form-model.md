---
title: Form model
description: The JSON shape of form.json — fields, layout (steps/sections/rows/columns), style, and submit policy.
---

`form.json` (inside the [`.dmf`](/fobo-data-maker/concepts/dmf/)) is the canonical form
definition. It separates **fields** (the data model) from **layout** (how
they're arranged). A minimal renderer only needs `fields`; a full one walks
`steps`.

## Top-level

```json
{
  "id": "contact_form",
  "name": "Contact form",
  "description": "Say hello",
  "schemaVersion": 1,
  "submitPolicy": "Anonymous",
  "fields": [ /* FieldDefinition[] */ ],
  "steps": [ /* Step[] — layout */ ],
  "style": { /* FormStyle */ },
  "cardLayout": { /* optional single-record card view */ },
  "lockedStyleFields": [ /* style paths the author pinned */ ],
  "messages": { "<slotId>": "custom message" }
}
```

| Field | Type | Notes |
|---|---|---|
| `id` | string | Stable form id (snake_case). |
| `name` | string | Display name. |
| `description` | string? | Optional. |
| `schemaVersion` | int | Per-form author-managed counter, starts at `1`. Copied into the submission `formVersion`. Not a format version. |
| `submitPolicy` | string | `"Anonymous"` or `"Authenticated"`. |
| `fields` | array | The data model — see [field kinds](/fobo-data-maker/schema/field-kinds/). |
| `steps` | array | Layout tree (below). |
| `style` | object? | Form-level theme/style (see palette/elementCss in the bundle). |
| `cardLayout` | object? | Optional layout for the single-record card / page-through view. Renderers that only render the form for submission can ignore it. |
| `lockedStyleFields` | string[] | Style paths the author explicitly pinned (authoring hint; not needed to render). |
| `messages` | object? | Form-level message-slot overrides (e.g. a validation banner). |

## FieldDefinition

Each entry in `fields[]`:

```json
{
  "id": "f1",
  "name": "email",
  "label": "Email",
  "description": null,
  "placeholder": null,
  "autocomplete": "email",
  "kind": "email",
  "required": true,
  "defaultValue": null,
  "calculatedExpression": null,
  "visibleWhen": null,
  "isPrimaryDisplay": false,
  "indexed": false,
  "validation": [ /* ValidationRule[] */ ],
  "messages": { "<checkId>": "custom error" },
  "style": { /* per-field Style override (optional) */ },
  "labelStyle": { /* per-field label Style override (optional) */ },
  "choice":  { "allowCustom": false, "choices": [ { "value": "a", "label": "A" } ], "display": "Radios", "columns": 2 },
  "number":  { "min": 0, "max": 100, "decimalPlaces": 0, "format": "N0" },
  "money":   { "currency": "EUR", "decimalPlaces": 2 },
  "text":    { "minLength": 0, "maxLength": 200, "pattern": null },
  "scale":   { "min": 1, "max": 5, "minLabel": "Low", "maxLabel": "High", "shape": "Circle" },
  "date":    { "min": "2020-01-01", "max": "2030-12-31" },
  "attachment": { "acceptedExtensions": [".pdf"], "maxSizeBytes": 5000000 },
  "relation": { "targetFormId": "...", "displayFieldId": "...", "multiple": false }
}
```

- `name` is the **storage key** used in submission `values` and expressions.
- `autocomplete` is an optional HTML autocomplete token (e.g. `email`, `name`).
- `kind` selects the editor + value shape — see [field kinds](/fobo-data-maker/schema/field-kinds/).
- Exactly one kind-specific option block (`choice`/`number`/`money`/`scale`/…)
  is populated, matching `kind`.
- `calculatedExpression` / `visibleWhen` are DSL strings — see
  [expressions](/fobo-data-maker/schema/expressions/).
- `isPrimaryDisplay` marks the record's title field; `indexed` requests a DB
  index (receiver-side hint).
- `style` / `labelStyle` are optional per-field design overrides (authoring
  detail; a structure-only renderer can ignore them).

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
| `richtext` | Markdown block (body in `markdown`) |
| `image` | a static image (`source`, `altText`, `maxHeight`, `maxWidth`, `align`) |
| `divider` | an `<hr>` (`thickness`, `color`) |
| `spacer` | vertical space (`height`) |
| `heading` | a heading (`text`, `level` 1–4) |
| `button` | a button — `name` + `label` (required), `action` (`None`/`Submit`/`Save`/`Reset`/`NextStep`/`PrevStep`), `variant`, `iconGlyph` |

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

See [Build your own](/fobo-data-maker/build-your-own/) for a minimal renderer walkthrough.
