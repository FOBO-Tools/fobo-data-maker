<?php
namespace DataMaker\Forms\Renderer\WordPress;

if (!defined('ABSPATH')) exit;

/**
 * Parses a .dmf bundle uploaded to WordPress. Mirrors the C# DmfReader
 * verification chain: parse manifest → optional Ed25519 signature check →
 * SHA-256 every file the manifest lists → extract form.json + the v3 extras
 * (compiled.json, elementCss.json, palette.css).
 *
 * Signature verification is optional and controlled by the WP admin setting.
 * Default off — admins who don't yet have a signer pubkey configured can
 * still upload forms; turn it on to enforce trust.
 */
final class DmfReader
{
    public const ENVELOPE_VERSION   = 3;
    public const FILE_MANIFEST      = 'manifest.json';
    public const FILE_SIGNATURE     = 'signature.bin';
    public const FILE_FORM          = 'form.json';
    public const FILE_COMPILED      = 'compiled.json';
    public const FILE_ELEMENT_CSS   = 'elementCss.json';
    public const FILE_PALETTE       = 'palette.css';
    public const FILE_FONTS         = 'fonts.css';

    /** FOBO root Ed25519 pubkey (base64). Fingerprint 50:be:66:c4:fb:a6:b0:12. */
    public const FOBO_ROOT_PUBKEY_B64 = 'K2XDkrQ3vn5FSzbohodSMGiSrfomXg9/bgfczFxEGh4=';

    /** Hard cap on the inflated size of any individual zip entry. Prevents
     *  zip-bomb uploads (1 KB compressed → 100 MB inflated) from OOM-ing
     *  the PHP worker. 16 MB is far above any legitimate font/asset blob. */
    private const MAX_ENTRY_BYTES = 16 * 1024 * 1024;

