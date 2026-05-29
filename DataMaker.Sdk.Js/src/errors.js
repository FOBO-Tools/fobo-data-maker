'use strict';

// Typed errors so callers can branch on `err.code` instead of string-matching.

class DataMakerError extends Error {
  constructor(message, code) {
    super(message);
    this.name = 'DataMakerError';
    this.code = code;
  }
}

// .dmf bundle malformed, unsigned, tampered, or share-only (no recipient).
class DmfError extends DataMakerError {
  constructor(message) {
    super(message, 'DMF_INVALID');
    this.name = 'DmfError';
  }
}

// Supplied values failed schema validation. `.issues` is an array of
// { field, kind, message } so callers can surface field-level feedback.
class ValidationError extends DataMakerError {
  constructor(issues) {
    const summary = issues.map((i) => `${i.field}: ${i.message}`).join('; ');
    super(`submission values are invalid — ${summary}`, 'VALIDATION_FAILED');
    this.name = 'ValidationError';
    this.issues = issues;
  }
}

// The /submissions endpoint returned a non-2xx. `.status` + `.body` carry
// the HTTP detail.
class SubmissionError extends DataMakerError {
  constructor(status, body) {
    super(`submission rejected by the server (HTTP ${status})`, 'SUBMISSION_REJECTED');
    this.name = 'SubmissionError';
    this.status = status;
    this.body = body;
  }
}

module.exports = { DataMakerError, DmfError, ValidationError, SubmissionError };
