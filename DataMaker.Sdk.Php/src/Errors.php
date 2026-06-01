<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/**
 * Base SDK error. Branch on {@see $errorCode} (a stable string) — PHP's
 * built-in Exception::getCode() is an int and reserved, so the SDK's
 * machine-readable code lives in $errorCode (mirrors the JS/Python `.code`).
 */
class DataMakerError extends \Exception
{
    public string $errorCode;

    public function __construct(string $message, string $errorCode)
    {
        parent::__construct($message);
        $this->errorCode = $errorCode;
    }
}

/** .dmf bundle malformed, unsigned, tampered, or share-only. */
class DmfError extends DataMakerError
{
    public function __construct(string $message)
    {
        parent::__construct($message, 'DMF_INVALID');
    }
}

/**
 * Supplied values failed schema validation.
 * @var array<int,array{field:string,kind:?string,message:string}> $issues
 */
class ValidationError extends DataMakerError
{
    /** @var array<int,array{field:string,kind:?string,message:string}> */
    public array $issues;

    /** @param array<int,array{field:string,kind:?string,message:string}> $issues */
    public function __construct(array $issues)
    {
        $summary = implode('; ', array_map(
            static fn (array $i): string => $i['field'] . ': ' . $i['message'],
            $issues
        ));
        parent::__construct("submission values are invalid — {$summary}", 'VALIDATION_FAILED');
        $this->issues = $issues;
    }
}

/** /submissions returned a non-2xx. */
class SubmissionError extends DataMakerError
{
    public int $status;
    public ?string $body;

    public function __construct(int $status, ?string $body)
    {
        parent::__construct("submission rejected by the server (HTTP {$status})", 'SUBMISSION_REJECTED');
        $this->status = $status;
        $this->body = $body;
    }
}
