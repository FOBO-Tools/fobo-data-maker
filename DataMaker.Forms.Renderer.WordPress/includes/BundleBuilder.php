<?php
namespace DataMaker\Forms\Renderer\WordPress;

if (!defined('ABSPATH')) exit;

/**
 * Assembles the JSON payload the renderer.js script reads from
 * the inlined <script id="dm-bundle-..."> tag. Pre-baked on the C# side
 * inside the .dmf — this class just decodes the stored columns and emits
 * them with the camelCase keys the JS expects.
 */
final class BundleBuilder
{
    public static function build_payload(array $form_row): array
    {
        $form    = json_decode($form_row['form_json'],            true) ?: [];
        $hidden  = FormStore::get_hidden_elements($form_row);

        if ($hidden) {
            $hiddenSet = array_flip($hidden);
            $form      = self::strip_hidden($form, $hiddenSet);
        }

        // Site-level error-message overrides set in the per-form admin
        // (Form settings → Field error messages). Layered on top of each
        // field's own schema messages so renderer falls back: site → schema → engine default.
        // Form-level slots ('validationBanner' etc.) come in under the
        // reserved '__form' key and merge into Form.Messages the same way.
        $overrides = FormStore::get_message_overrides($form_row);
        if ($overrides) {
            if (isset($overrides['__form']) && is_array($overrides['__form'])) {
                $merged = is_array($form['messages'] ?? null) ? $form['messages'] : [];
                foreach ($overrides['__form'] as $msgId => $text) {
                    $merged[(string)$msgId] = (string)$text;
                }
                if ($merged) $form['messages'] = $merged;
            }
            if (!empty($form['fields']) && is_array($form['fields'])) {
                foreach ($form['fields'] as &$f) {
                    $name = (string)($f['name'] ?? '');
                    if ($name === '' || !isset($overrides[$name])) continue;
                    $merged = is_array($f['messages'] ?? null) ? $f['messages'] : [];
                    foreach ($overrides[$name] as $msgId => $text) {
                        $merged[(string)$msgId] = (string)$text;
                    }
                    if ($merged) $f['messages'] = $merged;
                }
                unset($f);
            }
        }

        $elementCss = json_decode($form_row['element_css_json'] ?? '{}', true) ?: [];
        $paletteCss = (string)($form_row['palette_css'] ?? '');

        // Repair AARRGGBB → RRGGBBAA when bundle came from a pre-fix
        // desktop binary. Detection: in CSS-correct form, the LAST two
        // hex chars are the alpha channel — a designer authoring a
        // translucent color rarely picks alpha=FF (that's just opaque,
        // would be written as 6-char). When every 8-char literal in the
        // payload ends in FF, the bundle is overwhelmingly Uno-style
        // (FF is the blue channel of a fully-blue palette accent). Once
        // detected, rewrite once; well-formed CSS bundles untouched.
        if (self::looks_like_uno_hex_bundle($elementCss, $paletteCss)) {
            if (is_array($elementCss)) {
                foreach ($elementCss as $k => $v) {
                    if (is_string($v)) $elementCss[$k] = self::swap_argb_to_rgba($v);
                }
            }
            $paletteCss = self::swap_argb_to_rgba($paletteCss);
        }

        return [
            'form'       => $form,
            'compiled'   => json_decode($form_row['compiled_json']    ?? '{}', true) ?: new \stdClass(),
            'elementCss' => $elementCss ?: new \stdClass(),
            'paletteCss' => $paletteCss,
        ];
    }

    /**
     * True when every 8-char hex literal in the bundle ends in FF AND
     * at least one starts in non-FF — the signature of a desktop bake
     * before the C# StyleCss.NormalizeHex commit shipped. Returns false
     * for already-CSS-correct payloads (alpha-last with varied values),
     * so the rewrite is idempotent / no-op on correctly-emitted bundles.
     */
    private static function looks_like_uno_hex_bundle($elementCss, string $paletteCss): bool
    {
        $all = $paletteCss;
        if (is_array($elementCss)) {
            foreach ($elementCss as $v) if (is_string($v)) $all .= "\n" . $v;
        }
        if (!preg_match_all('/#([0-9a-fA-F]{8})\b/', $all, $matches)) return false;
        // Fully-transparent hex (#00000000) round-trips identically under
        // either convention, so it can't tell the two formats apart —
        // skip those when classifying. Same for fully-opaque #FFFFFFFF
        // (alpha-only difference).
        $seenSuspect = false;
        foreach ($matches[1] as $hex) {
            $upper = strtoupper($hex);
            if ($upper === '00000000' || $upper === 'FFFFFFFF') continue;
            $tail = substr($upper, 6, 2);
            if ($tail !== 'FF') return false;       // an alpha != FF → already CSS form
            $head = substr($upper, 0, 2);
            if ($head !== 'FF') $seenSuspect = true;
        }
        return $seenSuspect;
    }

    private static function swap_argb_to_rgba(string $css): string
    {
        return preg_replace_callback(
            '/#([0-9a-fA-F]{8})\b/',
            static function (array $m): string {
                $hex = $m[1];
                return '#' . substr($hex, 2, 6) . substr($hex, 0, 2);
            },
            $css
        ) ?? $css;
    }

    /**
     * Walk the form schema and drop any column whose own id is in the
     * hidden set, OR any FieldColumn whose referenced fieldId is hidden.
     * Empty rows / sections are kept (the renderer just emits an empty
     * row), since downstream rows aren't reflowed by removal — but
     * dropping columns lets CSS Grid auto-flow rebalance the row.
     */
    private static function strip_hidden(array $form, array $hiddenSet): array
    {
        // Drop standalone fields whose ids are hidden (so any orphan
        // FieldColumn pointing to them also drops below).
        if (!empty($form['fields']) && is_array($form['fields'])) {
            $form['fields'] = array_values(array_filter(
                $form['fields'],
                fn($f) => !isset($hiddenSet[(string)($f['id'] ?? '')])
            ));
        }
        if (!empty($form['steps']) && is_array($form['steps'])) {
            foreach ($form['steps'] as &$step) {
                if (!empty($step['sections']) && is_array($step['sections'])) {
                    foreach ($step['sections'] as &$section) {
                        if (!empty($section['rows']) && is_array($section['rows'])) {
                            foreach ($section['rows'] as &$row) {
                                $row['columns'] = self::filter_columns($row['columns'] ?? [], $hiddenSet);
                            }
                            unset($row);
                        }
                    }
                    unset($section);
                }
            }
            unset($step);
        }
        return $form;
    }

    private static function filter_columns(array $columns, array $hiddenSet): array
    {
        $out = [];
        foreach ($columns as $col) {
            $kind = strtolower((string)($col['kind'] ?? $col['Kind'] ?? ''));
            $id   = (string)($col['id'] ?? '');

            // Field columns are hidden by their target fieldId, not their own col id.
            if ($kind === 'field') {
                $fid = (string)($col['fieldId'] ?? '');
                if ($fid !== '' && isset($hiddenSet[$fid])) continue;
            } else if ($id !== '' && isset($hiddenSet[$id])) {
                continue;
            }

            // Recurse into groups.
            if ($kind === 'group' && !empty($col['rows']) && is_array($col['rows'])) {
                foreach ($col['rows'] as &$inner) {
                    $inner['columns'] = self::filter_columns($inner['columns'] ?? [], $hiddenSet);
                }
                unset($inner);
            }
            $out[] = $col;
        }
        return $out;
    }
}
