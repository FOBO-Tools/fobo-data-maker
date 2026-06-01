<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/** A verified .dmf form, ready to submit against. */
final class FormDescriptor
{
    public string $formId;
    public string $name;
    public ?string $description;
    public int $schemaVersion;
    public ?string $submitPolicy;
    public ?string $recipientUserId;
    public ?string $recipientPublicKey;
    /** @var array{publicKey:?string,identity:?array}|null */
    public ?array $signer;
    public int $envelopeVersion;
    /** @var array<int,array<string,mixed>> */
    public array $fields;
    public bool $verified;
    /** @var array{isVerified:bool,email:?string,company:?string,sub:?string,expiresAt:?string}|null */
    public ?array $foboVerification;

    /** @param array<string,mixed> $d */
    public function __construct(array $d)
    {
        $this->formId             = (string) ($d['formId'] ?? '');
        $this->name               = (string) ($d['name'] ?? '');
        $this->description        = $d['description'] ?? null;
        $this->schemaVersion      = (int) ($d['schemaVersion'] ?? 1);
        $this->submitPolicy       = $d['submitPolicy'] ?? null;
        $this->recipientUserId    = $d['recipientUserId'] ?? null;
        $this->recipientPublicKey = $d['recipientPublicKey'] ?? null;
        $this->signer             = $d['signer'] ?? null;
        $this->envelopeVersion    = (int) ($d['envelopeVersion'] ?? 0);
        $this->fields             = $d['fields'] ?? [];
        $this->verified           = (bool) ($d['verified'] ?? false);
        $this->foboVerification   = $d['foboVerification'] ?? null;
    }

    /** True when a valid FOBO attestation chains the signer's key to a verified email. */
    public function isFoboVerified(): bool
    {
        return $this->foboVerification !== null;
    }

    /** True when the form can receive sealed-box submissions (not share-only). */
    public function acceptsSubmissions(): bool
    {
        return !empty($this->recipientPublicKey) && !empty($this->recipientUserId);
    }
}
