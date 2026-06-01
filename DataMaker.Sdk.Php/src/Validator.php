<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/** Validate + coerce a caller's values against a form's field schema. */
final class Validator
{
    /**
     * Returns ['values' => ..., 'issues' => ...]. `values` holds only known,
     * non-empty, input-kind fields coerced to their wire shape; `issues` is
     * empty when the input is clean.
     *
     * @param array<int,array<string,mixed>> $fields
     * @param array<string,mixed> $input
     * @return array{values:array<string,mixed>,issues:array<int,array{field:string,kind:?string,message:string}>}
     */
    public static function validateValues(array $fields, array $input, bool $allowUnknown = false): array
    {
        $issues = [];
        $values = [];
        $byKey = [];
        foreach ($fields as $f) {
            $byKey[(string) ($f['key'] ?? '')] = $f;
        }

        foreach ($input as $key => $raw) {
            $key = (string) $key;
            $field = $byKey[$key] ?? null;
            if ($field === null) {
                if (!$allowUnknown) {
                    $issues[] = ['field' => $key, 'kind' => null, 'message' => 'unknown field — not in the form schema'];
                }
                continue;
            }
            if (!FieldKinds::isInputKind($field['kind'] ?? '')) {
                $nk = FieldKinds::normalizeKind($field['kind'] ?? '');
                $issues[] = ['field' => $key, 'kind' => $nk, 'message' => "field is read-only ({$nk}) and cannot be submitted"];
                continue;
            }
            if (self::isEmpty($raw)) {
                continue;
            }
            [$value, $error] = FieldKinds::coerceValue($field['kind'] ?? '', $raw, $field);
            if ($error !== null) {
                $issues[] = ['field' => $key, 'kind' => FieldKinds::normalizeKind($field['kind'] ?? ''), 'message' => $error];
                continue;
            }
            $values[$key] = $value;
        }

        foreach ($fields as $field) {
            if (empty($field['required'])) {
                continue;
            }
            if (in_array(FieldKinds::normalizeKind($field['kind'] ?? ''), FieldKinds::NON_INPUT_KINDS, true)) {
                continue;
            }
            $key = (string) ($field['key'] ?? '');
            if (!array_key_exists($key, $values)) {
                $issues[] = ['field' => $key, 'kind' => FieldKinds::normalizeKind($field['kind'] ?? ''), 'message' => 'required field is missing'];
            }
        }

        return ['values' => $values, 'issues' => $issues];
    }

    /** @param mixed $v */
    private static function isEmpty($v): bool
    {
        if ($v === null) {
            return true;
        }
        if (is_string($v)) {
            return trim($v) === '';
        }
        if (is_array($v)) {
            return count($v) === 0;
        }
        return false;
    }
}
