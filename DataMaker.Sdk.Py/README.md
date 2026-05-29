# datamaker (Python)

Submit records to a [Data Maker](https://datamaker.fobo-tools.com/) form from
Python. Reads a signed `.dmf` bundle, validates your values against its field
schema, **sealed-box encrypts them client-side against the form owner's public
key**, and posts the ciphertext to the public submissions endpoint. Everything
except the final POST runs offline; the server never sees your data in the
clear.

The Node twin is [`@fobo-tools/datamaker`](https://www.npmjs.com/package/@fobo-tools/datamaker)
— same wire contract, same `.dmf` reader.

## Install

```sh
pip install datamaker-forms
```

Depends only on [PyNaCl](https://pypi.org/project/PyNaCl/). Python ≥ 3.9.

## Library

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

You can also pass the raw bundle straight to `submit`:

```python
dm.submit(dmf=open("contact.dmf", "rb").read(), values={"email": "ada@example.com"})
```

### API

- **`read_form(dmf_bytes, verify=True) -> FormDescriptor`** — verifies the
  publisher's Ed25519 signature over the manifest and that `form.json` matches
  its signed hash; raises `DmfError` on tampering. The descriptor exposes
  `form_id`, `name`, `schema_version`, `submit_policy`, `recipient_user_id`,
  `recipient_public_key`, `fields`, and `verified`.

- **`submit(form=… | dmf=…, values=…, **opts) -> dict`** — validates, seals,
  and posts. Options: `api_base_url`, `submitter_id` (default `None` =
  anonymous), `validate` (default `True`), `allow_unknown`, `verify`, and
  `poster` (inject a custom HTTP poster for testing).

- **`build_submission(form, values, **opts) -> dict`** — validate + seal
  without sending; returns `submission_id`, `envelope`, `payload`, `values`.

- **`post_submission(envelope, **opts) -> dict`** — post a pre-built envelope.

- **`validate_values(fields, input, allow_unknown=False) -> (values, issues)`**
  — pure validation/coercion, no crypto.

### Validation

`submit`/`build_submission` raise `ValidationError` (with `.issues`) for missing
required fields, unknown keys (set `allow_unknown=True` to ignore), and values
for read-only kinds (`calc`, `heading`, `signature`). Values are coerced to the
wire shape per kind: numbers from strings, booleans from `"yes"`/`"true"`/`1`,
multi-choice → list, choice membership checked unless the field allows custom.

File-upload kinds (`image`, `attachment`) are passed through as-is.

## CLI

```sh
datamaker inspect contact.dmf
datamaker inspect contact.dmf --json

datamaker submit contact.dmf --field email=ada@example.com --field full_name=Ada
datamaker submit contact.dmf --data '{"email": "ada@example.com"}'
datamaker submit contact.dmf --data-file answers.json --dry-run
```

## License

BSD-3-Clause © FOBO Tools
