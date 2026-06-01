<?php
namespace DataMaker\Forms\Renderer\WordPress;

if (!defined('ABSPATH')) exit;

/**
 * PHP mirror of <c>DataMaker.Schema.Validation.IntrinsicMessageCatalog</c>.
 * Returns the customizable error-message slots for a field, driven from its
 * <c>kind</c>, options, and the flat <c>required</c> flag — same shape and
 * canonical slot ids as the C# side.
 *
 * Used by the WP admin (FormSettingsPage) to render one row per active slot
 * and by BundleBuilder to inline schema/site overrides into the form bundle.
 *
 * <b>Source of truth</b>: the canonical defaults live in
 * <c>DataMaker.Schema.Validation.IntrinsicMessages</c>. If you change a
 * string there, mirror the edit here so the WP admin placeholders agree
 * with what the renderer falls back to.
 */
final class MessageCatalog
{
    /**
     * Active message slots for the given field, in display order.
     * Returns array<int, array{id:string,label:string,default:string}>.
     */
    public static function slots_for(array $field): array
    {
        $slots = [];

        if (!empty($field['required'])) {
            $slots[] = ['id' => 'required', 'label' => __('Required', 'datamaker-renderer'), 'default' => __('Required', 'datamaker-renderer')];
        }

        // Text-options intrinsics. Parametrized labels carry the parameter so
        // the admin reads "Minimum length (5)", not just "Minimum length".
        $text = is_array($field['text'] ?? null) ? $field['text'] : null;
        if ($text) {
            $minLen = isset($text['minLength']) ? (int)$text['minLength'] : 0;
            if ($minLen > 0) {
                $slots[] = [
                    'id'      => 'text.minLength',
                    /* translators: %d = minimum character count */
                    'label'   => sprintf(__('Minimum length (%d)', 'datamaker-renderer'), $minLen),
                    /* translators: %d = minimum character count */
                    'default' => sprintf(_n('Must be at least %d character.', 'Must be at least %d characters.', $minLen, 'datamaker-renderer'), $minLen),
                ];
            }
            $maxLen = isset($text['maxLength']) ? (int)$text['maxLength'] : 0;
            if ($maxLen > 0) {
                $slots[] = [
                    'id'      => 'text.maxLength',
                    /* translators: %d = maximum character count */
                    'label'   => sprintf(__('Maximum length (%d)', 'datamaker-renderer'), $maxLen),
                    /* translators: %d = maximum character count */
                    'default' => sprintf(_n('Must be at most %d character.', 'Must be at most %d characters.', $maxLen, 'datamaker-renderer'), $maxLen),
                ];
            }
            if (!empty($text['pattern']) && is_string($text['pattern'])) {
                $slots[] = ['id' => 'text.pattern', 'label' => __('Pattern match', 'datamaker-renderer'), 'default' => __('Value does not match the required pattern.', 'datamaker-renderer')];
            }
        }

        $kind = strtolower((string)($field['kind'] ?? ''));
        switch ($kind) {
            case 'email':         $slots[] = ['id' => 'email',         'label' => __('Email format', 'datamaker-renderer'),    'default' => __('Not a valid email address.', 'datamaker-renderer')];     break;
            case 'url':           $slots[] = ['id' => 'url',           'label' => __('URL format', 'datamaker-renderer'),      'default' => __('Not a valid URL.', 'datamaker-renderer')];               break;
            case 'phone':         $slots[] = ['id' => 'phone',         'label' => __('Phone format', 'datamaker-renderer'),    'default' => __('Not a valid phone number.', 'datamaker-renderer')];      break;
            case 'number':        $slots[] = ['id' => 'number',        'label' => __('Whole number', 'datamaker-renderer'),    'default' => __('Not a whole number.', 'datamaker-renderer')];            break;
            case 'decimal':       $slots[] = ['id' => 'decimal',       'label' => __('Decimal number', 'datamaker-renderer'),  'default' => __('Not a valid decimal number.', 'datamaker-renderer')];    break;
            case 'money':         $slots[] = ['id' => 'money',         'label' => __('Monetary amount', 'datamaker-renderer'), 'default' => __('Not a valid monetary amount.', 'datamaker-renderer')];   break;
            case 'date':          $slots[] = ['id' => 'date',          'label' => __('Date', 'datamaker-renderer'),            'default' => __('Not a valid date.', 'datamaker-renderer')];              break;
            case 'datetime':      $slots[] = ['id' => 'datetime',      'label' => __('Date-time', 'datamaker-renderer'),       'default' => __('Not a valid date-time.', 'datamaker-renderer')];         break;
            case 'boolean':       $slots[] = ['id' => 'boolean',       'label' => __('Boolean', 'datamaker-renderer'),         'default' => __('Not a boolean.', 'datamaker-renderer')];                 break;
            case 'choice':
                $choice = $field['choice'] ?? null;
                if (is_array($choice) && !empty($choice['choices']) && empty($choice['allowCustom']))
                    $slots[] = ['id' => 'choice', 'label' => __('Allowed choice', 'datamaker-renderer'), 'default' => __('Value is not in the allowed list.', 'datamaker-renderer')];
                break;
            case 'multi-choice':
            case 'multichoice':
                $choice = $field['choice'] ?? null;
                if (is_array($choice) && !empty($choice['choices']) && empty($choice['allowCustom']))
                    $slots[] = ['id' => 'multichoice', 'label' => __('Allowed choices', 'datamaker-renderer'), 'default' => __('Some items are not in the allowed list.', 'datamaker-renderer')];
                break;
            case 'geo':
                $slots[] = ['id' => 'geo.lat', 'label' => __('Latitude range', 'datamaker-renderer'),  'default' => __('Latitude must be between -90 and 90.', 'datamaker-renderer')];
                $slots[] = ['id' => 'geo.lng', 'label' => __('Longitude range', 'datamaker-renderer'), 'default' => __('Longitude must be between -180 and 180.', 'datamaker-renderer')];
                $slots[] = ['id' => 'geo',     'label' => __('Geo point', 'datamaker-renderer'),       'default' => __('Not a valid geo point.', 'datamaker-renderer')];
                break;
            case 'image':
            case 'attachment':
                $slots[] = ['id' => 'attachment.ext', 'label' => __('File extension', 'datamaker-renderer'), 'default' => __('File extension not allowed.', 'datamaker-renderer')];
                break;
        }

        return $slots;
    }

    /**
     * Form-level message slots. Mirrors C# <c>FormMessageCatalog.All</c> —
     * unlike per-field slots these don't depend on a kind / options, so the
     * list is static and shared by every form.
     */
    public static function form_slots(): array
    {
        return [
            ['id' => 'validationBanner', 'label' => __('Validation banner', 'datamaker-renderer'),
             'default' => __('Please fix the highlighted fields before submitting.', 'datamaker-renderer')],
        ];
    }
}
