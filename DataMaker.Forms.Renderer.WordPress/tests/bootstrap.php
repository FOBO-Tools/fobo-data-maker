<?php
/**
 * PHPUnit bootstrap. Stubs the WordPress functions the plugin source
 * touches at file-load time so we can `require` the production files
 * without spinning up a full WP test harness. Each test file then
 * exercises pure helpers (regex sanitisers, hex translators, URL
 * validators) directly via reflection where they're private.
 */

namespace {
    if (!defined('ABSPATH')) define('ABSPATH', __DIR__ . '/');
    if (!defined('WPINC'))   define('WPINC',   'wp-includes');

    if (!function_exists('apply_filters')) {
        function apply_filters($tag, $value, ...$args) { return $value; }
    }
    if (!function_exists('wp_parse_url')) {
        function wp_parse_url($url) { return parse_url($url); }
    }
    if (!function_exists('__')) {
        function __($text, $domain = null) { return $text; }
    }
    if (!function_exists('esc_html')) {
        function esc_html($s) { return htmlspecialchars((string)$s, ENT_QUOTES); }
    }
    if (!function_exists('esc_attr')) {
        function esc_attr($s) { return htmlspecialchars((string)$s, ENT_QUOTES); }
    }
    if (!function_exists('esc_url_raw')) {
        function esc_url_raw($s) { return (string)$s; }
    }
    if (!function_exists('sanitize_text_field')) {
        function sanitize_text_field($s) { return trim(strip_tags((string)$s)); }
    }
    if (!function_exists('wp_json_encode')) {
        function wp_json_encode($data, $opts = 0) { return json_encode($data, $opts); }
    }
    if (!function_exists('wp_unslash')) {
        function wp_unslash($v) { return $v; }
    }
}

// Lightweight FormStore stub. BundleBuilder calls these two helpers;
// returning empty arrays keeps the BundleBuilder code path live without
// pulling in the wpdb-dependent production class.
namespace Fobo\DataMakerForms {
    if (!class_exists(__NAMESPACE__ . '\\FormStore')) {
        final class FormStore {
            public static function get_hidden_elements(array $row): array { return []; }
            public static function get_message_overrides(array $row): array { return []; }
        }
    }
}

namespace {
    require_once __DIR__ . '/../includes/BundleBuilder.php';
    require_once __DIR__ . '/../includes/Shortcode.php';
}
