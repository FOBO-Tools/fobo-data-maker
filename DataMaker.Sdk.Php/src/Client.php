<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/**
 * Build + post a DataMaker submission. The wire envelope/payload are byte-exact
 * to DataMaker.Sync.Shared (SubmissionEnvelope / SubmissionPayload), so JSON
 * keys are camelCase. Mirrors the Python/JS submit modules.
 */
final class Client
{
    public const DEFAULT_API_BASE_URL = 'https://datamaker-api.fobo-tools.com';

    /** UUID v4 with dashes stripped — matches the desktop's GUID 'n' format. */
    public static function newSubmissionId(): string
    {
        $d = random_bytes(16);
        $d[6] = chr((ord($d[6]) & 0x0f) | 0x40); // version 4
        $d[8] = chr((ord($d[8]) & 0x3f) | 0x80); // variant
        return bin2hex($d);
    }

    /**
     * Validate + seal a submission without sending.
     *
     * @param array<string,mixed> $values
     * @param array{validate?:bool,allowUnknown?:bool,submitterId?:?string,submissionId?:?string,submittedAt?:?string} $opts
     * @return array{submissionId:string,envelope:array<string,mixed>,payload:array<string,mixed>,values:array<string,mixed>}
     */
    public static function buildSubmission(FormDescriptor $form, array $values, array $opts = []): array
    {
        if (empty($form->recipientPublicKey)) {
            throw new DataMakerError('form has no recipient public key — share-only bundles cannot receive submissions', 'NO_RECIPIENT');
        }

        $outValues = $values;
        if ($opts['validate'] ?? true) {
            $res = Validator::validateValues($form->fields, $values, $opts['allowUnknown'] ?? false);
            if ($res['issues']) {
                throw new ValidationError($res['issues']);
            }
            $outValues = $res['values'];
        }

        $payload = [
            'values'      => (object) $outValues, // {} not [] when empty
            'submittedAt' => $opts['submittedAt'] ?? self::nowIso(),
            'formVersion' => $form->schemaVersion,
            'formSchema'  => '',
            'mode'        => 'Create',
        ];
        $ciphertext = Crypto::seal(self::compactJson($payload), $form->recipientPublicKey);
        $sid = $opts['submissionId'] ?? self::newSubmissionId();

        $envelope = [
            'submissionId'   => $sid,
            'formId'         => $form->formId,
            // Recipient DESTINATION: the box public key the form is sealed to. The
            // server fingerprints it to route to the right database. Required.
            'recipientPubkey' => $form->recipientPublicKey,
            'submitterId'    => $opts['submitterId'] ?? null,
            'ciphertext'     => $ciphertext,
        ];

        return ['submissionId' => $sid, 'envelope' => $envelope, 'payload' => $payload, 'values' => $outValues];
    }

    /**
     * POST a built envelope to /submissions.
     *
     * @param array<string,mixed> $envelope
     * @param array{apiBaseUrl?:string,poster?:callable} $opts
     * @return array{submissionId:?string,editToken:?string}
     */
    public static function postSubmission(array $envelope, array $opts = []): array
    {
        $base = rtrim($opts['apiBaseUrl'] ?? self::DEFAULT_API_BASE_URL, '/');
        $url = $base . '/submissions';
        $body = self::compactJson($envelope);
        $headers = ['Content-Type: application/json'];

        $poster = $opts['poster'] ?? [self::class, 'curlPost'];
        [$status, $text] = $poster($url, $body, $headers);

        if ($status < 200 || $status >= 300) {
            throw new SubmissionError($status, $text);
        }
        $data = json_decode($text, true);
        if (!is_array($data)) {
            throw new SubmissionError($status, "unparseable response body: {$text}");
        }
        return ['submissionId' => $data['submissionId'] ?? null, 'editToken' => $data['editToken'] ?? null];
    }

    /**
     * One-shot: read a .dmf (or take a pre-parsed form), validate + seal values,
     * and post.
     *
     * @param array{form?:FormDescriptor,dmf?:string,values:array<string,mixed>,apiBaseUrl?:string,validate?:bool,allowUnknown?:bool,submitterId?:?string,verify?:bool,poster?:callable} $opts
     * @return array{submissionId:?string,editToken:?string,formId:string}
     */
    public static function submit(array $opts): array
    {
        $form = $opts['form'] ?? null;
        if (!$form instanceof FormDescriptor) {
            if (empty($opts['dmf'])) {
                throw new DataMakerError('submit requires either form or dmf', 'NO_FORM');
            }
            $form = DmfReader::read($opts['dmf'], $opts['verify'] ?? true);
        }

        $built = self::buildSubmission($form, $opts['values'] ?? [], [
            'validate'     => $opts['validate'] ?? true,
            'allowUnknown' => $opts['allowUnknown'] ?? false,
            'submitterId'  => $opts['submitterId'] ?? null,
        ]);
        $postOpts = ['apiBaseUrl' => $opts['apiBaseUrl'] ?? self::DEFAULT_API_BASE_URL];
        if (isset($opts['poster'])) {
            $postOpts['poster'] = $opts['poster'];
        }
        $result = self::postSubmission($built['envelope'], $postOpts);
        $result['formId'] = $form->formId;
        return $result;
    }

    /** @param array<string,mixed> $headers */
    private static function curlPost(string $url, string $body, array $headers): array
    {
        $ch = curl_init($url);
        curl_setopt_array($ch, [
            CURLOPT_POST           => true,
            CURLOPT_POSTFIELDS     => $body,
            CURLOPT_HTTPHEADER     => $headers,
            CURLOPT_RETURNTRANSFER => true,
            CURLOPT_TIMEOUT        => 30,
        ]);
        $text = curl_exec($ch);
        if ($text === false) {
            $err = curl_error($ch);
            curl_close($ch);
            throw new SubmissionError(0, "network error: {$err}");
        }
        $status = (int) curl_getinfo($ch, CURLINFO_RESPONSE_CODE);
        curl_close($ch);
        return [$status, (string) $text];
    }

    /** @param mixed $value */
    private static function compactJson($value): string
    {
        return (string) json_encode($value, JSON_UNESCAPED_SLASHES);
    }

    private static function nowIso(): string
    {
        return gmdate('Y-m-d\TH:i:s') . '.' . sprintf('%03d', (int) (microtime(true) * 1000) % 1000) . 'Z';
    }
}
