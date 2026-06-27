"""datamaker — submit records to a Data Maker form from Python.

    import datamaker as dm

    with open("contact.dmf", "rb") as fh:
        form = dm.read_form(fh.read())          # verified descriptor

    result = dm.submit(form=form, values={"email": "ada@example.com", "name": "Ada"})
    # → {"submission_id", "edit_token", "form_id"}

Everything except the final POST runs offline: the .dmf carries the form id,
schema version, field schema, and the recipient's X25519 public key, so the
payload is validated and sealed client-side before anything leaves.
"""

from .dmf import read_form, extract_fields, FormDescriptor
from .submit import (
    submit,
    build_submission,
    post_submission,
    new_submission_id,
    DEFAULT_API_BASE_URL,
)
from .validate import validate_values
from .fields import normalize_kind, is_input_kind, coerce_value, KINDS, ALL_KINDS, NON_INPUT_KINDS
from .crypto import seal, sha256_hex, verify_ed25519
from .errors import DataMakerError, DmfError, ValidationError, SubmissionError

__version__ = "0.1.3"

__all__ = [
    "read_form",
    "extract_fields",
    "FormDescriptor",
    "submit",
    "build_submission",
    "post_submission",
    "new_submission_id",
    "DEFAULT_API_BASE_URL",
    "validate_values",
    "normalize_kind",
    "is_input_kind",
    "coerce_value",
    "KINDS",
    "ALL_KINDS",
    "NON_INPUT_KINDS",
    "seal",
    "sha256_hex",
    "verify_ed25519",
    "DataMakerError",
    "DmfError",
    "ValidationError",
    "SubmissionError",
]
