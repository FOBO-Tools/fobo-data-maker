<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/**
 * Verifies a publisher's FOBO attestation — the Ed25519-signed binding of their
 * signing key to a FOBO-verified email (and, when present, an admin-verified
 * company). Mirrors the desktop's DataMaker.Forms.Signing.FoboTrustRoot; the
 * root pubkey is duplicated here intentionally (sender-side, offline-verifiable).
 */
final class FoboTrustRoot
{
    /** FOBO root Ed25519 pubkey (base64). Fingerprint 50:be:66:c4:fb:a6:b0:12. */
    public const PUBLIC_KEY_B64 = 'K2XDkrQ3vn5FSzbohodSMGiSrfomXg9/bgfczFxEGh4=';

    private const MIN_VERSION = 1;
    private const MAX_VERSION = 2;

    /**
     * Verify a manifest's signer.foboAttestation against the FOBO root.
     * Returns ['isVerified','email','company','sub','expiresAt'] on success, or
     * null (bad signature, unknown version, pubkey mismatch, expired, or
     * malformed). Failure is not fatal — the form is still self-signed-usable.
     *
     * @param mixed $attestation
     */
    public static function verify($attestation, string $signerPublicKeyB64): ?array
    {
        if (!is_array($attestation)) {
            return null;
        }
        $payloadJson = $attestation['payloadJson']     ?? null;
        $sigB64      = $attestation['signatureBase64'] ?? null;
        if (!is_string($payloadJson) || !is_string($sigB64)) {
            return null;
        }

        $sig  = base64_decode($sigB64, true);
        $root = base64_decode(self::PUBLIC_KEY_B64, true);
        if ($sig === false || $root === false || strlen($sig) !== Crypto::CRYPTO_SIGN_BYTES) {
            return null;
        }

        try {
            $ok = sodium_crypto_sign_verify_detached($sig, $payloadJson, $root);
        } catch (\Throwable $e) {
            return null;
        }
        if (!$ok) {
            return null;
        }

        $p = json_decode($payloadJson, true);
        if (!is_array($p)) {
            return null;
        }
        $ver = $p['attestationVersion'] ?? 0;
        if (!is_int($ver) || $ver < self::MIN_VERSION || $ver > self::MAX_VERSION) {
            return null;
        }
        // FOBO must vouch for the same key that signed the form.
        if (($p['subjectPublicKey'] ?? null) !== $signerPublicKeyB64) {
            return null;
        }

        $expires = $p['expiresAt'] ?? null;
        if (is_string($expires)) {
            $expTs = strtotime($expires);
            if ($expTs !== false && time() >= $expTs) {
                return null;
            }
        }

        return [
            'isVerified' => true,
            'email'      => $p['subjectEmail']   ?? null,
            'company'    => $p['subjectCompany'] ?? null,
            'sub'        => $p['subjectSub']     ?? null,
            'expiresAt'  => $expires,
        ];
    }
}
