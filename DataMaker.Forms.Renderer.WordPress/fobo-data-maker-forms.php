<?php
/**
 * Plugin Name:       FOBO Data Maker Forms
 * Plugin URI:        https://fobo-tools.com/
 * Description:       Renders Data Maker forms from signed .dmf uploads. POSTs sealed submissions to the Data Maker API; supports the localStorage-backed edit flow.
 * Version:           1.0.0
 * Requires at least: 6.4
 * Requires PHP:      8.1
 * Author:            FOBO
 * License:           GPLv2 or later
 * License URI:       https://www.gnu.org/licenses/gpl-2.0.html
 * Text Domain:       fobo-data-maker-forms
 * Domain Path:       /languages
 */

if (!defined('ABSPATH')) {
    exit;
}

define('FOBO_DATA_MAKER_FORMS_VERSION',        '1.0.0');
define('FOBO_DATA_MAKER_FORMS_FILE',           __FILE__);
define('FOBO_DATA_MAKER_FORMS_DIR',            plugin_dir_path(__FILE__));
define('FOBO_DATA_MAKER_FORMS_URL',            plugin_dir_url(__FILE__));
// Public DataMaker API endpoint. CloudFront on datamaker-api.fobo-tools.com
// routes /submissions* to the Data Maker API (set up in the data-maker-agent
// CFN stack's CacheBehaviors). Default; can be overridden per-site via:
//   1. wp-config.php constant: define('FOBO_DATA_MAKER_FORMS_SYNC_API_URL', '…');
//      — wins everywhere, even before plugins load.
//   2. WP filter `fobo_data_maker_forms_sync_api_url` — runtime override per request.
// Resolved via fobo_data_maker_forms_sync_api_url(); never read the constant directly.
if (!defined('FOBO_DATA_MAKER_FORMS_SYNC_API_URL')) {
    define('FOBO_DATA_MAKER_FORMS_SYNC_API_URL', 'https://datamaker-api.fobo-tools.com/');
}

require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/DmfReader.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/FormStore.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/MessageCatalog.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/BundleBuilder.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/SubmitProxy.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Shortcode.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Block.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Admin/PageHeader.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Admin/UploadPage.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Admin/FormsListPage.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Admin/FormSettingsPage.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Admin/PreviewPage.php';
require_once FOBO_DATA_MAKER_FORMS_DIR . 'includes/Admin/SettingsPage.php';

register_activation_hook(__FILE__, ['\\Fobo\\DataMakerForms\\FormStore', 'install_table']);

// ── Plugin helpers (global namespace; callers use the leading-backslash
//    form so they resolve regardless of the calling file's namespace). ──

/**
 * Single source of truth for the sync Lambda URL. Reads
 * FOBO_DATA_MAKER_FORMS_SYNC_API_URL (wp-config or plugin default) and then applies
 * the `fobo_data_maker_forms_sync_api_url` filter so per-env overrides land
 * cleanly. Always call this — never `FOBO_DATA_MAKER_FORMS_SYNC_API_URL` directly.
 */
function fobo_data_maker_forms_sync_api_url(): string {
    $url = (string)apply_filters('fobo_data_maker_forms_sync_api_url', FOBO_DATA_MAKER_FORMS_SYNC_API_URL);
    return rtrim($url, '/') . '/';
}

/**
 * Capability gate. Plugin admin surfaces check `manage_fobo_data_maker_forms`,
 * which by default maps to the standard `manage_options` capability
 * (Administrators only). Hosts that want Editors to be able to upload /
 * configure forms can add the capability to the role with:
 *
 *     get_role('editor')->add_cap('manage_fobo_data_maker_forms');
 *
 * Defaults stay safe (admin-only) — `manage_options` is also accepted so
 * existing installs keep working without touching role caps.
 */
function fobo_data_maker_forms_user_can_manage(): bool {
    return current_user_can('manage_fobo_data_maker_forms') || current_user_can('manage_options');
}

// Translations load automatically: since WordPress 4.6, plugins hosted on
// WordPress.org have their /languages .mo files loaded on demand by core, so
// an explicit load_plugin_textdomain() call is no longer needed (and Plugin
// Check flags it as discouraged).

// Map our meta cap to itself so role grants are honored verbatim. Hosts
// that want a different mapping can add their own higher-priority filter.
add_filter('map_meta_cap', function ($caps, $cap) {
    if ($cap === 'manage_fobo_data_maker_forms') {
        return ['manage_fobo_data_maker_forms'];
    }
    return $caps;
}, 10, 2);

add_action('init',                  ['\\Fobo\\DataMakerForms\\Shortcode',           'register']);
add_action('init',                  ['\\Fobo\\DataMakerForms\\Block',               'register']);
add_action('admin_init',            ['\\Fobo\\DataMakerForms\\FormStore',           'maybe_upgrade']);
add_action('admin_menu',            ['\\Fobo\\DataMakerForms\\Admin\\UploadPage',       'register_menu']);
add_action('admin_menu',            ['\\Fobo\\DataMakerForms\\Admin\\FormSettingsPage', 'register_menu']);
add_action('admin_menu',            ['\\Fobo\\DataMakerForms\\Admin\\PreviewPage',     'register_menu']);
add_action('admin_init',            ['\\Fobo\\DataMakerForms\\Admin\\SettingsPage', 'register_settings']);
add_action('template_redirect',     ['\\Fobo\\DataMakerForms\\Admin\\PreviewPage', 'maybe_serve_frontend_preview']);
add_action('rest_api_init',         ['\\Fobo\\DataMakerForms\\SubmitProxy',         'register_routes']);
add_action('rest_api_init',         ['\\Fobo\\DataMakerForms\\Block',               'register_rest_routes']);
add_action('wp_enqueue_scripts',    ['\\Fobo\\DataMakerForms\\Shortcode',           'register_assets']);
add_action('enqueue_block_editor_assets', ['\\Fobo\\DataMakerForms\\Block',         'register_assets']);
