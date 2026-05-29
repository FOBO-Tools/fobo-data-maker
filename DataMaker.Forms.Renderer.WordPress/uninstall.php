<?php
/**
 * Cleanup hook fired by WordPress when an administrator deletes the
 * plugin via Plugins → Delete. Drops the custom form-bundle table and
 * removes plugin options + transients so a fresh install starts clean.
 *
 * NOT fired on plugin deactivate — only on full delete. Submitter PII
 * never lives in the wp_dm_forms table (the form definitions are
 * author-uploaded .dmf bundles, not submissions; submissions are
 * sealed and forwarded to the Data Maker API), so dropping the table
 * doesn't risk losing user data.
 *
 * Multisite: this file is included once per blog on network-wide
 * delete, so `dbDelta` / `DROP TABLE` runs against each blog's prefix
 * via the per-blog $wpdb instance.
 */

if (!defined('WP_UNINSTALL_PLUGIN')) {
    exit;
}

global $wpdb;

// Drop the custom forms table. `IF EXISTS` keeps the call idempotent
// across re-installs and across the multisite per-blog iteration.
$table = $wpdb->prefix . 'dm_forms';
$wpdb->query("DROP TABLE IF EXISTS `{$table}`");

// Remove every option the plugin writes. Transients used for rate-
// limit bucketing are short-lived (90 s) so they evaporate without
// help, but `delete_transient` with a wildcard isn't a thing, so we
// only target the persistent options here.
delete_option('datamaker_renderer_settings');
delete_option('dm_renderer_db_version');

// Best-effort transient cleanup for the rate-limit buckets so we
// don't leave thousands of orphan `dm_rl_*` rows in wp_options on a
// busy site. Wildcards aren't supported by delete_transient, so we
// SELECT the option_name values matching our prefix and delete each.
$rl_keys = $wpdb->get_col(
    "SELECT option_name FROM {$wpdb->options}
     WHERE option_name LIKE '\\_transient\\_dm\\_rl\\_%'
        OR option_name LIKE '\\_transient\\_timeout\\_dm\\_rl\\_%'"
);
foreach ($rl_keys as $opt) {
    delete_option($opt);
}
