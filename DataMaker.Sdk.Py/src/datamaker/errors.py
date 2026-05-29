"""Typed errors so callers can branch on ``err.code``."""

from __future__ import annotations

from typing import List, Optional


class DataMakerError(Exception):
    def __init__(self, message: str, code: str) -> None:
        super().__init__(message)
        self.code = code


class DmfError(DataMakerError):
    """.dmf bundle malformed, unsigned, tampered, or share-only."""

    def __init__(self, message: str) -> None:
        super().__init__(message, "DMF_INVALID")


class ValidationError(DataMakerError):
    """Supplied values failed schema validation. ``issues`` is a list of dicts
    ``{"field", "kind", "message"}``."""

    def __init__(self, issues: List[dict]) -> None:
        summary = "; ".join(f"{i['field']}: {i['message']}" for i in issues)
        super().__init__(f"submission values are invalid — {summary}", "VALIDATION_FAILED")
        self.issues = issues


class SubmissionError(DataMakerError):
    """/submissions returned a non-2xx."""

    def __init__(self, status: int, body: Optional[str]) -> None:
        super().__init__(f"submission rejected by the server (HTTP {status})", "SUBMISSION_REJECTED")
        self.status = status
        self.body = body
