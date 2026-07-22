<?php
namespace Fobo\DataMakerForms;

if (!defined('ABSPATH')) exit;

/**
 * Gutenberg block wrapping the [fobo_data_maker_form] shortcode. Server-rendered
 * (render_callback delegates to Shortcode::render) so the editor stays
 * lightweight — no React build pipeline, no client-side renderer in the
 * editor preview, just the same HTML the front-end gets.
 *
 * The editor sidebar uses a SelectControl populated from a REST endpoint
 * that lists every uploaded form's slug; authors pick one instead of
 * remembering the slug string.
 */
final class Block
{
    public static function register(): void
    {
        register_block_type('fobo/data-maker-form', [
            'api_version'     => 3,
            'title'           => __('FOBO Data Maker Form', 'fobo-data-maker-forms'),
            'category'        => 'embed',
            'icon'            => 'feedback',
            'description'     => __('Render a Data Maker form uploaded under FOBO Data Maker Forms → Upload .dmf.', 'fobo-data-maker-forms'),
            'attributes'      => [
                'slug'  => ['type' => 'string', 'default' => ''],
                'theme' => ['type' => 'string', 'default' => ''],   // ''=inherit, 'on', 'off'
            ],
            'editor_script'   => 'fobo-data-maker-forms-block-editor',
            'render_callback' => [self::class, 'render'],
            'supports'        => ['html' => false, 'align' => ['wide', 'full']],
        ]);
    }

    public static function register_assets(): void
    {
        wp_register_script(
            'fobo-data-maker-forms-block-editor',
            FOBO_DATA_MAKER_FORMS_URL . 'assets/block.js',
            ['wp-blocks', 'wp-element', 'wp-block-editor', 'wp-components', 'wp-api-fetch', 'wp-i18n'],
            FOBO_DATA_MAKER_FORMS_VERSION,
            true
        );

        // Wire the block editor's __() calls to the plugin's JSON
        // translations. WP loads languages/fobo-data-maker-forms-{locale}-
        // {md5(block.js path)}.json for this handle — produced by
        // `make json` (wp i18n make-json) from the per-locale .po files.
        if (function_exists('wp_set_script_translations')) {
            wp_set_script_translations(
                'fobo-data-maker-forms-block-editor',
                'fobo-data-maker-forms',
                FOBO_DATA_MAKER_FORMS_DIR . 'languages'
            );
        }
    }

    public static function register_rest_routes(): void
    {
        register_rest_route('fobo-data-maker/v1', '/forms', [
            'methods'             => 'GET',
            'callback'            => [self::class, 'list_forms'],
            'permission_callback' => function () { return current_user_can('edit_posts'); },
        ]);
    }

    public static function list_forms(): \WP_REST_Response
    {
        $rows = FormStore::list_all();
        $out  = [];
        foreach ($rows as $r) {
            $out[] = [
                'slug'    => (string)$r['slug'],
                'form_id' => (string)$r['form_id'],
                'label'   => sprintf('%s  (form %s)', $r['slug'], $r['form_id']),
            ];
        }
        return new \WP_REST_Response($out, 200);
    }

    public static function render(array $attributes): string
    {
        $slug = isset($attributes['slug']) ? sanitize_title((string)$attributes['slug']) : '';
        if (!$slug) {
            return '<p style="padding:12px;border:1px dashed #888;color:#666;">'
                . esc_html__('Pick a form in the block sidebar.', 'fobo-data-maker-forms')
                . '</p>';
        }
        $theme = isset($attributes['theme']) ? sanitize_text_field((string)$attributes['theme']) : '';
        return Shortcode::render(['id' => $slug, 'theme' => $theme]);
    }
}
