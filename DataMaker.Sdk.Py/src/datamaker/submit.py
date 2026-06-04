"""Build + post a Data Maker submission.

The wire envelope/payload are byte-exact to DataMaker.Sync.Shared
(SubmissionEnvelope / SubmissionPayload), so JSON keys here are camelCase even
though the Python API is snake_case.
"""

from __future__ import annotations

import json
import urllib.error
import urllib.request
import uuid
from datetime import datetime, timezone
from typing import Any, Callable, Dict, Optional, Tuple

from .dmf import FormDescriptor, read_form
from .errors import DataMakerError, SubmissionError, ValidationError
from .crypto import seal
from .validate import validate_values

DEFAULT_API_BASE_URL = "https://datamaker-api.fobo-tools.com"

# A poster takes (url, body_bytes, headers) and returns (status, text). The
# default uses urllib; tests inject a fake.
Poster = Callable[[str, bytes, Dict[str, str]], Tuple[int, str]]


def new_submission_id() -> str:
    """UUID v4 with dashes stripped — matches the desktop's GUID 'n' format."""
    return uuid.uuid4().hex


def _now_iso() -> str:
    return datetime.now(timezone.utc).isoformat().replace("+00:00", "Z")


def _as_descriptor(form) -> FormDescriptor:
    if isinstance(form, FormDescriptor):
        return form
    # Accept a plain mapping with the same field names.
    return FormDescriptor(
        form_id=form["form_id"],
        name=form.get("name", ""),
        description=form.get("description"),
        schema_version=form.get("schema_version", 1),
        submit_policy=form.get("submit_policy"),
        recipient_user_id=form.get("recipient_user_id"),
        recipient_public_key=form.get("recipient_public_key"),
        signer=form.get("signer"),
        envelope_version=form.get("envelope_version", 0),
        fields=form.get("fields", []),
        verified=form.get("verified", False),
    )


def build_submission(
    form,
    values: Dict[str, Any],
    *,
    validate: bool = True,
    allow_unknown: bool = False,
    submitter_id: Optional[str] = None,
    submission_id: Optional[str] = None,
    submitted_at: Optional[str] = None,
) -> dict:
    """Validate + seal a submission without sending. Returns
    ``{"submission_id", "envelope", "payload", "values"}``."""
    form = _as_descriptor(form)
    if not form.recipient_public_key:
        raise DataMakerError(
            "form has no recipient public key — share-only bundles cannot receive submissions",
            "NO_RECIPIENT",
        )

    out_values = values or {}
    if validate:
        out_values, issues = validate_values(form.fields, values, allow_unknown=allow_unknown)
        if issues:
            raise ValidationError(issues)

    payload = {
        "values": out_values,
        "submittedAt": submitted_at or _now_iso(),
        "formVersion": form.schema_version if form.schema_version is not None else 1,
        "formSchema": "",
        "mode": "Create",
    }
    ciphertext = seal(json.dumps(payload, separators=(",", ":")).encode("utf-8"), form.recipient_public_key)
    sid = submission_id or new_submission_id()

    envelope = {
        "submissionId": sid,
        "formId": form.form_id,
        # Recipient DESTINATION: the box public key the form is sealed to. The
        # server fingerprints it to route to the right database. Required.
        "recipientPubkey": form.recipient_public_key,
        "submitterId": submitter_id,
        "ciphertext": ciphertext,
    }
    return {"submission_id": sid, "envelope": envelope, "payload": payload, "values": out_values}


def _urllib_poster(url: str, body: bytes, headers: Dict[str, str]) -> Tuple[int, str]:
    req = urllib.request.Request(url, data=body, headers=headers, method="POST")
    try:
        with urllib.request.urlopen(req) as resp:
            return resp.status, resp.read().decode("utf-8")
    except urllib.error.HTTPError as e:
        return e.code, e.read().decode("utf-8", "replace")


def post_submission(
    envelope: dict,
    *,
    api_base_url: str = DEFAULT_API_BASE_URL,
    poster: Optional[Poster] = None,
) -> dict:
    """POST a built envelope to /submissions. Returns
    ``{"submission_id", "edit_token"}``."""
    url = api_base_url.rstrip("/") + "/submissions"
    body = json.dumps(envelope).encode("utf-8")
    headers = {"content-type": "application/json"}
    do_post = poster or _urllib_poster

    status, text = do_post(url, body, headers)
    if not (200 <= status < 300):
        raise SubmissionError(status, text)
    try:
        data = json.loads(text)
    except ValueError:
        raise SubmissionError(status, f"unparseable response body: {text}")
    return {"submission_id": data.get("submissionId"), "edit_token": data.get("editToken")}


def submit(
    *,
    form=None,
    dmf: Optional[bytes] = None,
    values: Dict[str, Any],
    api_base_url: str = DEFAULT_API_BASE_URL,
    validate: bool = True,
    allow_unknown: bool = False,
    submitter_id: Optional[str] = None,
    verify: bool = True,
    poster: Optional[Poster] = None,
) -> dict:
    """Read a .dmf (or take a pre-parsed form), validate + seal values, and
    post. Returns ``{"submission_id", "edit_token", "form_id"}``."""
    if form is None:
        if dmf is None:
            raise DataMakerError("submit requires either form= or dmf=", "NO_FORM")
        form = read_form(dmf, verify=verify)
    form = _as_descriptor(form)

    built = build_submission(
        form,
        values,
        validate=validate,
        allow_unknown=allow_unknown,
        submitter_id=submitter_id,
    )
    result = post_submission(built["envelope"], api_base_url=api_base_url, poster=poster)
    result["form_id"] = form.form_id
    return result
