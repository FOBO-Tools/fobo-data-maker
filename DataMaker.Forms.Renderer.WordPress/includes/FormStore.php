<?php
namespace DataMaker\Forms\Renderer\WordPress;

if (!defined('ABSPATH')) exit;

// This class is the plugin's sole data-access layer for the custom
// `{prefix}_dm_forms` table. Every read/write is inherently a direct DB call;
// all values go through $wpdb->prepare()/update()/insert()/delete() (auto-
// escaped), and the only string interpolation is the table name, which is a
// code constant (self::table()), never user input. Caching is intentionally
// skipped — these are low-volume admin writes plus one per-render lookup.
// phpcs:disable WordPress.DB.DirectDatabaseQuery.DirectQuery
// phpcs:disable WordPress.DB.DirectDatabaseQuery.NoCaching
// phpcs:disable WordPress.DB.PreparedSQL.NotPrepared
// phpcs:disable PluginCheck.Security.DirectDB.UnescapedDBParameter

/**
 * Custom WP table holding every uploaded .dmf. Form lookup is by `slug`
 * (admin-chosen) and `form_id` (the .dmf's own id) — the shortcode uses the
 * slug so authors can pick something readable. Re-uploading the same slug
 * overwrites in-place, which keeps the shortcode embedded in a page valid
 * across form revisions.
 */
final class FormStore
{
    /** Bump on schema changes; maybe_upgrade re-runs dbDelta when current option is stale. */
    private const DB_VERSION = 11;

    public static function table(): string
    {
        global $wpdb;
        return $wpdb->prefix . 'dm_forms';
    }

    public static function install_table(): void
    {
        global $wpdb;
        $table   = self::table();
        $charset = $wpdb->get_charset_collate();

        // dbDelta needs each column on its own line + double spaces around PK.
        $sql = "CREATE TABLE {$table} (
            id BIGINT UNSIGNED AUTO_INCREMENT,
            slug VARCHAR(120) NOT NULL,
            form_id VARCHAR(80) NOT NULL,
            schema_version INT NOT NULL DEFAULT 1,
            form_json LONGTEXT NOT NULL,
            compiled_json LONGTEXT NULL,
            element_css_json LONGTEXT NULL,
            palette_css LONGTEXT NULL,
            fonts_css LONGTEXT NULL,
            use_theme TINYINT NULL,
            edit_flow TINYINT NULL,
            hidden_elements LONGTEXT NULL,
            message_overrides LONGTEXT NULL,
            after_submit VARCHAR(500) NULL,
            success_message LONGTEXT NULL,
            privacy_url VARCHAR(500) NULL,
            privacy_link_text VARCHAR(200) NULL,
            consent_required TINYINT NULL,
            consent_label LONGTEXT NULL,
            webhook_url VARCHAR(500) NULL,
            honeypot_on TINYINT NULL,
            rate_limit_per_min INT NULL,
            turnstile_on TINYINT NULL,
            signature_verified TINYINT(1) NOT NULL DEFAULT 0,
            signer_pubkey VARCHAR(80) NULL,
            recipient_user_id VARCHAR(120) NULL,
            recipient_pubkey VARCHAR(80) NULL,
            fobo_verified TINYINT(1) NOT NULL DEFAULT 0,
            fobo_email VARCHAR(255) NULL,
            fobo_company VARCHAR(255) NULL,
            uploaded_at DATETIME NOT NULL,
            uploaded_by BIGINT UNSIGNED NULL,
            PRIMARY KEY  (id),
            UNIQUE KEY slug (slug)
        ) {$charset};";

