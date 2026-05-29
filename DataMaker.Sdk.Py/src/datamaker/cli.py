"""CLI for the datamaker package.

    datamaker inspect <form.dmf> [--json] [--no-verify]
    datamaker submit  <form.dmf> [--field k=v ...] [--data '<json>'] [--data-file f.json]
                                 [--api URL] [--submitter ID] [--dry-run]
                                 [--no-verify] [--no-validate]
"""

from __future__ import annotations

import argparse
import dataclasses
import json
import sys
from typing import Any, Dict

from . import read_form, build_submission, submit, normalize_kind
from .errors import ValidationError


def main(argv=None) -> int:
    argv = list(sys.argv[1:] if argv is None else argv)
    parser = argparse.ArgumentParser(prog="datamaker", description="Submit records to a Data Maker form")
    sub = parser.add_subparsers(dest="command", required=True)

    p_inspect = sub.add_parser("inspect", help="Show a .dmf's fields, recipient, and signature status")
    p_inspect.add_argument("dmf")
    p_inspect.add_argument("--json", action="store_true")
    p_inspect.add_argument("--no-verify", action="store_true")

    p_submit = sub.add_parser("submit", help="Submit a record to a form")
    p_submit.add_argument("dmf")
    p_submit.add_argument("--field", action="append", default=[], metavar="k=v")
    p_submit.add_argument("--data")
    p_submit.add_argument("--data-file")
    p_submit.add_argument("--api")
    p_submit.add_argument("--submitter")
    p_submit.add_argument("--dry-run", action="store_true")
    p_submit.add_argument("--no-verify", action="store_true")
    p_submit.add_argument("--no-validate", action="store_true")

    args = parser.parse_args(argv)
    try:
        if args.command == "inspect":
            return _cmd_inspect(args)
        if args.command == "submit":
            return _cmd_submit(args)
    except ValidationError as e:
        print(f"error: {e}", file=sys.stderr)
        for i in e.issues:
            print(f"  - {i['field']}: {i['message']}", file=sys.stderr)
        return 1
    except Exception as e:  # noqa: BLE001 — CLI top-level: surface a clean message
        print(f"error: {e}", file=sys.stderr)
        return 1
    return 0


def _read_bytes(path: str) -> bytes:
    with open(path, "rb") as fh:
        return fh.read()


def _cmd_inspect(args) -> int:
    form = read_form(_read_bytes(args.dmf), verify=not args.no_verify)
    if args.json:
        print(json.dumps(dataclasses.asdict(form), indent=2))
        return 0

    lines = [
        f"Form:        {form.name}  ({form.form_id})",
    ]
    if form.description:
        lines.append(f"Description:  {form.description}")
    lines.append(f"Version:     schema v{form.schema_version}  ·  envelope v{form.envelope_version}")
    lines.append(f"Submit:      {form.submit_policy or 'unspecified'}")
    lines.append(f"Signature:   {'verified' if form.verified else 'NOT verified (--no-verify)'}")
    lines.append(
        f"Recipient:   {form.recipient_user_id}"
        if form.recipient_public_key
        else "Recipient:   none — share-only bundle, cannot receive submissions"
    )
    lines.append("")
    lines.append("Fields:")
    for f in form.fields:
        req = " *required" if f.get("required") else ""
        ch = "  [" + ", ".join(c["value"] for c in f["choices"]) + "]" if f.get("choices") else ""
        lines.append(f"  {f['key']:<24} {normalize_kind(f['kind']):<14} {f['label']}{req}{ch}")
    print("\n".join(lines))
    return 0


def _collect_values(args) -> Dict[str, Any]:
    values: Dict[str, Any] = {}
    if args.data_file:
        with open(args.data_file, "r", encoding="utf-8") as fh:
            values.update(_parse_obj(fh.read()))
    if args.data:
        values.update(_parse_obj(args.data))
    for pair in args.field:
        if "=" not in pair:
            raise ValueError(f'--field must be key=value, got "{pair}"')
        key, val = pair.split("=", 1)
        if key in values:
            values[key] = values[key] + [val] if isinstance(values[key], list) else [values[key], val]
        else:
            values[key] = val
    return values


def _parse_obj(text: str) -> dict:
    obj = json.loads(text)
    if not isinstance(obj, dict):
        raise ValueError("expected a JSON object")
    return obj


def _cmd_submit(args) -> int:
    form = read_form(_read_bytes(args.dmf), verify=not args.no_verify)
    values = _collect_values(args)
    opts = dict(validate=not args.no_validate, submitter_id=args.submitter)

    if args.dry_run:
        built = build_submission(form, values, **opts)
        print(json.dumps({"payload": built["payload"], "envelope": built["envelope"]}, indent=2))
        return 0

    kwargs = dict(form=form, values=values, **opts)
    if args.api:
        kwargs["api_base_url"] = args.api
    result = submit(**kwargs)
    print(json.dumps(result, indent=2))
    return 0


if __name__ == "__main__":
    sys.exit(main())
