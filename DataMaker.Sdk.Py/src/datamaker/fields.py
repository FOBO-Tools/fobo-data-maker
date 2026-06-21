"""Canonical Data Maker field kinds + per-kind coercion.

form.json persists kinds in lowercase kebab-case ("long-text", "multi-choice").
Older bundles use PascalCase enum names or no-dash spellings; normalize_kind
folds every variant onto the canonical kebab id.
"""

from __future__ import annotations

import re
from typing import Any, Optional, Tuple

KINDS = {
    "TEXT": "text",
    "LONG_TEXT": "long-text",
    "RICH_TEXT": "rich-text",
    "NUMBER": "number",
    "DECIMAL": "decimal",
    "MONEY": "money",
    "DATE": "date",
    "DATETIME": "datetime",
    "BOOLEAN": "boolean",
    "CHOICE": "choice",
    "MULTI_CHOICE": "multi-choice",
    "LIST": "list",
    "EMAIL": "email",
    "PHONE": "phone",
    "URL": "url",
    "GEO": "geo",
    "IMAGE": "image",
    "ATTACHMENT": "attachment",
    "SIGNATURE": "signature",
    "INITIALS": "initials",
    "RELATION": "relation",
}
ALL_KINDS = set(KINDS.values())

_ALIASES = {
    "longtext": "long-text",
    "richtext": "rich-text",
    "multichoice": "multi-choice",
    "datetimeoffset": "datetime",
}

# Kinds in fields[] that never accept a submitted value.
NON_INPUT_KINDS = {"calc", "calculated", "heading"}

_TRUE = {"true", "1", "yes", "y", "on"}
_FALSE = {"false", "0", "no", "n", "off"}


def _squash(s: str) -> str:
    return re.sub(r"[^a-z0-9]", "", s)


def normalize_kind(kind: Any) -> str:
    k = str(kind or "").strip().lower()
    if k in ALL_KINDS:
        return k
    if k in _ALIASES:
        return _ALIASES[k]
    squashed = _squash(k)
    if squashed in _ALIASES:
        return _ALIASES[squashed]
    for canonical in ALL_KINDS:
        if _squash(canonical) == squashed:
            return canonical
    return k


def is_input_kind(kind: Any) -> bool:
    return normalize_kind(kind) not in NON_INPUT_KINDS


def _choice_values(field: dict):
    choices = field.get("choices") if field else None
    if not choices:
        return None
    return {str(c["value"]) for c in choices}


def coerce_value(kind: Any, raw: Any, field: dict) -> Tuple[Any, Optional[str]]:
    """Coerce ``raw`` to the wire shape for ``kind``. Returns ``(value, None)``
    on success or ``(None, error_message)``."""
    k = normalize_kind(kind)

    if k in ("number", "decimal", "money"):
        try:
            n = raw if isinstance(raw, (int, float)) and not isinstance(raw, bool) else float(str(raw).strip())
        except (TypeError, ValueError):
            return None, f'expected a number, got "{raw}"'
        if isinstance(n, float) and n.is_integer():
            n = int(n)
        return n, None

    if k == "boolean":
        if isinstance(raw, bool):
            return raw, None
        s = str(raw).strip().lower()
        if s in _TRUE:
            return True, None
        if s in _FALSE:
            return False, None
        return None, f'expected a boolean, got "{raw}"'

    if k == "multi-choice":
        arr = raw if isinstance(raw, (list, tuple)) else [raw]
        values = [str(v) for v in arr]
        allowed = _choice_values(field)
        if allowed and not field.get("allow_custom"):
            bad = [v for v in values if v not in allowed]
            if bad:
                return None, "not in allowed choices: " + ", ".join(bad)
        return values, None

    if k == "choice":
        v = str(raw)
        allowed = _choice_values(field)
        if allowed and not field.get("allow_custom") and v not in allowed:
            return None, f'"{v}" is not one of the allowed choices'
        return v, None

    if k in ("text", "long-text", "rich-text", "email", "phone", "url", "date", "datetime"):
        return str(raw), None

    # image/attachment/geo/relation/list and unknown: pass through.
    return raw, None
