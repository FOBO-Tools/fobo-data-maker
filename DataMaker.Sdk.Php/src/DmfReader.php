<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/**
 * Reader/verifier for the .dmf signed-bundle format
 * (DataMaker.Forms.Signing.DmfFormat). A .dmf is a ZIP of manifest.json (the
 * Ed25519-signed bytes), signature.bin, form.json, plus optional render assets
 * bound by SHA-256 in manifest.files[]. Verifies the manifest signature, the
 * form.json hash, and the FOBO attestation, then returns a sender-side
 * descriptor to submit against. Mirrors the Python/JS SDK readers.
 */
final class DmfReader
{
    private const FILE_MANIFEST  = 'manifest.json';
    private const FILE_SIGNATURE = 'signature.bin';
    private const FILE_FORM      = 'form.json';

    /** Hard cap on the inflated size of any single zip entry (zip-bomb guard). */
    private const MAX_ENTRY_BYTES = 16 * 1024 * 1024;

    /**
     * Parse + verify a .dmf. $verify gates the Ed25519 + form-hash checks
     * (disable only for trusted local fixtures). The FOBO attestation is
     * verified independently of $verify.
     */
    public static function read(string $dmfBytes, bool $verify = true): FormDescriptor
    {
        $tmp = tempnam(sys_get_temp_dir(), 'dmf');
        if ($tmp === false || file_put_contents($tmp, $dmfBytes) === false) {
            throw new DmfError('could not buffer the .dmf for reading');
        }

        $zip = new \ZipArchive();
        if ($zip->open($tmp) !== true) {
            @unlink($tmp);
            throw new DmfError('not a readable .dmf archive');
        }

        try {
            $manifestBytes = self::readEntry($zip, self::FILE_MANIFEST);
            $formBytes     = self::readEntry($zip, self::FILE_FORM);

            $manifest = json_decode($manifestBytes, true);
            if (!is_array($manifest)) {
                throw new DmfError('.dmf manifest.json is not valid JSON');
            }

            $signer = $manifest['signer'] ?? null;
            $signerPubB64 = is_array($signer) ? (string) ($signer['publicKey'] ?? '') : '';

            if ($verify) {
                if ($signerPubB64 === '') {
                    throw new DmfError('.dmf manifest.signer.publicKey is missing — cannot verify');
                }
                $signatureBytes = self::readEntry($zip, self::FILE_SIGNATURE);
                if (!Crypto::verifyEd25519($manifestBytes, $signatureBytes, $signerPubB64)) {
                    throw new DmfError('.dmf signature is invalid — bundle was tampered with or signer key is wrong');
                }
                self::verifyFormHash($manifest, $formBytes);
            }

            $form = json_decode($formBytes, true);
            if (!is_array($form)) {
                throw new DmfError('.dmf form.json is not valid JSON');
            }

            // FOBO attestation (optional) — its own chain, verified regardless of $verify.
            $fobo = null;
            if ($signerPubB64 !== '' && is_array($signer)) {
                $fobo = FoboTrustRoot::verify($signer['foboAttestation'] ?? null, $signerPubB64);
            }

            $recipient = $manifest['recipient'] ?? null;

            return new FormDescriptor([
                'formId'             => $form['id'] ?? '',
                'name'               => $form['name'] ?? '',
                'description'        => $form['description'] ?? null,
                'schemaVersion'      => $form['schemaVersion'] ?? 1,
                'submitPolicy'       => $form['submitPolicy'] ?? null,
                'recipientUserId'    => is_array($recipient) ? ($recipient['userId'] ?? null) : null,
                'recipientPublicKey' => is_array($recipient) ? ($recipient['publicKey'] ?? null) : null,
                'signer'             => is_array($signer) ? ['publicKey' => $signer['publicKey'] ?? null, 'identity' => $signer['identity'] ?? null] : null,
                'envelopeVersion'    => $manifest['envelopeVersion'] ?? 0,
                'fields'             => self::extractFields($form),
                'verified'           => $verify,
                'foboVerification'   => $fobo,
            ]);
        } finally {
            $zip->close();
            @unlink($tmp);
        }
    }

    /**
     * Flatten form.fields[] into submit-ready descriptors. `key` is the storage
     * name (what the receiver indexes on); `label` is the caption.
     *
     * @param array<string,mixed> $form
     * @return array<int,array<string,mixed>>
     */
    public static function extractFields(array $form): array
    {
        $out = [];
        foreach (($form['fields'] ?? []) as $f) {
            if (!is_array($f)) {
                continue;
            }
            $name = (string) ($f['name'] ?? '');
            $entry = [
                'id'       => $f['id'] ?? '',
                'key'      => $name,
                'name'     => $name,
                'label'    => $f['label'] ?? $name,
                'kind'     => $f['kind'] ?? '',
                'required' => (bool) ($f['required'] ?? false),
            ];
            if (!empty($f['description'])) {
                $entry['description'] = $f['description'];
            }
            if (!empty($f['placeholder'])) {
                $entry['placeholder'] = $f['placeholder'];
            }
            $choice = $f['choice'] ?? null;
            $choices = is_array($choice) ? ($choice['choices'] ?? null) : null;
            if (is_array($choices) && $choices !== []) {
                $entry['choices'] = array_map(static function ($c): array {
                    $v = $c['value'] ?? '';
                    return ['value' => $v, 'label' => $c['label'] ?? $v];
                }, $choices);
                $entry['allowCustom'] = (bool) ($choice['allowCustom'] ?? false);
            }
            $out[] = $entry;
        }
        return $out;
    }

    /** @param array<string,mixed> $manifest */
    private static function verifyFormHash(array $manifest, string $formBytes): void
    {
        $digest = Crypto::sha256Hex($formBytes);
        foreach (($manifest['files'] ?? []) as $f) {
            if (is_array($f) && ($f['path'] ?? null) === self::FILE_FORM) {
                if (strtolower((string) ($f['sha256'] ?? '')) !== $digest) {
                    throw new DmfError('.dmf form.json hash mismatch — bundle was tampered with');
                }
                return;
            }
        }
        throw new DmfError('.dmf manifest does not list form.json — bundle is malformed');
    }

    private static function readEntry(\ZipArchive $zip, string $path): string
    {
        $stat = $zip->statName($path);
        if ($stat === false) {
            throw new DmfError("bundle is missing required entry '{$path}'");
        }
        if (isset($stat['size']) && (int) $stat['size'] > self::MAX_ENTRY_BYTES) {
            throw new DmfError("entry '{$path}' exceeds the maximum allowed size");
        }
        $bytes = $zip->getFromName($path);
        if ($bytes === false) {
            throw new DmfError("bundle is missing required entry '{$path}'");
        }
        return $bytes;
    }
}