        require_once ABSPATH . 'wp-admin/includes/upgrade.php';
        dbDelta($sql);
        update_option('dm_renderer_db_version', self::DB_VERSION);
    }

    /**
     * Idempotent schema upgrade. Hooked on admin_init so existing installs
     * pick up new columns / indexes without a full deactivate/activate cycle.
     */
    public static function maybe_upgrade(): void
    {
        if ((int)get_option('dm_renderer_db_version', 0) >= self::DB_VERSION) return;
        self::install_table();
    }

    public static function upsert(array $bundle, string $slug, int $user_id, ?bool $use_theme = true): int
    {
        global $wpdb;
        $table     = self::table();
        $slug      = sanitize_title($slug);
        $recipient = $bundle['recipient'] ?? null;
        $fobo      = $bundle['fobo'] ?? null; // verified FOBO attestation, or null

        $row = [
            'slug'               => $slug,
            'form_id'            => (string)$bundle['form_id'],
            'schema_version'     => (int)$bundle['schema_version'],
            'form_json'          => wp_json_encode($bundle['form']),
            'compiled_json'      => wp_json_encode($bundle['compiled']),
            'element_css_json'   => wp_json_encode($bundle['element_css']),
            'palette_css'        => (string)$bundle['palette_css'],
            'fonts_css'          => (string)($bundle['fonts_css'] ?? ''),
            'use_theme'          => $use_theme === null ? null : ($use_theme ? 1 : 0),
            'signature_verified' => (int)(bool)$bundle['signature_verified'],
            'signer_pubkey'      => (string)$bundle['signer_pubkey'],
            'recipient_user_id'  => is_array($recipient) ? (string)($recipient['userId']    ?? '') : null,
            'recipient_pubkey'   => is_array($recipient) ? (string)($recipient['publicKey'] ?? '') : null,
            'fobo_verified'      => is_array($fobo) ? 1 : 0,
            'fobo_email'         => is_array($fobo) ? ($fobo['email']   ?? null) : null,
            'fobo_company'       => is_array($fobo) ? ($fobo['company'] ?? null) : null,
            'uploaded_at'        => current_time('mysql', true),
            'uploaded_by'        => $user_id,
        ];

        $existing = self::find_by_slug($slug);
        if ($existing) {
            $wpdb->update($table, $row, ['id' => $existing['id']]);
            return (int)$existing['id'];
        }
        $wpdb->insert($table, $row);
        return (int)$wpdb->insert_id;
    }

    public static function set_use_theme(int $id, bool $use_theme): void
    {
        global $wpdb;
        $wpdb->update(self::table(), ['use_theme' => $use_theme ? 1 : 0], ['id' => $id]);
    }

    public static function set_edit_flow(int $id, bool $on): void
    {
        global $wpdb;
        $wpdb->update(self::table(), ['edit_flow' => $on ? 1 : 0], ['id' => $id]);
    }

    /**
     * Per-form anti-abuse + privacy + integration knobs. All optional —
     * null means "use the plugin default / off". The form-settings admin
     * page is the only writer; SubmitProxy reads them at submit time.
     */
    /**
     * Persist the privacy-policy target. Stored as either:
     *   ''           — not set
     *   'page:123'   — link to the WP page/post with that id (resolved at
     *                  render time via get_permalink)
     *   'https://…'  — fully-qualified external URL
     */
    public static function set_privacy_url(int $id, string $value): void {
        global $wpdb;
        $clean = trim($value);
        if ($clean !== '' && !str_starts_with($clean, 'page:')) {
            $clean = esc_url_raw($clean);
        }
        $wpdb->update(self::table(), ['privacy_url' => $clean], ['id' => $id]);
    }

    /**
     * Resolve a stored privacy_url value to a final URL string. Mirrors
     * resolve_after_submit_url so the consent-block link gets a
     * permalink even when the admin picked a WP page from the dropdown.
     */
    public static function resolve_privacy_url(?string $stored): string {
        if (!$stored) return '';
        $stored = trim($stored);
        if ($stored === '') return '';
        if (str_starts_with($stored, 'page:')) {
            $pid = (int)substr($stored, 5);
            if ($pid <= 0) return '';
            $url = get_permalink($pid);
            return $url ? (string)$url : '';
        }
        return esc_url_raw($stored);
    }
    public static function set_privacy_link_text(int $id, string $text): void {
        global $wpdb;
        $wpdb->update(self::table(), ['privacy_link_text' => sanitize_text_field($text)], ['id' => $id]);
    }

    public static function set_consent_required(int $id, bool $on, string $label): void {
        global $wpdb;
        $wpdb->update(self::table(), [
            'consent_required' => $on ? 1 : 0,
            'consent_label'    => $label,
        ], ['id' => $id]);
    }
    public static function set_webhook_url(int $id, string $url): void {
        global $wpdb;
        $wpdb->update(self::table(), ['webhook_url' => esc_url_raw($url)], ['id' => $id]);
    }
    public static function set_honeypot(int $id, bool $on): void {
        global $wpdb;
        $wpdb->update(self::table(), ['honeypot_on' => $on ? 1 : 0], ['id' => $id]);
    }
    public static function set_rate_limit(int $id, int $perMin): void {
        global $wpdb;
        $wpdb->update(self::table(), ['rate_limit_per_min' => max(0, $perMin)], ['id' => $id]);
    }
    public static function set_turnstile(int $id, bool $on): void {
        global $wpdb;
        $wpdb->update(self::table(), ['turnstile_on' => $on ? 1 : 0], ['id' => $id]);
    }

    /** Persist the (sanitized) array of hidden col/field ids for a form. */
    public static function set_hidden_elements(int $id, array $hidden): void
    {
        global $wpdb;
        $clean = array_values(array_unique(array_filter(array_map('strval', $hidden), fn($s) => $s !== '')));
        $wpdb->update(self::table(), ['hidden_elements' => wp_json_encode($clean)], ['id' => $id]);
    }

    /**
     * Persist the after-submit redirect target. Stored as either:
     *   ''           — stay on page (default)
     *   'page:123'   — redirect to the WP page/post with that id (resolved at shortcode render time via get_permalink)
     *   'https://…'  — redirect to a fully-qualified URL
     */
    public static function set_after_submit(int $id, string $value): void
    {
        global $wpdb;
        $wpdb->update(self::table(), ['after_submit' => $value], ['id' => $id]);
    }

    /** Persist the per-form Markdown shown in stay-on-page mode after a
     *  successful submission. Empty falls back to a generic default at
     *  render time. */
    public static function set_success_message(int $id, string $markdown): void
    {
        global $wpdb;
        $wpdb->update(self::table(), ['success_message' => $markdown], ['id' => $id]);
    }

    /** The Markdown to render in the form-root after a successful submit
     *  when after_submit is "stay on page". Empty stored value =>
     *  default polite confirmation. */
    public static function resolve_success_message(?string $stored): string
    {
        $s = trim((string)($stored ?? ''));
        return $s !== '' ? $s : __('## Thanks for your submission.', 'datamaker-renderer');
    }

    /**
     * Resolve a stored `after_submit` value to a final URL string. Empty
     * stored value or unresolvable page returns ''. The renderer treats ''
     * as "stay on page".
     */
    public static function resolve_after_submit_url(?string $stored): string
    {
        if (!$stored) return '';
        $stored = trim($stored);
        if ($stored === '') return '';
        if (str_starts_with($stored, 'page:')) {
            $pid = (int)substr($stored, 5);
            if ($pid <= 0) return '';
            $url = get_permalink($pid);
            return $url ? (string)$url : '';
        }
        return esc_url_raw($stored);
    }

    /**
     * Persist per-form error-message overrides. Shape:
     *   { fieldName: { msgId: text } }
     * Empty / missing entries fall through to the schema's own message
     * (FieldDefinition.Messages), then to the engine default. BundleBuilder
     * merges this into each field before the bundle reaches the browser.
     */
    public static function set_message_overrides(int $id, array $overrides): void
    {
        global $wpdb;
        $clean = self::sanitize_message_overrides($overrides);
        $wpdb->update(self::table(), ['message_overrides' => wp_json_encode($clean)], ['id' => $id]);
    }

    /** Decode + sanitize the JSON blob, dropping empty text and unknown shapes. */
    public static function get_message_overrides(array $row): array
    {
        $raw = $row['message_overrides'] ?? '';
        if (!$raw) return [];
        $decoded = json_decode((string)$raw, true);
        return is_array($decoded) ? self::sanitize_message_overrides($decoded) : [];
    }

    private static function sanitize_message_overrides(array $in): array
    {
        $out = [];
        foreach ($in as $fieldName => $bag) {
            if (!is_array($bag)) continue;
            // Reserved key '__form' keeps form-level (non-field) overrides
            // — currently just 'validationBanner'. sanitize_text_field
            // strips it (no spaces), so allow it through verbatim.
            $name = $fieldName === '__form' ? '__form' : sanitize_text_field((string)$fieldName);
            if ($name === '') continue;
            $clean = [];
            foreach ($bag as $msgId => $text) {
                $id = sanitize_key((string)$msgId);
                $t  = is_string($text) ? trim($text) : '';
                if ($id !== '' && $t !== '') $clean[$id] = $t;
            }
            if ($clean) $out[$name] = $clean;
        }
        return $out;
    }

    public static function get_hidden_elements(array $row): array
    {
        $raw = $row['hidden_elements'] ?? '';
        if (!$raw) return [];
        $decoded = json_decode((string)$raw, true);
        return is_array($decoded) ? array_values(array_filter(array_map('strval', $decoded), fn($s) => $s !== '')) : [];
    }

    public static function find_by_slug(string $slug): ?array
    {
        global $wpdb;
        $row = $wpdb->get_row(
            $wpdb->prepare("SELECT * FROM " . self::table() . " WHERE slug = %s", $slug),
            ARRAY_A
        );
        return $row ?: null;
    }

    public static function delete(int $id): void
    {
        global $wpdb;
        $wpdb->delete(self::table(), ['id' => $id]);
    }

    public static function list_all(): array
    {
        global $wpdb;
        $rows = $wpdb->get_results("SELECT id, slug, form_id, schema_version, signature_verified, use_theme, uploaded_at FROM " . self::table() . " ORDER BY uploaded_at DESC", ARRAY_A);
        return $rows ?: [];
    }
}
// phpcs:enable WordPress.DB.DirectDatabaseQuery.DirectQuery
// phpcs:enable WordPress.DB.DirectDatabaseQuery.NoCaching
// phpcs:enable WordPress.DB.PreparedSQL.NotPrepared
// phpcs:enable PluginCheck.Security.DirectDB.UnescapedDBParameter