    public static function read(string $zip_bytes, bool $verify_signature = false, ?string $expected_signer_pubkey_b64 = null): array
    {
        $tmp = tempnam(sys_get_temp_dir(), 'dmf');
        if ($tmp === false) {
            throw new \RuntimeException('Could not allocate temp file for .dmf parse.');
        }
        if (file_put_contents($tmp, $zip_bytes) === false) {
            @unlink($tmp);
            throw new \RuntimeException('Could not write .dmf to temp file.');
        }

        $zip = new \ZipArchive();
        if ($zip->open($tmp) !== true) {
            @unlink($tmp);
            throw new \RuntimeException('File is not a valid zip archive — is this really a .dmf?');
        }

        try {
            $manifest_bytes  = self::read_entry($zip, self::FILE_MANIFEST);
            $signature_bytes = self::read_entry($zip, self::FILE_SIGNATURE);

            $manifest = json_decode($manifest_bytes, true);
            if (!is_array($manifest)) {
                throw new \RuntimeException('Manifest is not valid JSON.');
            }

            $envelope_version = $manifest['envelopeVersion'] ?? null;
            if ($envelope_version !== self::ENVELOPE_VERSION) {
                throw new \RuntimeException(sprintf(
                    'Unsupported envelope version %s; this plugin understands v%d.',
                    var_export($envelope_version, true), self::ENVELOPE_VERSION
                ));
            }

            $signer_pubkey_b64 = $manifest['signer']['publicKey'] ?? '';
            $signer_pubkey     = self::b64_decode_strict($signer_pubkey_b64);

            if ($verify_signature) {
                if (!function_exists('sodium_crypto_sign_verify_detached')) {
                    throw new \RuntimeException('libsodium PHP extension required for signature verification.');
                }
                if ($expected_signer_pubkey_b64) {
                    $expected = self::b64_decode_strict($expected_signer_pubkey_b64);
                    if (!hash_equals($expected, $signer_pubkey)) {
                        throw new \RuntimeException('Signer key does not match the configured pubkey.');
                    }
                }
                $ok = sodium_crypto_sign_verify_detached($signature_bytes, $manifest_bytes, $signer_pubkey);
                if (!$ok) {
                    throw new \RuntimeException('Manifest signature is invalid — bytes were tampered with or the signer key does not match.');
                }
            }

            $form_bytes     = null;
            $compiled_bytes = null;
            $css_bytes      = null;
            $palette_bytes  = null;
            $fonts_bytes    = null;
            $images         = array(); // 'images/{hash}.{ext}' => raw bytes

            foreach (($manifest['files'] ?? []) as $entry) {
                $path   = $entry['path']   ?? '';
                $sha    = $entry['sha256'] ?? '';
                $size   = $entry['size']   ?? 0;
                $bytes  = self::read_entry($zip, $path);
                if (strlen($bytes) !== (int)$size) {
                    throw new \RuntimeException("File '{$path}' size mismatch.");
                }
                $actual = hash('sha256', $bytes);
                if (!hash_equals(strtolower($sha), $actual)) {
                    throw new \RuntimeException("File '{$path}' SHA-256 mismatch.");
                }
                switch ($path) {
                    case self::FILE_FORM:        $form_bytes     = $bytes; break;
                    case self::FILE_COMPILED:    $compiled_bytes = $bytes; break;
                    case self::FILE_ELEMENT_CSS: $css_bytes      = $bytes; break;
                    case self::FILE_PALETTE:     $palette_bytes  = $bytes; break;
                    case self::FILE_FONTS:       $fonts_bytes    = $bytes; break;
                    default:
                        if (strncmp($path, 'images/', 7) === 0) {
                            $images[$path] = $bytes;
                        }
                        break;
                }
            }

            if ($form_bytes === null) {
                throw new \RuntimeException('Bundle has no form.json payload.');
            }

            $form = json_decode($form_bytes, true);
            if (!is_array($form)) {
                throw new \RuntimeException('form.json is not valid JSON.');
            }

            // Rehydrate dmf:images/… refs back to inline data URIs from the
            // (already hash-verified) bundle image files, so the renderer sees
            // ordinary inline images. No-op when the bundle has no lifted images.
            if ($images) {
                $form = self::rehydrate_images($form, $images);
            }

            // FOBO attestation (optional) — its own chain, verified against the
            // root pubkey regardless of $verify_signature. Null when absent or
            // it fails to verify; the form is still self-signed-usable.
            $fobo = self::verify_fobo_attestation(
                $manifest['signer']['foboAttestation'] ?? null,
                $signer_pubkey_b64
            );

            return [
                'form'             => $form,
                'compiled'         => $compiled_bytes ? (json_decode($compiled_bytes, true) ?: new \stdClass()) : new \stdClass(),
                'element_css'      => $css_bytes      ? (json_decode($css_bytes, true)      ?: new \stdClass()) : new \stdClass(),
                'palette_css'      => $palette_bytes  ? (string)$palette_bytes : '',
                'fonts_css'        => $fonts_bytes    ? (string)$fonts_bytes   : '',
                'schema_version'   => (int)($form['schemaVersion'] ?? 1),
                'form_id'          => (string)($form['id'] ?? ''),
                'signature_verified' => (bool)$verify_signature,
                'signer_pubkey'    => $signer_pubkey_b64,
                'recipient'        => $manifest['recipient'] ?? null,
                'fobo'             => $fobo,
            ];
        } finally {
            $zip->close();
            @unlink($tmp);
        }
    }

    /**
     * Recursively replace every `dmf:images/{hash}.{ext}` string in the decoded
     * form with a `data:` URI built from the bundle's (hash-verified) image
     * bytes. Unknown refs and http(s)/data: sources are left untouched.
     *
     * @param mixed $node
     * @param array<string,string> $images  'images/{hash}.{ext}' => raw bytes
     * @return mixed
     */
    private static function rehydrate_images($node, array $images)
    {
        if (is_array($node)) {
            foreach ($node as $key => $value) {
                $node[$key] = self::rehydrate_images($value, $images);
            }
            return $node;
        }
        if (is_string($node) && strncmp($node, 'dmf:images/', 11) === 0) {
            $path = substr($node, 4); // drop "dmf:" → "images/{hash}.{ext}"
            if (isset($images[$path])) {
                $ext  = strtolower(pathinfo($path, PATHINFO_EXTENSION));
                $mime = self::mime_for_ext($ext);
                return 'data:' . $mime . ';base64,' . base64_encode($images[$path]);
            }
        }
        return $node;
    }

