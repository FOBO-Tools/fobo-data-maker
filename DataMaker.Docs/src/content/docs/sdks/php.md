---
title: PHP SDK
description: Submit records to a DataMaker form from PHP with fobo/datamaker-sdk.
---

`fobo/datamaker-sdk` reads a `.dmf`, verifies the signature + FOBO attestation,
sealed-box encrypts a submission, and posts it. PHP 8.0+ with `ext-sodium`,
`ext-zip`, `ext-json`, `ext-curl`.

## Install

```sh
composer require fobo/datamaker-sdk
```

## Submit

```php
use DataMaker\Sdk\Client;

$dmf = file_get_contents('contact.dmf');

$result = Client::submit([
    'dmf'    => $dmf,                                  // verifies signature + form hash
    'values' => ['email' => 'ada@example.com', 'full_name' => 'Ada'],
]);
// => ['submissionId' => '...', 'editToken' => '...', 'formId' => 'contact_form']
```

Or read the form once and reuse it: `Client::submit(['form' => $form, 'values' => ...])`.

## Building the `values` array

`values` is keyed by each field's **`name`** (its storage key — *not* the
label). Inspect the form to see the names + kinds you need to fill:

```php
use DataMaker\Sdk\DmfReader;

$form = DmfReader::read($dmf);
foreach ($form->fields as $f) {
    echo $f['key'], ' · ', $f['kind'], $f['required'] ? ' (required)' : '', "\n";
}
// email · email (required)
// full_name · text
// age · number
// subscribed · boolean
// plan · choice
// interests · multi-choice
```

Map your data to those names, using the value type each kind expects:

```php
$values = [
    'email'       => 'ada@example.com',  // email        → string
    'full_name'   => 'Ada Lovelace',     // text         → string
    'age'         => 37,                 // number        → number (37, not "37")
    'subscribed'  => true,               // boolean       → bool
    'plan'        => 'pro',              // choice        → one of the choice values
    'interests'   => ['news', 'beta'],   // multi-choice  → string[]
    'signup_date' => '2026-05-29',       // date          → "YYYY-MM-DD"
    'budget'      => 1999.99,            // money         → number
];

Client::submit(['form' => $form, 'values' => $values]);
```

The full value type for every kind is in the
[field kinds reference](/fobo-data-maker/schema/field-kinds/).

## Trust: who receives the submission

`DmfReader::read` independently verifies the FOBO attestation against the
baked-in root key. Use it to show the verified publisher before submitting:

```php
$form = DmfReader::read($dmf);

if ($form->isFoboVerified()) {
    $a = $form->foboVerification;
    echo "Verified by FOBO as {$a['email']}";        // + $a['company'] when admin-verified
}
if (!$form->acceptsSubmissions()) {
    // share-only bundle: no sealed-box recipient, submissions can't route back
}
```

:::tip[The SDK coerces — but validates]
By default `submit` **coerces** (`"37"` → `37`, `"yes"` → `true`, a single
choice → `["x"]` for multi-choice) and **validates**: it throws a
`ValidationError` (with `->issues`) for a missing required field, an unknown key
(typo protection), a value for a read-only kind, or a choice that isn't in the
list. Pass `'validate' => false` to skip, or `'allowUnknown' => true` to ignore
extra keys.
:::

```php
use DataMaker\Sdk\ValidationError;

try {
    Client::submit(['form' => $form, 'values' => $values]);
} catch (ValidationError $e) {
    foreach ($e->issues as $i) {
        fwrite(STDERR, "{$i['field']}: {$i['message']}\n");
    }
}
```

## API

- **`DmfReader::read($dmfBytes, $verify = true)`** → `FormDescriptor` (`formId`,
  `name`, `schemaVersion`, `submitPolicy`, `recipientUserId`,
  `recipientPublicKey`, `signer`, `fields`, `verified`, `foboVerification`,
  `isFoboVerified()`, `acceptsSubmissions()`). Throws `DmfError` on tampering.
- **`Client::submit(['form' => …|'dmf' => …, 'values' => …, ...opts])`** →
  `['submissionId', 'editToken', 'formId']`. Options: `apiBaseUrl`,
  `submitterId`, `validate` (default `true`), `allowUnknown`, `verify`.
- **`Client::buildSubmission($form, $values, $opts)`** → seal without sending
  (`['submissionId', 'envelope', 'payload', 'values']`).
- **`Client::postSubmission($envelope, $opts)`** → post a pre-built envelope.
- **`Validator::validateValues($fields, $input, $allowUnknown)`** →
  `['values', 'issues']`.

Errors all extend `DataMaker\Sdk\DataMakerError` and carry a stable string in
`->errorCode` (PHP's `Exception::getCode()` is an int, so the machine-readable
code lives on `->errorCode`): `DMF_INVALID`, `VALIDATION_FAILED` (with
`->issues`), `SUBMISSION_REJECTED` (with `->status`, `->body`).

## Scope

Sender-side only — read a `.dmf` and submit. It does **not** decrypt or receive
submissions; that's the form owner's app, which guards the X25519 private half.
The WordPress plugin uses this SDK to verify the bundles it renders.
