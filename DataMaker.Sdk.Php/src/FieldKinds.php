<?php

declare(strict_types=1);

namespace DataMaker\Sdk;

/**
 * Canonical DataMaker field kinds + per-kind coercion. form.json persists kinds
 * in lowercase kebab-case ("long-text", "multi-choice"); older bundles use
 * PascalCase enum names or no-dash spellings — {@see normalizeKind} folds every
 * variant onto the canonical kebab id. Mirrors the Python/JS field-kind logic.
 */
final class FieldKinds
{
    /** @var array<string,string> canonical kebab ids keyed by enum name. */
    public const KINDS = [
        'TEXT' => 'text', 'LONG_TEXT' => 'long-text', 'RICH_TEXT' => 'rich-text',
        'NUMBER' => 'number', 'DECIMAL' => 'decimal', 'MONEY' => 'money',
        'DATE' => 'date', 'DATETIME' => 'datetime', 'BOOLEAN' => 'boolean',
        'CHOICE' => 'choice', 'MULTI_CHOICE' => 'multi-choice', 'LIST' => 'list',
        'EMAIL' => 'email', 'PHONE' => 'phone', 'URL' => 'url', 'GEO' => 'geo',
        'IMAGE' => 'image', 'ATTACHMENT' => 'attachment',
        'SIGNATURE' => 'signature', 'INITIALS' => 'initials', 'RELATION' => 'relation',
    ];

    private const ALIASES = [
        'longtext' => 'long-text', 'richtext' => 'rich-text',
        'multichoice' => 'multi-choice', 'datetimeoffset' => 'datetime',
    ];

    /** Kinds in fields[] that never accept a submitted value. */
    public const NON_INPUT_KINDS = ['calc', 'calculated', 'heading'];

    private const TRUE_WORDS  = ['true', '1', 'yes', 'y', 'on'];
    private const FALSE_WORDS = ['false', '0', 'no', 'n', 'off'];

    /** @return string[] */
    public static function allKinds(): array
    {
        return array_values(self::KINDS);
    }

    /** @param mixed $kind */
    public static function normalizeKind($kind): string
    {
        $k = strtolower(trim((string) ($kind ?? '')));
        $all = self::allKinds();
        if (in_array($k, $all, true)) {
            return $k;
        }
        if (isset(self::ALIASES[$k])) {
            return self::ALIASES[$k];
        }
        $squashed = self::squash($k);
        if (isset(self::ALIASES[$squashed])) {
            return self::ALIASES[$squashed];
        }
        foreach ($all as $canonical) {
            if (self::squash($canonical) === $squashed) {
                return $canonical;
            }
        }
        return $k;
    }

    /** @param mixed $kind */
    public static function isInputKind($kind): bool
    {
        return !in_array(self::normalizeKind($kind), self::NON_INPUT_KINDS, true);
    }

    /**
     * Coerce $raw to the wire shape for $kind.
     *
     * @param mixed $kind
     * @param mixed $raw
     * @param array<string,mixed> $field
     * @return array{0:mixed,1:?string} [value, errorMessage]
     */
    public static function coerceValue($kind, $raw, array $field): array
    {
        $k = self::normalizeKind($kind);

        if ($k === 'number' || $k === 'decimal' || $k === 'money') {
            if (is_int($raw) || (is_float($raw) && !is_bool($raw))) {
                $n = $raw;
            } else {
                $s = trim((string) $raw);
                if ($s === '' || !is_numeric($s)) {
                    return [null, "expected a number, got \"{$raw}\""];
                }
                $n = $s + 0; // int|float
            }
            if (is_float($n) && floor($n) === $n) {
                $n = (int) $n;
            }
            return [$n, null];
        }

        if ($k === 'boolean') {
            if (is_bool($raw)) {
                return [$raw, null];
            }
            $s = strtolower(trim((string) $raw));
            if (in_array($s, self::TRUE_WORDS, true)) {
                return [true, null];
            }
            if (in_array($s, self::FALSE_WORDS, true)) {
                return [false, null];
            }
            return [null, "expected a boolean, got \"{$raw}\""];
        }

        if ($k === 'multi-choice') {
            $arr = is_array($raw) ? $raw : [$raw];
            $values = array_map(static fn ($v): string => (string) $v, $arr);
            $allowed = self::choiceValues($field);
            if ($allowed !== null && empty($field['allowCustom'])) {
                $bad = array_values(array_filter($values, static fn ($v) => !in_array($v, $allowed, true)));
                if ($bad) {
                    return [null, 'not in allowed choices: ' . implode(', ', $bad)];
                }
            }
            return [$values, null];
        }

        if ($k === 'choice') {
            $v = (string) $raw;
            $allowed = self::choiceValues($field);
            if ($allowed !== null && empty($field['allowCustom']) && !in_array($v, $allowed, true)) {
                return [null, "\"{$v}\" is not one of the allowed choices"];
            }
            return [$v, null];
        }

        if (in_array($k, ['text', 'long-text', 'rich-text', 'email', 'phone', 'url', 'date', 'datetime'], true)) {
            return [(string) $raw, null];
        }

        // image/attachment/geo/relation/list and unknown: pass through.
        return [$raw, null];
    }

    /**
     * @param array<string,mixed> $field
     * @return string[]|null
     */
    private static function choiceValues(array $field): ?array
    {
        $choices = $field['choices'] ?? null;
        if (!is_array($choices) || $choices === []) {
            return null;
        }
        return array_map(static fn ($c): string => (string) ($c['value'] ?? ''), $choices);
    }

    private static function squash(string $s): string
    {
        return preg_replace('/[^a-z0-9]/', '', $s) ?? '';
    }
}
