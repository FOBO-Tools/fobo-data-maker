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
            $slots[] = ['id' => 'required', 'label' => 'Required', 'default' => self::DEFAULTS['required']];
        }

        // Text-options intrinsics. Parametrized labels carry the parameter so
        // the admin reads "Minimum length (5)", not just "Minimum length".
        $text = is_array($field['text'] ?? null) ? $field['text'] : null;
        if ($text) {
            $minLen = isset($text['minLength']) ? (int)$text['minLength'] : 0;
            if ($minLen > 0) {
                $slots[] = [
                    'id'      => 'text.minLength',
                    'label'   => "Minimum length ({$minLen})",
                    'default' => sprintf('Must be at least %d character%s.', $minLen, $minLen === 1 ? '' : 's'),
                ];
            }
            $maxLen = isset($text['maxLength']) ? (int)$text['maxLength'] : 0;
            if ($maxLen > 0) {
                $slots[] = [
                    'id'      => 'text.maxLength',
                    'label'   => "Maximum length ({$maxLen})",
                    'default' => sprintf('Must be at most %d character%s.', $maxLen, $maxLen === 1 ? '' : 's'),
                ];
            }
            if (!empty($text['pattern']) && is_string($text['pattern'])) {
                $slots[] = ['id' => 'text.pattern', 'label' => 'Pattern match', 'default' => self::DEFAULTS['text.pattern']];
            }
        }

        $kind = strtolower((string)($field['kind'] ?? ''));
        switch ($kind) {
            case 'email':         $slots[] = ['id' => 'email',         'label' => 'Email format',      'default' => self::DEFAULTS['email']];        break;
            case 'url':           $slots[] = ['id' => 'url',           'label' => 'URL format',        'default' => self::DEFAULTS['url']];          break;
            case 'phone':         $slots[] = ['id' => 'phone',         'label' => 'Phone format',      'default' => self::DEFAULTS['phone']];        break;
            case 'number':        $slots[] = ['id' => 'number',        'label' => 'Whole number',      'default' => self::DEFAULTS['number']];       break;
            case 'decimal':       $slots[] = ['id' => 'decimal',       'label' => 'Decimal number',    'default' => self::DEFAULTS['decimal']];      break;
            case 'money':         $slots[] = ['id' => 'money',         'label' => 'Monetary amount',   'default' => self::DEFAULTS['money']];        break;
            case 'date':          $slots[] = ['id' => 'date',          'label' => 'Date',              'default' => self::DEFAULTS['date']];         break;
            case 'datetime':      $slots[] = ['id' => 'datetime',      'label' => 'Date-time',         'default' => self::DEFAULTS['datetime']];     break;
            case 'boolean':       $slots[] = ['id' => 'boolean',       'label' => 'Boolean',           'default' => self::DEFAULTS['boolean']];      break;
            case 'choice':
                $choice = $field['choice'] ?? null;
                if (is_array($choice) && !empty($choice['choices']) && empty($choice['allowCustom']))
                    $slots[] = ['id' => 'choice', 'label' => 'Allowed choice', 'default' => self::DEFAULTS['choice']];
                break;
            case 'multi-choice':
            case 'multichoice':
                $choice = $field['choice'] ?? null;
                if (is_array($choice) && !empty($choice['choices']) && empty($choice['allowCustom']))
                    $slots[] = ['id' => 'multichoice', 'label' => 'Allowed choices', 'default' => self::DEFAULTS['multichoice']];
                break;
            case 'geo':
                $slots[] = ['id' => 'geo.lat', 'label' => 'Latitude range',  'default' => self::DEFAULTS['geo.lat']];
                $slots[] = ['id' => 'geo.lng', 'label' => 'Longitude range', 'default' => self::DEFAULTS['geo.lng']];
                $slots[] = ['id' => 'geo',     'label' => 'Geo point',       'default' => self::DEFAULTS['geo']];
                break;
            case 'image':
            case 'attachment':
                $slots[] = ['id' => 'attachment.ext', 'label' => 'File extension', 'default' => self::DEFAULTS['attachment.ext']];
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
            ['id' => 'validationBanner', 'label' => 'Validation banner',
             'default' => self::FORM_DEFAULTS['validationBanner']],
        ];
    }

    /** Form-level engine defaults. */
    public const FORM_DEFAULTS = [
        'validationBanner' => 'Please fix the highlighted fields before submitting.',
    ];

    /** Engine-default English messages keyed by slot id. Single source of truth in PHP-land. */
    public const DEFAULTS = [
        'required'         => 'Required',
        'text.pattern'     => 'Value does not match the required pattern.',
        'email'            => 'Not a valid email address.',
        'url'              => 'Not a valid URL.',
        'phone'            => 'Not a valid phone number.',
        'number'           => 'Not a whole number.',
        'decimal'          => 'Not a valid decimal number.',
        'money'            => 'Not a valid monetary amount.',
        'date'             => 'Not a valid date.',
        'datetime'         => 'Not a valid date-time.',
        'boolean'          => 'Not a boolean.',
        'choice'           => 'Value is not in the allowed list.',
        'multichoice'      => 'Some items are not in the allowed list.',
        'geo.lat'          => 'Latitude must be between -90 and 90.',
        'geo.lng'          => 'Longitude must be between -180 and 180.',
        'geo'              => 'Not a valid geo point.',
        'attachment.ext'   => 'File extension not allowed.',
    ];
}
