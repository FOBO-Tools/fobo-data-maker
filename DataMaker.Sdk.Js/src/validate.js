'use strict';

const { normalizeKind, isInputKind, coerceValue, NON_INPUT_KINDS } = require('./fieldKinds');

// Validate + coerce a caller's values object against a form's field schema.
// Returns { values, issues }:
//   values — the coerced dict keyed by field name, ready to seal. Only known,
//            non-empty, input-kind fields land here.
//   issues — [{ field, kind, message }]; empty means the input is clean.
//
// Rules: unknown keys are rejected (typo protection), values for read-only
// kinds (calc/heading/signature) are rejected, required fields must be present
// and non-empty, and every value is coerced to its kind's wire shape.
function validateValues(fields, input, opts = {}) {
  const allowUnknown = opts.allowUnknown === true;
  const issues = [];
  const values = {};

  const byKey = new Map();
  for (const f of fields) byKey.set(f.key, f);

  for (const [key, raw] of Object.entries(input || {})) {
    const field = byKey.get(key);
    if (!field) {
      if (!allowUnknown) {
        issues.push({ field: key, kind: null, message: 'unknown field — not in the form schema' });
      }
      continue;
    }
    if (!isInputKind(field.kind)) {
      issues.push({
        field: key,
        kind: normalizeKind(field.kind),
        message: `field is read-only (${normalizeKind(field.kind)}) and cannot be submitted`,
      });
      continue;
    }
    if (isEmpty(raw)) continue; // handled by the required pass below

    const { value, error } = coerceValue(field.kind, raw, field);
    if (error) {
      issues.push({ field: key, kind: normalizeKind(field.kind), message: error });
      continue;
    }
    values[key] = value;
  }

  for (const field of fields) {
    if (!field.required) continue;
    if (NON_INPUT_KINDS.has(normalizeKind(field.kind))) continue;
    if (!(field.key in values)) {
      issues.push({
        field: field.key,
        kind: normalizeKind(field.kind),
        message: 'required field is missing',
      });
    }
  }

  return { values, issues };
}

function isEmpty(v) {
  if (v === undefined || v === null) return true;
  if (typeof v === 'string') return v.trim() === '';
  if (Array.isArray(v)) return v.length === 0;
  return false;
}

module.exports = { validateValues };
