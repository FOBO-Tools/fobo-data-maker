# DataMaker PHP SDK

Sender-side SDK for [DataMaker](https://github.com/FOBO-Tools/fobo-data-maker) forms:
read a `.dmf`, verify its signature + FOBO attestation, sealed-box encrypt a submission,
and POST it. The wire envelope is byte-exact with the .NET / Python / JS SDKs.

Requires PHP 8.0+ with `ext-sodium`, `ext-zip`, `ext-json`, `ext-curl`.

## Install

```bash
composer require fobo/datamaker-sdk
```

## Submit a form

```php
use DataMaker\Sdk\Client;

$dmf = file_get_contents('contact.dmf');

$result = Client::submit([
    'dmf'    => $dmf,                                  // verifies signature + form hash
    'values' => ['email' => 'ada@example.com', 'full_name' => 'Ada'],
]);
// => ['submissionId' => '...', 'editToken' => '...', 'formId' => 'contact_form']
```

## Inspect trust before submitting

```php
use DataMaker\Sdk\DmfReader;

$form = DmfReader::read($dmf);

if ($form->isFoboVerified()) {
    $a = $form->foboVerification;
    echo "Verified by FOBO as {$a['email']}";        // + $a['company'] when admin-verified
}
if ($form->acceptsSubmissions()) {
    // sealed-box recipient present — submissions are E2E-encrypted to the publisher
}
```

`DmfReader::read` verifies the publisher's Ed25519 signature over the manifest and the
`form.json` hash, and independently verifies the FOBO attestation against the baked-in
root pubkey (Ed25519 sig + version + key match + expiry). Failure of the attestation is
non-fatal — the form stays self-signed-usable; only `isFoboVerified()` reflects it.

## Build without sending

```php
use DataMaker\Sdk\Client;
use DataMaker\Sdk\DmfReader;

$form  = DmfReader::read($dmf);
$built = Client::buildSubmission($form, ['email' => 'ada@example.com']);
// $built['envelope'] is the SubmissionEnvelope; POST it yourself, or:
$result = Client::postSubmission($built['envelope']);
```

## Errors

All throw `DataMaker\Sdk\DataMakerError` with a stable `->errorCode` string:

| Class | `errorCode` |
|-------|-------------|
| `DmfError` | `DMF_INVALID` |
| `ValidationError` (carries `->issues`) | `VALIDATION_FAILED` |
| `SubmissionError` (carries `->status`, `->body`) | `SUBMISSION_REJECTED` |

> PHP's `Exception::getCode()` is an int, so the SDK's machine-readable code lives in
> `->errorCode` (the JS/Python `.code` equivalent).

## Scope

Sender-side only — read a `.dmf` and submit. It does **not** decrypt or receive
submissions; that's the form owner's app, which guards the X25519 private half.