    /**
     * Verify a manifest's signer.foboAttestation against the FOBO root pubkey.
     * Returns ['is_verified','email','company','sub','expires_at'] on success,
     * or null (bad signature, unknown version, pubkey mismatch, expired, or
     * malformed / libsodium unavailable).
     *
     * @param mixed $attestation
     */
    private static function verify_fobo_attestation($attestation, string $signer_pubkey_b64): ?array
    {
        if (!is_array($attestation)) {
            return null;
        }
        $payload_json = $attestation['payloadJson']     ?? null;
        $sig_b64      = $attestation['signatureBase64'] ?? null;
        if (!is_string($payload_json) || !is_string($sig_b64)) {
            return null;
        }
        if (!function_exists('sodium_crypto_sign_verify_detached')) {
            return null;
        }

        $sig  = base64_decode($sig_b64, true);
        $root = base64_decode(self::FOBO_ROOT_PUBKEY_B64, true);
        if ($sig === false || $root === false || strlen($sig) !== SODIUM_CRYPTO_SIGN_BYTES) {
            return null;
        }

        try {
            $ok = sodium_crypto_sign_verify_detached($sig, $payload_json, $root);
        } catch (\Throwable $e) {
            return null;
        }
        if (!$ok) {
            return null;
        }

        $p = json_decode($payload_json, true);
        if (!is_array($p)) {
            return null;
        }
        $ver = $p['attestationVersion'] ?? 0;
        if (!is_int($ver) || $ver < 1 || $ver > 2) {
            return null;
        }
        // FOBO must vouch for the same key that signed the form.
        if (($p['subjectPublicKey'] ?? null) !== $signer_pubkey_b64) {
            return null;
        }

        $expires = $p['expiresAt'] ?? null;
        if (is_string($expires)) {
            $exp_ts = strtotime($expires);
            if ($exp_ts !== false && time() >= $exp_ts) {
                return null;
            }
        }

        return [
            'is_verified' => true,
            'email'       => $p['subjectEmail']   ?? null,
            'company'     => $p['subjectCompany'] ?? null,
            'sub'         => $p['subjectSub']     ?? null,
            'expires_at'  => $expires,
        ];
    }

    private static function mime_for_ext(string $ext): string
    {
        switch ($ext) {
            case 'png':  return 'image/png';
            case 'jpg':  return 'image/jpeg';
            case 'jpeg': return 'image/jpeg';
            case 'gif':  return 'image/gif';
            case 'webp': return 'image/webp';
            case 'svg':  return 'image/svg+xml';
            case 'bmp':  return 'image/bmp';
            case 'ico':  return 'image/x-icon';
            case 'avif': return 'image/avif';
            default:     return 'application/octet-stream';
        }
    }

    private static function read_entry(\ZipArchive $zip, string $path): string
    {
        // Bomb defense: check the central-directory size BEFORE inflating
        // so a 1 KB compressed entry that expands to 1 GB never reaches
        // getFromName(). statName returns false when the entry is missing,
        // which keeps the original "missing required entry" error.
        $stat = $zip->statName($path);
        if ($stat === false) {
            throw new \RuntimeException("Bundle is missing required entry '{$path}'.");
        }
        if (isset($stat['size']) && (int)$stat['size'] > self::MAX_ENTRY_BYTES) {
            throw new \RuntimeException("Entry '{$path}' exceeds the maximum allowed size.");
        }
        $bytes = $zip->getFromName($path);
        if ($bytes === false) {
            throw new \RuntimeException("Bundle is missing required entry '{$path}'.");
        }
        return $bytes;
    }

    private static function b64_decode_strict(string $s): string
    {
        $bytes = base64_decode($s, true);
        if ($bytes === false) {
            throw new \RuntimeException('Invalid base64 in manifest.');
        }
        return $bytes;
    }
}
