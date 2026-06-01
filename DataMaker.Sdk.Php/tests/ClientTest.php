<?php

declare(strict_types=1);

namespace DataMaker\Sdk\Tests;

use DataMaker\Sdk\Client;
use DataMaker\Sdk\FieldKinds;
use DataMaker\Sdk\FoboTrustRoot;
use DataMaker\Sdk\FormDescriptor;
use DataMaker\Sdk\Validator;
use DataMaker\Sdk\ValidationError;
use PHPUnit\Framework\TestCase;

final class ClientTest extends TestCase
{
    /** Build a descriptor whose recipient is a fresh X25519 keypair we can decrypt with. */
    private function formWithRecipient(string &$secret): FormDescriptor
    {
        $kp = sodium_crypto_box_keypair();
        $secret = $kp;
        $pub = sodium_crypto_box_publickey($kp);
        return new FormDescriptor([
            'formId'             => 'contact_form',
            'schemaVersion'      => 4,
            'recipientUserId'    => 'user-123',
            'recipientPublicKey' => base64_encode($pub),
            'fields'             => [
                ['id' => 'f1', 'key' => 'email', 'name' => 'email', 'label' => 'Email', 'kind' => 'email', 'required' => true],
                ['id' => 'f2', 'key' => 'age', 'name' => 'age', 'label' => 'Age', 'kind' => 'number', 'required' => false],
            ],
            'verified'           => true,
        ]);
    }

    public function testBuildSubmissionSealsPayloadThatRoundTrips(): void
    {
        $form = $this->formWithRecipient($kp);
        $built = Client::buildSubmission($form, ['email' => 'ada@example.com', 'age' => '37']);

        $env = $built['envelope'];
        self::assertSame('contact_form', $env['formId']);
        self::assertSame('user-123', $env['recipientUserId']);
        self::assertSame(32, strlen($env['submissionId']));

        // Decrypt the sealed ciphertext with the recipient secret key.
        $ct = base64_decode($env['ciphertext'], true);
        $plain = sodium_crypto_box_seal_open($ct, $kp);
        self::assertNotFalse($plain);
        $payload = json_decode((string) $plain, true);

        self::assertSame('ada@example.com', $payload['values']['email']);
        self::assertSame(37, $payload['values']['age']); // number coerced from "37"
        self::assertSame('Create', $payload['mode']);
        self::assertSame(4, $payload['formVersion']);
    }

    public function testValidationRejectsUnknownAndMissingRequired(): void
    {
        $form = $this->formWithRecipient($kp);
        try {
            Client::buildSubmission($form, ['nope' => 'x']); // unknown + missing required email
            self::fail('expected ValidationError');
        } catch (ValidationError $e) {
            $fieldsWithIssues = array_column($e->issues, 'field');
            self::assertContains('nope', $fieldsWithIssues);
            self::assertContains('email', $fieldsWithIssues);
        }
    }

    public function testPostSubmissionUsesInjectedPoster(): void
    {
        $form = $this->formWithRecipient($kp);
        $built = Client::buildSubmission($form, ['email' => 'ada@example.com']);

        $captured = null;
        $poster = function (string $url, string $body, array $headers) use (&$captured): array {
            $captured = $url;
            return [200, json_encode(['submissionId' => 'sid-1', 'editToken' => 'tok-1'])];
        };
        $res = Client::postSubmission($built['envelope'], ['poster' => $poster]);

        self::assertStringEndsWith('/submissions', (string) $captured);
        self::assertSame('sid-1', $res['submissionId']);
        self::assertSame('tok-1', $res['editToken']);
    }

    public function testFoboVerifyRejectsGarbage(): void
    {
        self::assertNull(FoboTrustRoot::verify(['payloadJson' => '{}', 'signatureBase64' => 'AAAA'], 'somekey'));
        self::assertNull(FoboTrustRoot::verify(null, 'somekey'));
    }

    public function testFieldKindNormalisation(): void
    {
        self::assertSame('long-text', FieldKinds::normalizeKind('LongText'));
        self::assertSame('multi-choice', FieldKinds::normalizeKind('MULTI_CHOICE'));
        self::assertFalse(FieldKinds::isInputKind('heading'));
        self::assertTrue(FieldKinds::isInputKind('text'));
    }

    public function testReadOnlyKindRejected(): void
    {
        $res = Validator::validateValues(
            [['key' => 'h', 'name' => 'h', 'kind' => 'heading', 'required' => false]],
            ['h' => 'x']
        );
        self::assertNotEmpty($res['issues']);
        self::assertSame('heading', $res['issues'][0]['kind']);
    }
}
