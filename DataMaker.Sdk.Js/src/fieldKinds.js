'use strict';

// Canonical Data Maker field kinds (DataMaker.Schema.Fields.FieldTypes) and the
// helpers the SDK uses to coerce/validate a submitted value per kind.
//
// form.json persists kinds in lowercase kebab-case ("long-text", "multi-choice").
// Older bundles + the desktop's enum names ("LongText", "MultiChoice") and the
// no-dash spellings ("longtext") also exist in the wild, so normalizeKind folds
// every variant onto the canonical kebab id before any lookup.

const KINDS = {
  TEXT: 'text',
  LONG_TEXT: 'long-text',
  RICH_TEXT: 'rich-text',
  NUMBER: 'number',
  DECIMAL: 'decimal',
  MONEY: 'money',
  DATE: 'date',
  DATETIME: 'datetime',
  BOOLEAN: 'boolean',
  CHOICE: 'choice',
  MULTI_CHOICE: 'multi-choice',
  LIST: 'list',
  EMAIL: 'email',
  PHONE: 'phone',
  URL: 'url',
  GEO: 'geo',
  IMAGE: 'image',
  ATTACHMENT: 'attachment',
  SIGNATURE: 'signature',
  INITIALS: 'initials',
  RELATION: 'relation',
};

const ALL_KINDS = new Set(Object.values(KINDS));

// Aliases: PascalCase enum names and no-dash spellings → canonical kebab id.
const ALIASES = {
  longtext: KINDS.LONG_TEXT,
  richtext: KINDS.RICH_TEXT,
  multichoice: KINDS.MULTI_CHOICE,
  datetimeoffset: KINDS.DATETIME,
};

// Kinds carried in form.fields[] that never accept a submitted value:
// calculated fields are server/derived, headings are layout-only.
const NON_INPUT_KINDS = new Set(['calc', 'calculated', 'heading']);

// Composite kinds the SDK passes through verbatim (caller must supply the
// already-built ref/object). File upload helpers are out of scope for v1.
const PASSTHROUGH_KINDS = new Set([
  KINDS.IMAGE,
  KINDS.ATTACHMENT,
  KINDS.SIGNATURE,
  KINDS.INITIALS,
  KINDS.GEO,
  KINDS.RELATION,
  KINDS.LIST,
]);

function normalizeKind(kind) {
  const k = String(kind || '').trim().toLowerCase();
  if (ALL_KINDS.has(k)) return k;
  if (ALIASES[k]) return ALIASES[k];
  // Strip non-alphanumerics ("long_text", "long text", "Long-Text") and retry.
  const squashed = k.replace(/[^a-z0-9]/g, '');
  if (ALIASES[squashed]) return ALIASES[squashed];
  for (const canonical of ALL_KINDS) {
    if (canonical.replace(/[^a-z0-9]/g, '') === squashed) return canonical;
  }
  return k; // unknown — leave as-is; treated as a pass-through string
}

function isInputKind(kind) {
  return !NON_INPUT_KINDS.has(normalizeKind(kind));
}

// Coerce a raw JS value to the wire shape the receiver expects for `kind`.
// Returns { value } on success or { error } with a human message. Empty input
// (undefined/null/"") is the caller's concern (required check), not ours.
function coerceValue(kind, raw, field) {
  const k = normalizeKind(kind);

  switch (k) {
    case KINDS.NUMBER:
    case KINDS.DECIMAL:
    case KINDS.MONEY: {
      const n = typeof raw === 'number' ? raw : Number(String(raw).trim());
      if (!Number.isFinite(n)) return { error: `expected a number, got "${raw}"` };
      return { value: n };
    }

    case KINDS.BOOLEAN: {
      if (typeof raw === 'boolean') return { value: raw };
      const s = String(raw).trim().toLowerCase();
      if (['true', '1', 'yes', 'y', 'on'].includes(s)) return { value: true };
      if (['false', '0', 'no', 'n', 'off'].includes(s)) return { value: false };
      return { error: `expected a boolean, got "${raw}"` };
    }

    case KINDS.MULTI_CHOICE: {
      const arr = Array.isArray(raw) ? raw : [raw];
      const values = arr.map((v) => String(v));
      const allowed = choiceValues(field);
      if (allowed && !field.allowCustom) {
        const bad = values.filter((v) => !allowed.has(v));
        if (bad.length) return { error: `not in allowed choices: ${bad.join(', ')}` };
      }
      return { value: values };
    }

    case KINDS.CHOICE: {
      const v = String(raw);
      const allowed = choiceValues(field);
      if (allowed && !field.allowCustom && !allowed.has(v)) {
        return { error: `"${v}" is not one of the allowed choices` };
      }
      return { value: v };
    }

    case KINDS.TEXT:
    case KINDS.LONG_TEXT:
    case KINDS.RICH_TEXT:
    case KINDS.EMAIL:
    case KINDS.PHONE:
    case KINDS.URL:
    case KINDS.DATE:
    case KINDS.DATETIME:
      return { value: String(raw) };

    default:
      // image/attachment/geo/relation/list and unknown kinds: pass through as-is.
      return { value: raw };
  }
}

function choiceValues(field) {
  if (!field || !Array.isArray(field.choices) || !field.choices.length) return null;
  return new Set(field.choices.map((c) => String(c.value)));
}

module.exports = {
  KINDS,
  ALL_KINDS,
  NON_INPUT_KINDS,
  PASSTHROUGH_KINDS,
  normalizeKind,
  isInputKind,
  coerceValue,
};
