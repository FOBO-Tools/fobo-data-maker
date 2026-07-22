<?php
/**
 * Cleanup hook fired by WordPress when an administrator deletes the
 * plugin via Plugins → Delete. Drops the custom form-bundle table and
 * removes plugin options + transients so a fresh install starts clean.
 *
 * NOT fired on plugin deactivate — only on full delete. Submitter PII
 * never lives in the wp_fobo_data_maker_forms table (the form definitions
 * are author-uploaded .dmf bundles, not submissions; submissions are
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
$fobo_data_maker_forms_table = $wpdb->prefix . 'fobo_data_maker_forms';
// phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.DirectDatabaseQuery.SchemaChange, WordPress.DB.PreparedSQL.InterpolatedNotPrepared, PluginCheck.Security.DirectDB.UnescapedDBParameter -- one-time uninstall DROP of our own custom table; name is a code constant, not user input.
$wpdb->query("DROP TABLE IF EXISTS `{$fobo_data_maker_forms_table}`");

// Remove every option the plugin writes. Transients used for rate-
// limit bucketing are short-lived (90 s) so they evaporate without
// help, but `delete_transient` with a wildcard isn't a thing, so we
// only target the persistent options here.
delete_option('fobo_data_maker_forms_settings');
delete_option('fobo_data_maker_forms_db_version');

// Best-effort transient cleanup for the rate-limit buckets so we
// don't leave thousands of orphan `fobo_data_maker_forms_rl_*` rows in
// wp_options on a busy site. Wildcards aren't supported by
// delete_transient, so we SELECT the option_name values matching our
// prefix and delete each.
// phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.NotPrepared, PluginCheck.Security.DirectDB.UnescapedDBParameter -- one-time uninstall scan of the core options table with a literal LIKE prefix; no user input.
$fobo_data_maker_forms_rl_keys = $wpdb->get_col(
    "SELECT option_name FROM {$wpdb->options}
     WHERE option_name LIKE '\\_transient\\_fobo\\_data\\_maker\\_forms\\_rl\\_%'
        OR option_name LIKE '\\_transient\\_timeout\\_fobo\\_data\\_maker\\_forms\\_rl\\_%'"
);
foreach ($fobo_data_maker_forms_rl_keys as $fobo_data_maker_forms_opt) {
    delete_option($fobo_data_maker_forms_opt);
}
