---
title: Python SDK
description: Submit records to a DataMaker form from Python with datamaker-forms.
---

`datamaker-forms` reads a `.dmf`, validates, sealed-box encrypts, and posts.
Depends only on [PyNaCl](https://pypi.org/project/PyNaCl/). Python ≥ 3.9.

## Install

```sh
pip install datamaker-forms
```

The import name is `datamaker`.

## Submit

```python
import datamaker as dm

with open("contact.dmf", "rb") as fh:
    form = dm.read_form(fh.read())   # verifies signature + form hash

result = dm.submit(
    form=form,
    values={"email": "ada@example.com", "full_name": "Ada"},
)
# → {"submission_id": ..., "edit_token": ..., "form_id": "contact_form"}
```

Or pass the bytes: `dm.submit(dmf=open("contact.dmf","rb").read(), values=...)`.

## Building the `values` dict

`values` is keyed by each field's **`key`** (its storage name — *not* the
label). Inspect the form to see the names + kinds you need:

```python
for f in form.fields:
    print(f["key"], "·", f["kind"], "(required)" if f["required"] else "")
# email · email (required)
# full_name · text
# age · number
# subscribed · boolean
# plan · choice
# interests · multi-choice
# signup_date · date
# budget · money
```

Then map your data, using the value type each kind expects:

```python
values = {
    "email":       "ada@example.com",  # email        -> str
    "full_name":   "Ada Lovelace",     # text         -> str
    "age":          37,                # number        -> int  (37, not "37")
    "subscribed":   True,              # boolean       -> bool
    "plan":        "pro",              # choice        -> str (one of the choice values)
    "interests":   ["news", "beta"],   # multi-choice  -> list[str]
    "signup_date": "2026-05-29",       # date          -> "YYYY-MM-DD"
    "budget":       1999.99,           # money         -> float
}

dm.submit(form=form, values=values)
```

The full value type for every kind is in the
[field kinds reference](/fobo-data-maker/schema/field-kinds/).

:::tip[Forgiving, but be intentional]
By default `submit` **coerces** (`"37"` → `37`, `"yes"` → `True`, a single
choice → `["x"]` for multi-choice) and **validates**: it raises
`ValidationError` (with `.issues`) for a missing required field, an unknown key,
a value for a read-only kind, or an out-of-list choice. Pass `validate=False` to
skip, or `allow_unknown=True` to ignore extra keys.
:::

```python
try:
    dm.submit(form=form, values=values)
except dm.ValidationError as e:
    for i in e.issues:
        print(f"{i['field']}: {i['message']}")
```

## API

- **`read_form(dmf_bytes, verify=True) -> FormDescriptor`** — verifies the
  Ed25519 signature + form hash; raises `DmfError` on tampering.
- **`submit(form=… | dmf=…, values=…, **opts) -> dict`** — options:
  `api_base_url`, `submitter_id` (default `None`), `validate` (default `True`),
  `allow_unknown`, `verify`, `poster` (inject an HTTP poster for tests).
- **`build_submission(form, values, **opts) -> dict`** — seal without sending.
- **`post_submission(envelope, **opts) -> dict`** — post a pre-built envelope.
- **`validate_values(fields, input, allow_unknown=False) -> (values, issues)`**.

`submit`/`build_submission` raise `ValidationError` (with `.issues`); a non-2xx
raises `SubmissionError` (with `.status`).

## CLI

```sh
datamaker inspect contact.dmf
datamaker submit contact.dmf --field email=ada@example.com --field full_name=Ada
datamaker submit contact.dmf --data-file answers.json --dry-run
```

The wire format is identical to the JS and .NET SDKs — a `.dmf` signed
anywhere verifies here, and submissions interoperate.
