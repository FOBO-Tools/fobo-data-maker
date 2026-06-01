<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/**
 * Sealed-box crypto for DataMaker submissions, via PHP's ext-sodium.
 * Mirrors the desktop's DataMaker.Sync.Shared.SealedBox (libsodium
 * crypto_box_seal — X25519 + XSalsa20-Poly1305) so the form owner decrypts
 * with their X25519 private half. base64 is standard-with-padding, matching
 * libsodium's base64_variants.ORIGINAL.
 */
final class Crypto
{
    public const CRYPTO_BOX_PUBLICKEYBYTES  = 32;
    public const CRYPTO_SIGN_PUBLICKEYBYTES = 32;
    public const CRYPTO_SIGN_BYTES          = 64;

    /**
     * Sealed-box encrypt $plaintext against the recipient X25519 public key.
     * Returns std-base64 ciphertext for SubmissionEnvelope.ciphertext.
     */
    public static function seal(string $plaintext, string $recipientPubkeyB64): string
    {
        $pk = self::b64decode($recipientPubkeyB64, 'recipient pubkey');
        if (strlen($pk) !== self::CRYPTO_BOX_PUBLICKEYBYTES) {
            throw new DmfError('recipient pubkey must be ' . self::CRYPTO_BOX_PUBLICKEYBYTES . ' bytes, got ' . strlen($pk));
        }
        return base64_encode(sodium_crypto_box_seal($plaintext, $pk));
    }

    public static function sha256Hex(string $data): string
    {
        return hash('sha256', $data);
    }

    /** Verify a detached Ed25519 signature over $message. */
    public static function verifyEd25519(string $message, string $signature, string $signerPubkeyB64): bool
    {
        $pub = self::b64decode($signerPubkeyB64, 'signer pubkey');
        if (strlen($pub) !== self::CRYPTO_SIGN_PUBLICKEYBYTES) {
            throw new DmfError('signer pubkey must be ' . self::CRYPTO_SIGN_PUBLICKEYBYTES . ' bytes, got ' . strlen($pub));
        }
        if (strlen($signature) !== self::CRYPTO_SIGN_BYTES) {
            throw new DmfError('signature must be ' . self::CRYPTO_SIGN_BYTES . ' bytes, got ' . strlen($signature));
        }
        return sodium_crypto_sign_verify_detached($signature, $message, $pub);
    }

    public static function b64decode(string $s, string $label = 'value'): string
    {
        $bytes = base64_decode($s, true);
        if ($bytes === false) {
            throw new DmfError("invalid base64 in {$label}");
        }
        return $bytes;
    }
}
