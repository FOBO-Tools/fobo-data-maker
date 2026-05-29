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
