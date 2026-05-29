"""Validate + coerce a caller's values dict against a form's field schema."""

from __future__ import annotations

from typing import Any, Dict, List, Tuple

from .fields import normalize_kind, is_input_kind, coerce_value, NON_INPUT_KINDS


def _is_empty(v: Any) -> bool:
    if v is None:
        return True
    if isinstance(v, str):
        return v.strip() == ""
    if isinstance(v, (list, tuple)):
        return len(v) == 0
    return False


def validate_values(
    fields: List[dict], input_values: Dict[str, Any], allow_unknown: bool = False
) -> Tuple[Dict[str, Any], List[dict]]:
    """Returns ``(values, issues)``. ``values`` holds only known, non-empty,
    input-kind fields coerced to their wire shape. ``issues`` is empty when the
    input is clean."""
    issues: List[dict] = []
    values: Dict[str, Any] = {}
    by_key = {f["key"]: f for f in fields}

    for key, raw in (input_values or {}).items():
        field = by_key.get(key)
        if field is None:
            if not allow_unknown:
                issues.append({"field": key, "kind": None, "message": "unknown field — not in the form schema"})
            continue
        if not is_input_kind(field["kind"]):
            nk = normalize_kind(field["kind"])
            issues.append({"field": key, "kind": nk, "message": f"field is read-only ({nk}) and cannot be submitted"})
            continue
        if _is_empty(raw):
            continue
        value, error = coerce_value(field["kind"], raw, field)
        if error:
            issues.append({"field": key, "kind": normalize_kind(field["kind"]), "message": error})
            continue
        values[key] = value

    for field in fields:
        if not field.get("required"):
            continue
        if normalize_kind(field["kind"]) in NON_INPUT_KINDS:
            continue
        if field["key"] not in values:
            issues.append({"field": field["key"], "kind": normalize_kind(field["kind"]), "message": "required field is missing"})

    return values, issues
