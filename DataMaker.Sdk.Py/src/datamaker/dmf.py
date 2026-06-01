"""Reader/verifier for the .dmf signed-bundle format
(DataMaker.Forms.Signing.DmfFormat).

A .dmf is a ZIP of manifest.json (the Ed25519-signed bytes), signature.bin
(64 raw bytes), form.json (the Form), plus optional assets bound by SHA-256 in
manifest.files[]. read_form verifies the signature over the manifest and that
form.json matches its signed hash, then returns a descriptor to submit against.
"""

from __future__ import annotations

import io
import json
import zipfile
from dataclasses import dataclass, field as dc_field
from typing import List, Optional

from .crypto import sha256_hex, verify_ed25519
from .errors import DmfError
from .fobo import verify_fobo_attestation

MANIFEST_ENTRY = "manifest.json"
SIGNATURE_ENTRY = "signature.bin"
FORM_ENTRY = "form.json"


@dataclass
class FormDescriptor:
    form_id: str
    name: str
    description: Optional[str]
    schema_version: int
    submit_policy: Optional[str]
    recipient_user_id: Optional[str]
    recipient_public_key: Optional[str]
    signer: Optional[dict]
    envelope_version: int
    fields: List[dict] = dc_field(default_factory=list)
    verified: bool = True
    #: ``{is_verified, email, company, sub, expires_at}`` when a valid FOBO
    #: attestation is present, else ``None`` (self-signed, no FOBO chain).
    fobo_verification: Optional[dict] = None

    @property
    def is_fobo_verified(self) -> bool:
        return self.fobo_verification is not None


def read_form(dmf_bytes: bytes, verify: bool = True) -> FormDescriptor:
    try:
        zf = zipfile.ZipFile(io.BytesIO(dmf_bytes))
    except zipfile.BadZipFile as e:
        raise DmfError(f"not a readable .dmf archive: {e}")

    names = set(zf.namelist())
    if MANIFEST_ENTRY not in names:
        raise DmfError(".dmf is missing manifest.json")
    if FORM_ENTRY not in names:
        raise DmfError(".dmf is missing form.json")
    if verify and SIGNATURE_ENTRY not in names:
        raise DmfError(".dmf is missing signature.bin")

    manifest_bytes = zf.read(MANIFEST_ENTRY)
    form_bytes = zf.read(FORM_ENTRY)
    manifest = _parse_json(manifest_bytes, "manifest.json")

    if verify:
        signature_bytes = zf.read(SIGNATURE_ENTRY)
        _verify_signature(manifest, manifest_bytes, signature_bytes)
        _verify_form_hash(manifest, form_bytes)

    form = _parse_json(form_bytes, "form.json")
    recipient = manifest.get("recipient")
    signer = manifest.get("signer")

    # FOBO attestation (optional) — its own chain, verified against the root
    # pubkey regardless of `verify`. None when absent or it fails to verify.
    fobo = None
    if signer and signer.get("publicKey") and isinstance(signer.get("foboAttestation"), dict):
        fobo = verify_fobo_attestation(signer["foboAttestation"], signer["publicKey"])

    return FormDescriptor(
        form_id=form.get("id"),
        name=form.get("name"),
        description=form.get("description"),
        schema_version=form.get("schemaVersion", 1),
        submit_policy=form.get("submitPolicy"),
        recipient_user_id=recipient.get("userId") if recipient else None,
        recipient_public_key=recipient.get("publicKey") if recipient else None,
        signer={"public_key": signer.get("publicKey"), "identity": signer.get("identity")} if signer else None,
        envelope_version=manifest.get("envelopeVersion", 0),
        fields=extract_fields(form),
        verified=verify,
        fobo_verification=fobo,
    )


def _parse_json(data: bytes, label: str):
    try:
        return json.loads(data.decode("utf-8"))
    except (ValueError, UnicodeDecodeError) as e:
        raise DmfError(f".dmf {label} is not valid JSON: {e}")


def _verify_signature(manifest: dict, manifest_bytes: bytes, signature_bytes: bytes) -> None:
    signer = manifest.get("signer") or {}
    if not signer.get("publicKey"):
        raise DmfError(".dmf manifest.signer.publicKey is missing — cannot verify")
    try:
        ok = verify_ed25519(manifest_bytes, signature_bytes, signer["publicKey"])
    except ValueError as e:
        raise DmfError(str(e))
    if not ok:
        raise DmfError(".dmf signature is invalid — bundle was tampered with or signer key is wrong")


def _verify_form_hash(manifest: dict, form_bytes: bytes) -> None:
    digest = sha256_hex(form_bytes)
    entry = next((f for f in manifest.get("files", []) if f.get("path") == FORM_ENTRY), None)
    if entry is None:
        raise DmfError(".dmf manifest does not list form.json — bundle is malformed")
    if entry.get("sha256") != digest:
        raise DmfError(".dmf form.json hash mismatch — bundle was tampered with")


def extract_fields(form: dict) -> List[dict]:
    out: List[dict] = []
    for f in form.get("fields", []) or []:
        entry = {
            "id": f.get("id"),
            "key": f.get("name"),
            "name": f.get("name"),
            "label": f.get("label") or f.get("name"),
            "kind": f.get("kind"),
            "required": bool(f.get("required")),
        }
        if f.get("description"):
            entry["description"] = f["description"]
        if f.get("placeholder"):
            entry["placeholder"] = f["placeholder"]
        choice = f.get("choice")
        choices = choice.get("choices") if isinstance(choice, dict) else None
        if choices:
            entry["choices"] = [{"value": c["value"], "label": c.get("label") or c["value"]} for c in choices]
            entry["allow_custom"] = bool(choice.get("allowCustom"))
        out.append(entry)
    return out
