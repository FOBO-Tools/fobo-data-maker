import base64
import io
import json
import zipfile

import pytest
from nacl.public import PrivateKey, SealedBox
from nacl.signing import SigningKey

import datamaker as dm
from datamaker.errors import DmfError


def make_dmf(tamper_form=False, with_recipient=True):
    """Build a real, signed .dmf in memory so tests hit the exact verify + seal
    path a production bundle does — no fixtures, no network."""
    signer = SigningKey.generate()           # Ed25519 — signs the manifest
    recipient = PrivateKey.generate()        # X25519 — receives submissions

    form = {
        "id": "contact_form",
        "name": "Contact form",
        "description": "Say hello",
        "schemaVersion": 4,
        "submitPolicy": "Anonymous",
        "fields": [
            {"id": "f1", "name": "email", "label": "Email", "kind": "email", "required": True},
            {"id": "f2", "name": "full_name", "label": "Full name", "kind": "text", "required": False},
            {"id": "f3", "name": "age", "label": "Age", "kind": "number", "required": False},
            {"id": "f4", "name": "subscribe", "label": "Subscribe", "kind": "boolean", "required": False},
            {
                "id": "f5", "name": "topic", "label": "Topic", "kind": "choice", "required": False,
                "choice": {"allowCustom": False, "choices": [{"value": "sales", "label": "Sales"}, {"value": "support"}]},
            },
            {
                "id": "f6", "name": "tags", "label": "Tags", "kind": "multi-choice", "required": False,
                "choice": {"allowCustom": False, "choices": [{"value": "a"}, {"value": "b"}, {"value": "c"}]},
            },
            {"id": "f7", "name": "total", "label": "Total", "kind": "calc", "required": False},
        ],
    }

    form_bytes = json.dumps(form).encode("utf-8")
    form_hash = dm.sha256_hex(form_bytes)

    b64 = lambda b: base64.b64encode(b).decode("ascii")
    manifest = {
        "envelopeVersion": 3,
        "signedAt": "2026-05-28T00:00:00Z",
        "signer": {"publicKey": b64(bytes(signer.verify_key)), "identity": {"name": "Tester"}},
        "recipient": {"publicKey": b64(bytes(recipient.public_key)), "userId": "user-123"} if with_recipient else None,
        "files": [{"path": "form.json", "sha256": form_hash, "size": len(form_bytes)}],
    }
    manifest_bytes = json.dumps(manifest).encode("utf-8")
    signature = signer.sign(manifest_bytes).signature

    stored_form = json.dumps({**form, "name": "TAMPERED"}).encode("utf-8") if tamper_form else form_bytes

    buf = io.BytesIO()
    with zipfile.ZipFile(buf, "w") as zf:
        zf.writestr("manifest.json", manifest_bytes)
        zf.writestr("signature.bin", signature)
        zf.writestr("form.json", stored_form)
    return buf.getvalue(), recipient


def open_sealed(ciphertext_b64, recipient):
    plain = SealedBox(recipient).decrypt(base64.b64decode(ciphertext_b64))
    return json.loads(plain)


def test_read_form_verifies_and_extracts():
    data, _ = make_dmf()
    form = dm.read_form(data)
    assert form.form_id == "contact_form"
    assert form.schema_version == 4
    assert form.recipient_user_id == "user-123"
    assert form.recipient_public_key
    assert form.verified is True
    topic = next(f for f in form.fields if f["key"] == "topic")
    assert [c["value"] for c in topic["choices"]] == ["sales", "support"]


def test_build_submission_seals_openable_payload():
    data, recipient = make_dmf()
    form = dm.read_form(data)
    built = dm.build_submission(form, {
        "email": "ada@example.com",
        "full_name": "Ada",
        "age": "37",
        "subscribe": "yes",
        "tags": ["a", "c"],
    })
    env = built["envelope"]
    assert env["formId"] == "contact_form"
    assert env["recipientUserId"] == "user-123"
    assert env["submitterId"] is None
    assert len(env["submissionId"]) == 32

    opened = open_sealed(env["ciphertext"], recipient)
    assert opened["formVersion"] == 4
    assert opened["mode"] == "Create"
    assert opened["formSchema"] == ""
    assert opened["values"]["email"] == "ada@example.com"
    assert opened["values"]["age"] == 37
    assert opened["values"]["subscribe"] is True
    assert opened["values"]["tags"] == ["a", "c"]
    assert opened["values"] == built["payload"]["values"]


def test_validation_rejects_bad_input():
    data, _ = make_dmf()
    form = dm.read_form(data)

    with pytest.raises(dm.ValidationError) as e1:
        dm.build_submission(form, {"full_name": "NoEmail"})
    assert any(i["field"] == "email" for i in e1.value.issues)

    with pytest.raises(dm.ValidationError) as e2:
        dm.build_submission(form, {"email": "a@b.com", "nope": "x"})
    assert any(i["field"] == "nope" for i in e2.value.issues)

    with pytest.raises(dm.ValidationError) as e3:
        dm.build_submission(form, {"email": "a@b.com", "total": 5})
    assert any(i["field"] == "total" and "read-only" in i["message"] for i in e3.value.issues)

    with pytest.raises(dm.ValidationError) as e4:
        dm.build_submission(form, {"email": "a@b.com", "topic": "marketing"})
    assert any(i["field"] == "topic" for i in e4.value.issues)


def test_tampered_form_fails_hash_check():
    data, _ = make_dmf(tamper_form=True)
    with pytest.raises(DmfError):
        dm.read_form(data)


def test_share_only_cannot_submit():
    data, _ = make_dmf(with_recipient=False)
    form = dm.read_form(data)
    assert form.recipient_public_key is None
    with pytest.raises(dm.DataMakerError) as e:
        dm.build_submission(form, {"email": "a@b.com"})
    assert e.value.code == "NO_RECIPIENT"


def test_submit_posts_and_returns_result():
    data, _ = make_dmf()
    captured = {}

    def fake_poster(url, body, headers):
        captured["url"] = url
        captured["body"] = json.loads(body)
        return 200, json.dumps({"submissionId": "srv-id", "editToken": "tok-abc"})

    result = dm.submit(dmf=data, values={"email": "ada@example.com"}, api_base_url="https://example.test/", poster=fake_poster)
    assert captured["url"] == "https://example.test/submissions"
    assert captured["body"]["formId"] == "contact_form"
    assert captured["body"]["ciphertext"]
    assert result["edit_token"] == "tok-abc"
    assert result["form_id"] == "contact_form"


def test_submit_raises_on_non_2xx():
    data, _ = make_dmf()

    def fake_poster(url, body, headers):
        return 413, "too big"

    with pytest.raises(dm.SubmissionError) as e:
        dm.submit(dmf=data, values={"email": "a@b.com"}, poster=fake_poster)
    assert e.value.status == 413
