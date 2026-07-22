<?php
namespace Fobo\DataMakerForms;

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
            $slots[] = ['id' => 'required', 'label' => __('Required', 'fobo-data-maker-forms'), 'default' => __('Required', 'fobo-data-maker-forms')];
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
                    'label'   => sprintf(__('Minimum length (%d)', 'fobo-data-maker-forms'), $minLen),
                    /* translators: %d = minimum character count */
                    'default' => sprintf(_n('Must be at least %d character.', 'Must be at least %d characters.', $minLen, 'fobo-data-maker-forms'), $minLen),
                ];
            }
            $maxLen = isset($text['maxLength']) ? (int)$text['maxLength'] : 0;
            if ($maxLen > 0) {
                $slots[] = [
                    'id'      => 'text.maxLength',
                    /* translators: %d = maximum character count */
                    'label'   => sprintf(__('Maximum length (%d)', 'fobo-data-maker-forms'), $maxLen),
                    /* translators: %d = maximum character count */
                    'default' => sprintf(_n('Must be at most %d character.', 'Must be at most %d characters.', $maxLen, 'fobo-data-maker-forms'), $maxLen),
                ];
            }
            if (!empty($text['pattern']) && is_string($text['pattern'])) {
                $slots[] = ['id' => 'text.pattern', 'label' => __('Pattern match', 'fobo-data-maker-forms'), 'default' => __('Value does not match the required pattern.', 'fobo-data-maker-forms')];
            }
        }

        $kind = strtolower((string)($field['kind'] ?? ''));
        switch ($kind) {
            case 'email':         $slots[] = ['id' => 'email',         'label' => __('Email format', 'fobo-data-maker-forms'),    'default' => __('Not a valid email address.', 'fobo-data-maker-forms')];     break;
            case 'url':           $slots[] = ['id' => 'url',           'label' => __('URL format', 'fobo-data-maker-forms'),      'default' => __('Not a valid URL.', 'fobo-data-maker-forms')];               break;
            case 'phone':         $slots[] = ['id' => 'phone',         'label' => __('Phone format', 'fobo-data-maker-forms'),    'default' => __('Not a valid phone number.', 'fobo-data-maker-forms')];      break;
            case 'number':        $slots[] = ['id' => 'number',        'label' => __('Whole number', 'fobo-data-maker-forms'),    'default' => __('Not a whole number.', 'fobo-data-maker-forms')];            break;
            case 'decimal':       $slots[] = ['id' => 'decimal',       'label' => __('Decimal number', 'fobo-data-maker-forms'),  'default' => __('Not a valid decimal number.', 'fobo-data-maker-forms')];    break;
            case 'money':         $slots[] = ['id' => 'money',         'label' => __('Monetary amount', 'fobo-data-maker-forms'), 'default' => __('Not a valid monetary amount.', 'fobo-data-maker-forms')];   break;
            case 'date':          $slots[] = ['id' => 'date',          'label' => __('Date', 'fobo-data-maker-forms'),            'default' => __('Not a valid date.', 'fobo-data-maker-forms')];              break;
            case 'datetime':      $slots[] = ['id' => 'datetime',      'label' => __('Date-time', 'fobo-data-maker-forms'),       'default' => __('Not a valid date-time.', 'fobo-data-maker-forms')];         break;
            case 'boolean':       $slots[] = ['id' => 'boolean',       'label' => __('Boolean', 'fobo-data-maker-forms'),         'default' => __('Not a boolean.', 'fobo-data-maker-forms')];                 break;
            case 'choice':
                $choice = $field['choice'] ?? null;
                if (is_array($choice) && !empty($choice['choices']) && empty($choice['allowCustom']))
                    $slots[] = ['id' => 'choice', 'label' => __('Allowed choice', 'fobo-data-maker-forms'), 'default' => __('Value is not in the allowed list.', 'fobo-data-maker-forms')];
                break;
            case 'multi-choice':
            case 'multichoice':
                $choice = $field['choice'] ?? null;
                if (is_array($choice) && !empty($choice['choices']) && empty($choice['allowCustom']))
                    $slots[] = ['id' => 'multichoice', 'label' => __('Allowed choices', 'fobo-data-maker-forms'), 'default' => __('Some items are not in the allowed list.', 'fobo-data-maker-forms')];
                break;
            case 'geo':
                $slots[] = ['id' => 'geo.lat', 'label' => __('Latitude range', 'fobo-data-maker-forms'),  'default' => __('Latitude must be between -90 and 90.', 'fobo-data-maker-forms')];
                $slots[] = ['id' => 'geo.lng', 'label' => __('Longitude range', 'fobo-data-maker-forms'), 'default' => __('Longitude must be between -180 and 180.', 'fobo-data-maker-forms')];
                $slots[] = ['id' => 'geo',     'label' => __('Geo point', 'fobo-data-maker-forms'),       'default' => __('Not a valid geo point.', 'fobo-data-maker-forms')];
                break;
            case 'image':
            case 'attachment':
                $slots[] = ['id' => 'attachment.ext', 'label' => __('File extension', 'fobo-data-maker-forms'), 'default' => __('File extension not allowed.', 'fobo-data-maker-forms')];
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
            ['id' => 'validationBanner', 'label' => __('Validation banner', 'fobo-data-maker-forms'),
             'default' => __('Please fix the highlighted fields before submitting.', 'fobo-data-maker-forms')],
        ];
    }
}
