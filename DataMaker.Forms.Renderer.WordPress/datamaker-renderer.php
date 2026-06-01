<?php
/**
 * Plugin Name:       Data Maker Forms
 * Plugin URI:        https://fobo-tools.com/
 * Description:       Renders Data Maker forms from signed .dmf uploads. POSTs sealed submissions to the Data Maker API; supports the localStorage-backed edit flow.
 * Version:           0.1.0
 * Requires at least: 6.4
 * Requires PHP:      8.1
 * Author:            FOBO
 * License:           BSD-3-Clause
 * License URI:       https://opensource.org/license/bsd-3-clause
 * Text Domain:       datamaker-renderer
 * Domain Path:       /languages
 */

if (!defined('ABSPATH')) {
    exit;
}

define('DM_RENDERER_VERSION',        '0.1.0');
define('DM_RENDERER_FILE',           __FILE__);
define('DM_RENDERER_DIR',            plugin_dir_path(__FILE__));
define('DM_RENDERER_URL',            plugin_dir_url(__FILE__));
// Public DataMaker API endpoint. CloudFront on datamaker-api.fobo-tools.com
// routes /submissions* to the Data Maker API (set up in the data-maker-agent
// CFN stack's CacheBehaviors). Default; can be overridden per-site via:
//   1. wp-config.php constant: define('DM_RENDERER_SYNC_API_URL', '…');
//      — wins everywhere, even before plugins load.
//   2. WP filter `dm_renderer_sync_api_url` — runtime override per request.
// Resolved via dm_renderer_sync_api_url(); never read the constant directly.
if (!defined('DM_RENDERER_SYNC_API_URL')) {
    define('DM_RENDERER_SYNC_API_URL', 'https://datamaker-api.fobo-tools.com/');
}

require_once DM_RENDERER_DIR . 'includes/DmfReader.php';
require_once DM_RENDERER_DIR . 'includes/FormStore.php';
require_once DM_RENDERER_DIR . 'includes/MessageCatalog.php';
require_once DM_RENDERER_DIR . 'includes/BundleBuilder.php';
require_once DM_RENDERER_DIR . 'includes/SubmitProxy.php';
require_once DM_RENDERER_DIR . 'includes/Shortcode.php';
require_once DM_RENDERER_DIR . 'includes/Block.php';
require_once DM_RENDERER_DIR . 'includes/Admin/PageHeader.php';
require_once DM_RENDERER_DIR . 'includes/Admin/UploadPage.php';
require_once DM_RENDERER_DIR . 'includes/Admin/FormsListPage.php';
require_once DM_RENDERER_DIR . 'includes/Admin/FormSettingsPage.php';
require_once DM_RENDERER_DIR . 'includes/Admin/PreviewPage.php';
require_once DM_RENDERER_DIR . 'includes/Admin/SettingsPage.php';

register_activation_hook(__FILE__, ['\\DataMaker\\Forms\\Renderer\\WordPress\\FormStore', 'install_table']);

// ── Plugin helpers (global namespace; callers use the leading-backslash
//    form so they resolve regardless of the calling file's namespace). ──

/**
 * Single source of truth for the sync Lambda URL. Reads
 * DM_RENDERER_SYNC_API_URL (wp-config or plugin default) and then applies
 * the `dm_renderer_sync_api_url` filter so per-env overrides land
 * cleanly. Always call this — never `DM_RENDERER_SYNC_API_URL` directly.
 */
function dm_renderer_sync_api_url(): string {
    $url = (string)apply_filters('dm_renderer_sync_api_url', DM_RENDERER_SYNC_API_URL);
    return rtrim($url, '/') . '/';
}

/**
 * Capability gate. Plugin admin surfaces check `manage_datamaker_forms`,
 * which by default maps to the standard `manage_options` capability
 * (Administrators only). Hosts that want Editors to be able to upload /
 * configure forms can add the capability to the role with:
 *
 *     get_role('editor')->add_cap('manage_datamaker_forms');
 *
 * Defaults stay safe (admin-only) — `manage_options` is also accepted so
 * existing installs keep working without touching role caps.
 */
function dm_renderer_user_can_manage(): bool {
    return current_user_can('manage_datamaker_forms') || current_user_can('manage_options');
}

// Load plugin translations from /languages so __() / esc_html__() pick up
// .mo files matching the active site locale. Text domain = plugin slug.
add_action('init', function () {
    load_plugin_textdomain('datamaker-renderer', false, dirname(plugin_basename(__FILE__)) . '/languages');
});

// Map our meta cap to itself so role grants are honored verbatim. Hosts
// that want a different mapping can add their own higher-priority filter.
add_filter('map_meta_cap', function ($caps, $cap) {
    if ($cap === 'manage_datamaker_forms') {
        return ['manage_datamaker_forms'];
    }
    return $caps;
}, 10, 2);

add_action('init',                  ['\\DataMaker\\Forms\\Renderer\\WordPress\\Shortcode',           'register']);
add_action('init',                  ['\\DataMaker\\Forms\\Renderer\\WordPress\\Block',               'register']);
add_action('admin_init',            ['\\DataMaker\\Forms\\Renderer\\WordPress\\FormStore',           'maybe_upgrade']);
add_action('admin_menu',            ['\\DataMaker\\Forms\\Renderer\\WordPress\\Admin\\UploadPage',       'register_menu']);
add_action('admin_menu',            ['\\DataMaker\\Forms\\Renderer\\WordPress\\Admin\\FormSettingsPage', 'register_menu']);
add_action('admin_menu',            ['\\DataMaker\\Forms\\Renderer\\WordPress\\Admin\\PreviewPage',     'register_menu']);
add_action('admin_init',            ['\\DataMaker\\Forms\\Renderer\\WordPress\\Admin\\SettingsPage', 'register_settings']);
add_action('template_redirect',     ['\\DataMaker\\Forms\\Renderer\\WordPress\\Admin\\PreviewPage', 'maybe_serve_frontend_preview']);
add_action('rest_api_init',         ['\\DataMaker\\Forms\\Renderer\\WordPress\\SubmitProxy',         'register_routes']);
add_action('rest_api_init',         ['\\DataMaker\\Forms\\Renderer\\WordPress\\Block',               'register_rest_routes']);
add_action('wp_enqueue_scripts',    ['\\DataMaker\\Forms\\Renderer\\WordPress\\Shortcode',           'register_assets']);
add_action('enqueue_block_editor_assets', ['\\DataMaker\\Forms\\Renderer\\WordPress\\Block',         'register_assets']);
