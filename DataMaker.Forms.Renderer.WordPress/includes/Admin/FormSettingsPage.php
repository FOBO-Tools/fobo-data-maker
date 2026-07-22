<?php
namespace Fobo\DataMakerForms\Admin;

use Fobo\DataMakerForms\FormStore;
use Fobo\DataMakerForms\MessageCatalog;

if (!defined('ABSPATH')) exit;

/**
 * Per-form settings — theme override, edit-flow toggle, per-element
 * visibility checklist. Reachable via ?page=fobo-data-maker-forms-form
 * &form_id={id}; the Forms list page links to it per row.
 *
 * The visibility list walks every section / row / column in the form's
 * layout plus the standalone field bag, lets the WP admin uncheck the
 * pieces they don't want shown to submitters, and persists the chosen
 * ids as a JSON array under wp_fobo_data_maker_forms.hidden_elements. BundleBuilder
 * filters those out of the bundle before the renderer ever sees them,
 * so the layout grid auto-flows around the gaps (matches the
 * relation-skip behaviour on the renderer side).
 */
final class FormSettingsPage
{
    public static function register_menu(): void
    {
        // Hidden submenu — registered under the FOBO Data Maker Forms parent
        // so the page slug exists for permalinks, but `null` as menu
        // title keeps it out of the sidebar (it's reached via the
        // Forms list "Settings" link per row).
        add_submenu_page(
            'fobo-data-maker-forms',
            __('Form settings', 'fobo-data-maker-forms'),
            null,
            'manage_options',
            'fobo-data-maker-forms-form',
            [self::class, 'render']
        );
    }

    public static function render(): void
    {
        if (!\fobo_data_maker_forms_user_can_manage()) return;

        $form_id = isset($_GET['form_id']) ? (int)$_GET['form_id'] : 0;
        if ($form_id <= 0) {
            echo '<div class="wrap"><h1>' . esc_html__('Form settings', 'fobo-data-maker-forms') . '</h1><p>' . esc_html__('Missing or invalid form id.', 'fobo-data-maker-forms') . '</p></div>';
            return;
        }

        // Re-fetch by id (FormStore exposes by slug; reuse list_all + filter for now).
        global $wpdb;
        // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.NotPrepared, PluginCheck.Security.DirectDB.UnescapedDBParameter -- custom plugin table; id is placeholdered via prepare(), table name is a code constant, single admin read.
        $row = $wpdb->get_row($wpdb->prepare("SELECT * FROM " . FormStore::table() . " WHERE id = %d", $form_id), ARRAY_A);
        if (!$row) {
            echo '<div class="wrap"><h1>' . esc_html__('Form settings', 'fobo-data-maker-forms') . '</h1><p>' . esc_html__('Form not found.', 'fobo-data-maker-forms') . '</p></div>';
            return;
        }

        $notice = null;
        if (!empty($_POST['dm_form_settings_nonce'])
            && wp_verify_nonce(sanitize_text_field(wp_unslash($_POST['dm_form_settings_nonce'])), 'dm_form_settings')) {
            FormStore::set_use_theme($form_id, !empty($_POST['use_theme']));
            FormStore::set_edit_flow($form_id, !empty($_POST['edit_flow']));

            // After-submit target: radio picks the kind, the matching value
            // is the source. URL kind validates as a real URL; page kind
            // validates the page exists. Stay-on-page = empty string.
            $kind = sanitize_text_field(wp_unslash($_POST['after_submit_kind'] ?? 'none'));
            $stored = '';
            if ($kind === 'page') {
                $pid = absint(wp_unslash($_POST['after_submit_page'] ?? 0));
                if ($pid > 0) $stored = 'page:' . $pid;
            } elseif ($kind === 'url') {
                $url = esc_url_raw(wp_unslash($_POST['after_submit_url'] ?? ''));
                if ($url) $stored = $url;
            }
            FormStore::set_after_submit($form_id, $stored);

            // Markdown allowed; wp_kses_post strips JS / unsafe HTML but keeps
            // headings, lists, links, emphasis. Empty stays empty (renderer
            // falls back to the default at render time).
            $msg = isset($_POST['success_message'])
                ? wp_kses_post(wp_unslash((string)$_POST['success_message']))
                : '';
            FormStore::set_success_message($form_id, $msg);
            $hidden = isset($_POST['hidden']) && is_array($_POST['hidden'])
                ? array_map('sanitize_text_field', wp_unslash($_POST['hidden']))
                : [];
            // Defence-in-depth: drop ids that belong to required fields so a
            // tampered form post can't hide them. The UI also disables the
            // checkbox for required items, so this only kicks in on abuse.
            $form_now      = json_decode($row['form_json'], true) ?: [];
            $required_ids  = self::required_field_ids($form_now);
            if ($required_ids) {
                $hidden = array_values(array_diff($hidden, $required_ids));
            }
            FormStore::set_hidden_elements($form_id, $hidden);

            // Per-form message overrides — only persist the non-empty
            // textboxes; FormStore sanitises the rest (drops empty / unknown
            // shape entries).
            // Nested {field:{msgId:text}} map; sanitize every leaf here
            // (map_deep walks the array) before it ever reaches FormStore.
            $msg_in = isset($_POST['msg']) && is_array($_POST['msg'])
                ? map_deep(wp_unslash($_POST['msg']), 'sanitize_text_field')
                : [];
            FormStore::set_message_overrides($form_id, is_array($msg_in) ? $msg_in : []);

            // Privacy / consent / anti-abuse / integrations
            $privacy_kind = sanitize_text_field(wp_unslash($_POST['privacy_kind'] ?? 'none'));
            $privacy_value = '';
            if ($privacy_kind === 'page') {
                $pid = absint(wp_unslash($_POST['privacy_page'] ?? 0));
                if ($pid > 0) $privacy_value = 'page:' . $pid;
            } elseif ($privacy_kind === 'url') {
                $privacy_value = esc_url_raw(wp_unslash($_POST['privacy_url'] ?? ''));
            }
            FormStore::set_privacy_url($form_id, $privacy_value);
            FormStore::set_privacy_link_text($form_id, sanitize_text_field(wp_unslash($_POST['privacy_link_text'] ?? '')));
            FormStore::set_consent_required(
                $form_id,
                !empty($_POST['consent_required']),
                sanitize_text_field(wp_unslash($_POST['consent_label'] ?? ''))
            );
            FormStore::set_webhook_url($form_id, esc_url_raw(wp_unslash($_POST['webhook_url'] ?? '')));
            FormStore::set_honeypot($form_id, !empty($_POST['honeypot_on']));
            FormStore::set_rate_limit($form_id, absint(wp_unslash($_POST['rate_limit_per_min'] ?? 0)));
            FormStore::set_turnstile($form_id, !empty($_POST['turnstile_on']));

            // phpcs:ignore WordPress.DB.DirectDatabaseQuery.DirectQuery, WordPress.DB.DirectDatabaseQuery.NoCaching, WordPress.DB.PreparedSQL.NotPrepared, PluginCheck.Security.DirectDB.UnescapedDBParameter -- custom plugin table; id is placeholdered via prepare(), table name is a code constant, single admin read.
            $row = $wpdb->get_row($wpdb->prepare("SELECT * FROM " . FormStore::table() . " WHERE id = %d", $form_id), ARRAY_A);
            $notice = __('Saved.', 'fobo-data-maker-forms');
        }

        $form           = json_decode($row['form_json'], true) ?: [];
        $hidden_now     = FormStore::get_hidden_elements($row);
        $msg_overrides  = FormStore::get_message_overrides($row);
        $privacy_url       = (string)($row['privacy_url']         ?? '');
        $privacy_link_text = (string)($row['privacy_link_text']   ?? '');
        $consent_on        = !empty($row['consent_required']);
        $consent_label     = (string)($row['consent_label']       ?? '');
        $webhook_url    = (string)($row['webhook_url']        ?? '');
        $honeypot_on    = !empty($row['honeypot_on']);
        $rate_limit     = (int)($row['rate_limit_per_min'] ?? 0);
        $turnstile_on   = !empty($row['turnstile_on']);
        $turnstile_site_key = (string)(SettingsPage::get()['turnstile_site_key'] ?? '');
        $hiddenSet    = array_flip($hidden_now);
        $useThemeOn   = $row['use_theme'] === null || $row['use_theme'] === '' ? true  : (bool)(int)$row['use_theme'];
        $editFlowOn   = $row['edit_flow'] === null || $row['edit_flow'] === '' ? false : (bool)(int)$row['edit_flow'];
        $items        = self::enumerate($form);
        $back_url     = admin_url('admin.php?page=fobo-data-maker-forms-forms');

        ?>
        <div class="wrap">
            <?php PageHeader::render(
                /* translators: %s = form name or slug */
                sprintf(__('Form settings — %s', 'fobo-data-maker-forms'), (string)($form['name'] ?? $row['slug']))
            ); ?>
            <p><a href="<?php echo esc_url($back_url); ?>">← <?php esc_html_e('Back to forms', 'fobo-data-maker-forms'); ?></a></p>

            <?php if ($notice): ?>
                <div class="notice notice-success is-dismissible"><p><?php echo esc_html($notice); ?></p></div>
            <?php endif; ?>

            <form method="post">
                <?php wp_nonce_field('dm_form_settings', 'dm_form_settings_nonce'); ?>

                <h2><?php esc_html_e('Behaviour', 'fobo-data-maker-forms'); ?></h2>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><?php esc_html_e('Designer styling', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="use_theme" value="1" <?php checked($useThemeOn); ?>>
                                <?php esc_html_e('Apply Theme/Styling', 'fobo-data-maker-forms'); ?>
                            </label>
                            <p class="description"><?php esc_html_e('On = render the form the way it looks in the desktop designer (palette, fonts, button variants, heading styles, per-element overrides). Off = strip all of that and let the active WordPress theme drive the look. Layout (rows, columns, spacing) is honored in both modes.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Edit flow', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="edit_flow" value="1" <?php checked($editFlowOn); ?>>
                                <?php esc_html_e('Let submitters edit their submission later (browser localStorage)', 'fobo-data-maker-forms'); ?>
                            </label>
                            <p class="description"><?php esc_html_e('When on, a submitter returning to the same form on the same browser sees "Continue editing?" before a fresh start.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('After submit', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <?php
                            $stored      = (string)($row['after_submit'] ?? '');
                            $kindCurrent = $stored === '' ? 'none' : (str_starts_with($stored, 'page:') ? 'page' : 'url');
                            $pageCurrent = $kindCurrent === 'page' ? (int)substr($stored, 5) : 0;
                            $urlCurrent  = $kindCurrent === 'url'  ? $stored : '';
                            $pages = get_pages(['sort_column' => 'post_title', 'post_status' => 'publish']);
                            ?>
                            <p>
                                <label><input type="radio" name="after_submit_kind" value="none" <?php checked($kindCurrent, 'none'); ?>>
                                <?php esc_html_e('Stay on page (default)', 'fobo-data-maker-forms'); ?></label>
                            </p>
                            <p>
                                <label><input type="radio" name="after_submit_kind" value="page" <?php checked($kindCurrent, 'page'); ?>>
                                <?php esc_html_e('Redirect to a WordPress page:', 'fobo-data-maker-forms'); ?></label>
                                <select name="after_submit_page" style="margin-left:8px">
                                    <option value="0">— <?php esc_html_e('select a page', 'fobo-data-maker-forms'); ?> —</option>
                                    <?php foreach ($pages as $p): ?>
                                        <option value="<?php echo (int)$p->ID; ?>" <?php selected($pageCurrent, (int)$p->ID); ?>>
                                            <?php echo esc_html($p->post_title); ?>
                                        </option>
                                    <?php endforeach; ?>
                                </select>
                            </p>
                            <p>
                                <label><input type="radio" name="after_submit_kind" value="url" <?php checked($kindCurrent, 'url'); ?>>
                                <?php esc_html_e('Redirect to a URL:', 'fobo-data-maker-forms'); ?></label>
                                <input type="url" class="regular-text" name="after_submit_url" value="<?php echo esc_attr($urlCurrent); ?>" placeholder="https://…" style="margin-left:8px">
                            </p>
                            <p class="description"><?php esc_html_e('Browser navigates to the chosen target after a successful submission. "Stay on page" replaces the form with the success message below.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Success message', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <?php
                            $msgStored  = (string)($row['success_message'] ?? '');
                            $msgDefault = __('## Thanks for your submission.', 'fobo-data-maker-forms');
                            $msgDisplay = $msgStored !== '' ? $msgStored : $msgDefault;
                            ?>
                            <textarea name="success_message" class="large-text code" rows="6" placeholder="<?php echo esc_attr($msgDefault); ?>"><?php echo esc_textarea($msgDisplay); ?></textarea>
                            <p class="description"><?php
                                printf(
                                    wp_kses(
                                        /* translators: %1$s-%5$s = Markdown syntax examples wrapped in <code>; %6$s = "Stay on page" wrapped in <em> */
                                        __('Markdown allowed (%1$s, %2$s, %3$s, %4$s, %5$s). Rendered inside the form\'s container after a successful submit when "After submit" is set to %6$s; ignored for redirect modes. Leave blank for the default.', 'fobo-data-maker-forms'),
                                        ['code' => [], 'em' => []]
                                    ),
                                    '<code>## Heading</code>',
                                    '<code>**bold**</code>',
                                    '<code>*italic*</code>',
                                    '<code>- bullet</code>',
                                    '<code>[link](url)</code>',
                                    '<em>' . esc_html__('Stay on page', 'fobo-data-maker-forms') . '</em>'
                                );
                            ?></p>
                        </td>
                    </tr>
                </table>

                <h2><?php esc_html_e('Visibility', 'fobo-data-maker-forms'); ?></h2>
                <p><?php esc_html_e('Uncheck any item to hide it from this form on the WordPress site. Filtering happens server-side, before the renderer reads the form, so the layout grid auto-flows around the gaps.', 'fobo-data-maker-forms'); ?></p>

                <table class="widefat striped">
                    <thead>
                        <tr>
                            <th style="width:80px">
                                <label title="<?php esc_attr_e('Show or hide every non-required item at once', 'fobo-data-maker-forms'); ?>">
                                    <input type="checkbox" id="dm-vis-all"> <?php esc_html_e('all', 'fobo-data-maker-forms'); ?>
                                </label>
                            </th>
                            <th><?php esc_html_e('Item', 'fobo-data-maker-forms'); ?></th>
                            <th><?php esc_html_e('Kind', 'fobo-data-maker-forms'); ?></th>
                            <th><?php esc_html_e('Where', 'fobo-data-maker-forms'); ?></th>
                        </tr>
                    </thead>
                    <tbody>
                    <?php if (!$items): ?>
                        <tr><td colspan="4"><?php esc_html_e('No layout elements found.', 'fobo-data-maker-forms'); ?></td></tr>
                    <?php else: foreach ($items as $it):
                        $required = !empty($it['required']);
                        // Render: checked = visible (matches user mental
                        // model). On submit the JS below inverts so the form
                        // POSTs the HIDDEN ids — adding new elements to the
                        // form definition later defaults them visible.
                        $visible  = $required || !isset($hiddenSet[$it['id']]);
                    ?>
                        <tr>
                            <td>
                                <input type="checkbox"
                                    name="hidden[]"
                                    value="<?php echo esc_attr($it['id']); ?>"
                                    <?php echo $visible ? 'checked' : ''; ?>
                                    <?php echo $required ? 'disabled' : 'data-invert="1"'; ?>
                                    >
                            </td>
                            <td>
                                <?php echo esc_html($it['label']); ?>
                                <?php if ($required): ?>
                                    <span style="color:#a8201a;font-size:11px;margin-left:6px;" title="<?php esc_attr_e('Required fields can\'t be hidden', 'fobo-data-maker-forms'); ?>"><?php esc_html_e('required', 'fobo-data-maker-forms'); ?></span>
                                <?php endif; ?>
                            </td>
                            <td><code><?php echo esc_html($it['kind']); ?></code></td>
                            <td><?php echo esc_html($it['where']); ?></td>
                        </tr>
                    <?php endforeach; endif; ?>
                    </tbody>
                </table>
                <p class="description"><?php
                    printf(
                        wp_kses(
                            /* translators: %1$s = "visible" wrapped in <strong>; %2$s = hidden[] wrapped in <code> */
                            __('A checked box = %1$s. The form posts the inverted set as %2$s so legacy fields not yet in the form definition aren\'t accidentally hidden when added later.', 'fobo-data-maker-forms'),
                            ['strong' => [], 'code' => []]
                        ),
                        '<strong>' . esc_html__('visible', 'fobo-data-maker-forms') . '</strong>',
                        '<code>hidden[]</code>'
                    );
                ?></p>

                <h2 style="margin-top:32px;"><?php esc_html_e('Privacy &amp; consent', 'fobo-data-maker-forms'); ?></h2>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><?php esc_html_e('Privacy policy target', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <?php
                            $privacy_kind_cur = $privacy_url === ''
                                ? 'none'
                                : (str_starts_with($privacy_url, 'page:') ? 'page' : 'url');
                            $privacy_page_cur = $privacy_kind_cur === 'page' ? (int)substr($privacy_url, 5) : 0;
                            $privacy_url_cur  = $privacy_kind_cur === 'url'  ? $privacy_url : '';
                            $pages_for_privacy = get_pages(['sort_column' => 'post_title', 'post_status' => 'publish']);
                            ?>
                            <p>
                                <label><input type="radio" name="privacy_kind" value="none" <?php checked($privacy_kind_cur, 'none'); ?>>
                                <?php esc_html_e('Not set', 'fobo-data-maker-forms'); ?></label>
                            </p>
                            <p>
                                <label><input type="radio" name="privacy_kind" value="page" <?php checked($privacy_kind_cur, 'page'); ?>>
                                <?php esc_html_e('WordPress page:', 'fobo-data-maker-forms'); ?></label>
                                <select name="privacy_page" style="margin-left:8px">
                                    <option value="0">— <?php esc_html_e('select a page', 'fobo-data-maker-forms'); ?> —</option>
                                    <?php foreach ($pages_for_privacy as $p): ?>
                                        <option value="<?php echo (int)$p->ID; ?>" <?php selected($privacy_page_cur, (int)$p->ID); ?>>
                                            <?php echo esc_html($p->post_title); ?>
                                        </option>
                                    <?php endforeach; ?>
                                </select>
                            </p>
                            <p>
                                <label><input type="radio" name="privacy_kind" value="url" <?php checked($privacy_kind_cur, 'url'); ?>>
                                <?php esc_html_e('External URL:', 'fobo-data-maker-forms'); ?></label>
                                <input type="url" class="regular-text" name="privacy_url"
                                       value="<?php echo esc_attr($privacy_url_cur); ?>"
                                       placeholder="https://example.com/privacy"
                                       style="margin-left:8px;width:100%;max-width:440px;">
                            </p>
                            <p class="description"><?php esc_html_e('Linked from the consent label below when set.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Privacy link text', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <input type="text" name="privacy_link_text"
                                   class="regular-text"
                                   style="width:100%;max-width:560px;"
                                   value="<?php echo esc_attr($privacy_link_text); ?>"
                                   placeholder="<?php esc_attr_e('privacy policy', 'fobo-data-maker-forms'); ?>" />
                            <p class="description"><?php esc_html_e('Text shown as the link to the privacy target. Empty = "privacy policy".', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Require consent before submit', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="consent_required" value="1"
                                       <?php checked($consent_on); ?> />
                                <?php esc_html_e('Show a consent checkbox above the submit button; block POST until ticked.', 'fobo-data-maker-forms'); ?>
                            </label>
                            <p style="margin-top:8px;">
                                <input type="text" name="consent_label"
                                       class="regular-text"
                                       style="width:100%;max-width:560px;"
                                       value="<?php echo esc_attr($consent_label); ?>"
                                       placeholder="<?php esc_attr_e('I agree to the privacy policy.', 'fobo-data-maker-forms'); ?>" />
                            </p>
                            <p class="description"><?php esc_html_e('Use {privacy} as a placeholder to insert a link to the URL above.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                </table>

                <h2 style="margin-top:32px;"><?php esc_html_e('Anti-abuse', 'fobo-data-maker-forms'); ?></h2>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><?php esc_html_e('Honeypot field', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="honeypot_on" value="1"
                                       <?php checked($honeypot_on); ?> />
                                <?php esc_html_e('Render a hidden field; reject submissions that fill it. Recommended.', 'fobo-data-maker-forms'); ?>
                            </label>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Rate limit (per minute, per IP)', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <input type="number" name="rate_limit_per_min" min="0" max="600"
                                   value="<?php echo esc_attr((string)$rate_limit); ?>"
                                   style="width:90px;" />
                            <p class="description"><?php esc_html_e('0 = no limit (a plugin-wide default still applies). Soft throttle enforced via WP transients.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Cloudflare Turnstile', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="turnstile_on" value="1"
                                       <?php checked($turnstile_on); ?>
                                       <?php disabled($turnstile_site_key === ''); ?> />
                                <?php esc_html_e('Require visitors to pass a Turnstile challenge before submitting.', 'fobo-data-maker-forms'); ?>
                            </label>
                            <?php if ($turnstile_site_key === ''): ?>
                                <p class="description" style="color:#a8201a;">
                                    <?php
                                    printf(
                                        /* translators: %s = link to plugin settings */
                                        esc_html__('Turnstile keys not configured. Set them in %s first.', 'fobo-data-maker-forms'),
                                        '<a href="' . esc_url(admin_url('admin.php?page=fobo-data-maker-forms-settings')) . '">' . esc_html__('FOBO Data Maker Forms → Settings', 'fobo-data-maker-forms') . '</a>'
                                    );
                                    ?>
                                </p>
                            <?php else: ?>
                                <p class="description"><?php esc_html_e('Server verifies each token with Cloudflare before sealing the submission. Honeypot + rate limit still apply.', 'fobo-data-maker-forms'); ?></p>
                            <?php endif; ?>
                        </td>
                    </tr>
                </table>

                <h2 style="margin-top:32px;"><?php esc_html_e('Integrations', 'fobo-data-maker-forms'); ?></h2>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><?php esc_html_e('Webhook URL', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <input type="url" name="webhook_url"
                                   class="regular-text"
                                   style="width:100%;max-width:560px;"
                                   value="<?php echo esc_attr($webhook_url); ?>"
                                   placeholder="https://hooks.example.com/dm-submit" />
                            <p class="description"><?php
                                printf(
                                    /* translators: %s = action hook name */
                                    esc_html__('POSTed with submission metadata (no plaintext field values) after a successful sealed submit. For full notification flows use the WP action hook %s — works with Post SMTP, Mailpoet, WPForms add-ons, Zapier, custom code.', 'fobo-data-maker-forms'),
                                    '<code>fobo_data_maker_forms_submission_received</code>'
                                );
                            ?></p>
                        </td>
                    </tr>
                </table>

                <h2 style="margin-top:32px;"><?php esc_html_e('Form-wide messages', 'fobo-data-maker-forms'); ?></h2>
                <p class="description"><?php esc_html_e('Form-level text the renderer shows, independent of any single field. Empty box = use the form\'s default (set in the designer) or, failing that, the engine\'s English fallback.', 'fobo-data-maker-forms'); ?></p>

                <?php
                $form_msg_overrides   = $msg_overrides['__form'] ?? [];
                $schema_form_messages = is_array($form['messages'] ?? null) ? $form['messages'] : [];
                ?>
                <table class="form-table" role="presentation">
                    <tbody>
                        <?php foreach (MessageCatalog::form_slots() as $slot):
                            $placeholder = isset($schema_form_messages[$slot['id']]) && is_string($schema_form_messages[$slot['id']]) && $schema_form_messages[$slot['id']] !== ''
                                ? (string)$schema_form_messages[$slot['id']]
                                : (string)$slot['default'];
                            $value = isset($form_msg_overrides[$slot['id']]) ? (string)$form_msg_overrides[$slot['id']] : '';
                        ?>
                            <tr>
                                <th scope="row"><?php echo esc_html($slot['label']); ?></th>
                                <td>
                                    <input type="text"
                                           class="regular-text"
                                           style="width:100%;max-width:560px;"
                                           name="msg[__form][<?php echo esc_attr($slot['id']); ?>]"
                                           value="<?php echo esc_attr($value); ?>"
                                           placeholder="<?php echo esc_attr($placeholder); ?>" />
                                </td>
                            </tr>
                        <?php endforeach; ?>
                    </tbody>
                </table>

                <h2 style="margin-top:32px;"><?php esc_html_e('Field error messages', 'fobo-data-maker-forms'); ?></h2>
                <p class="description"><?php esc_html_e('Override the validation error text shown next to each field. Empty box = use the form\'s default (set in the designer) or, failing that, the engine\'s English fallback. Both are shown as placeholder text inside each box.', 'fobo-data-maker-forms'); ?></p>

                <?php
                $fields_for_msgs = is_array($form['fields'] ?? null) ? $form['fields'] : [];
                $any_slot = false;
                foreach ($fields_for_msgs as $fld) {
                    if (MessageCatalog::slots_for($fld)) { $any_slot = true; break; }
                }
                ?>

                <?php if (!$any_slot): ?>
                    <p class="description"><em><?php esc_html_e('No fields in this form expose customizable message slots. Toggle Required, set field options (min/max length, allowed extensions, etc.) in the designer to enable per-check overrides.', 'fobo-data-maker-forms'); ?></em></p>
                <?php else: ?>
                    <table class="form-table" role="presentation">
                        <tbody>
                            <?php foreach ($fields_for_msgs as $fld):
                                $slots = MessageCatalog::slots_for($fld);
                                if (!$slots) continue;
                                $fname  = (string)($fld['name']  ?? '');
                                $flabel = (string)($fld['label'] ?? $fname);
                                if ($fname === '') continue;
                                $schemaMsgs = is_array($fld['messages'] ?? null) ? $fld['messages'] : [];
                                $fOverrides = $msg_overrides[$fname] ?? [];
                            ?>
                                <tr>
                                    <th scope="row" style="vertical-align:top;">
                                        <?php echo esc_html($flabel); ?>
                                        <p style="font-weight:normal;color:#666;font-size:11px;margin:4px 0 0;"><code><?php echo esc_html($fname); ?></code></p>
                                    </th>
                                    <td>
                                        <?php foreach ($slots as $slot):
                                            // Placeholder cascade: schema-set message wins (form author's intent),
                                            // engine default otherwise. Site override goes into the input value.
                                            $placeholder = isset($schemaMsgs[$slot['id']]) && is_string($schemaMsgs[$slot['id']]) && $schemaMsgs[$slot['id']] !== ''
                                                ? (string)$schemaMsgs[$slot['id']]
                                                : (string)$slot['default'];
                                            $value = isset($fOverrides[$slot['id']]) ? (string)$fOverrides[$slot['id']] : '';
                                        ?>
                                            <div style="margin-bottom:10px;">
                                                <label style="display:block;font-size:12px;color:#444;margin-bottom:2px;">
                                                    <?php echo esc_html($slot['label']); ?>
                                                </label>
                                                <input type="text"
                                                       class="regular-text"
                                                       style="width:100%;max-width:560px;"
                                                       name="msg[<?php echo esc_attr($fname); ?>][<?php echo esc_attr($slot['id']); ?>]"
                                                       value="<?php echo esc_attr($value); ?>"
                                                       placeholder="<?php echo esc_attr($placeholder); ?>" />
                                            </div>
                                        <?php endforeach; ?>
                                    </td>
                                </tr>
                            <?php endforeach; ?>
                        </tbody>
                    </table>
                <?php endif; ?>

                <?php submit_button(__('Save form settings', 'fobo-data-maker-forms')); ?>
            </form>

            <?php
            // Checkbox-invert + master-toggle behaviour, delivered through the
            // enqueue pipeline (src-less handle carrying inline JS) instead of a
            // hand-written <script> tag.
            wp_register_script('fobo-data-maker-forms-form-settings', false, [], FOBO_DATA_MAKER_FORMS_VERSION, true);
            wp_enqueue_script('fobo-data-maker-forms-form-settings');
            wp_add_inline_script('fobo-data-maker-forms-form-settings', <<<'DMJS'
            // The visible-checkbox UX has the user CHECK to show. We persist
            // the INVERSE (hidden ids) so new elements added to the form
            // definition later default to visible. Flip every data-invert
            // checkbox right before the form posts. Document-level capture
            // listener so this works regardless of how the script is loaded
            // (inline / deferred / re-parented by WP admin chrome). We
            // identify our form by its nonce field — unique to this page.
            (function () {
              document.addEventListener('submit', function (e) {
                const form = e.target;
                if (!form || form.tagName !== 'FORM') return;
                if (!form.querySelector('input[name="dm_form_settings_nonce"]')) return;
                form.querySelectorAll('input[type="checkbox"][data-invert="1"]').forEach(function (cb) {
                  if (cb.disabled) return;     // required-field locks
                  cb.checked = !cb.checked;    // visible → don't post; hidden → post
                });
              }, true);

              // Master "all" toggle in the visibility table header. Clicking
              // it mirrors its state onto every non-required row checkbox.
              // Toggling individual rows after also keeps the header in sync
              // with the all-checked / none-checked / mixed states.
              document.addEventListener('change', function (e) {
                const t = e.target;
                if (!t) return;
                const form = t.closest && t.closest('form');
                if (!form || !form.querySelector('input[name="dm_form_settings_nonce"]')) return;
                const all = form.querySelector('#dm-vis-all');
                if (!all) return;
                const rows = form.querySelectorAll('input[type="checkbox"][data-invert="1"]:not([disabled])');
                if (t === all) {
                  rows.forEach(function (cb) { cb.checked = all.checked; });
                  return;
                }
                if (t.matches('input[type="checkbox"][data-invert="1"]')) {
                  let on = 0, off = 0;
                  rows.forEach(function (cb) { cb.checked ? on++ : off++; });
                  all.checked       = off === 0;
                  all.indeterminate = on > 0 && off > 0;
                }
              });

              // Initial header sync on page load — reflects the persisted state.
              document.addEventListener('DOMContentLoaded', function () {
                const form = document.querySelector('form input[name="dm_form_settings_nonce"]');
                if (!form) return;
                const f = form.closest('form');
                const all = f.querySelector('#dm-vis-all');
                const rows = f.querySelectorAll('input[type="checkbox"][data-invert="1"]:not([disabled])');
                if (!all) return;
                let on = 0, off = 0;
                rows.forEach(function (cb) { cb.checked ? on++ : off++; });
                all.checked       = rows.length > 0 && off === 0;
                all.indeterminate = on > 0 && off > 0;
              });
            })();
DMJS);
            ?>
        </div>
        <?php
    }

    /**
     * Walk the form schema, returning a flat list of every uniquely-id'd
     * layout column (heading, richtext, image, divider, spacer, button,
     * group, field column) plus standalone fields. Returned items carry
     * { id, label, kind, where } for the table render.
     */
    private static function enumerate(array $form): array
    {
        $out  = [];
        $field_by_id = [];
        foreach (($form['fields'] ?? []) as $f) {
            if (!empty($f['id'])) $field_by_id[(string)$f['id']] = $f;
        }
        foreach (($form['steps'] ?? []) as $stepIdx => $step) {
            /* translators: %d = step number */
            $stepLabel = $step['title'] ?? sprintf(__('Step %d', 'fobo-data-maker-forms'), $stepIdx + 1);
            foreach (($step['sections'] ?? []) as $secIdx => $section) {
                /* translators: %d = section number */
                $secLabel = $section['title'] ?? sprintf(__('Section %d', 'fobo-data-maker-forms'), $secIdx + 1);
                foreach (($section['rows'] ?? []) as $rowIdx => $row) {
                    foreach (($row['columns'] ?? []) as $col) {
                        self::collect_column($col, $field_by_id, "{$stepLabel} → {$secLabel}", $out);
                    }
                }
            }
        }
        return $out;
    }

    /**
     * A field is treated as required — and therefore NOT hideable — when its
     * boolean `required` flag is set OR its validation list carries ANY
     * RequiredRule, conditional (`when` gate) or not.
     *
     * Conditional required rules count too: a requirement-expression field is
     * still author-declared mandatory data, just situational. Hiding it would
     * silently drop data the publisher marked mandatory under some condition.
     * (Earlier this gated on `empty($r['when'])` — unconditional only — which
     * left conditionally-required fields hideable; brought in line with the
     * desktop hosted-forms HideableElements::IsFieldRequired.)
     */
    private static function is_field_required(array $field): bool
    {
        if (!empty($field['required'])) return true;
        $rules = $field['validation'] ?? [];
        if (!is_array($rules)) return false;
        foreach ($rules as $r) {
            if (!is_array($r)) continue;
            $kind = strtolower((string)($r['$kind'] ?? $r['kind'] ?? ''));
            if ($kind === 'required') return true;
        }
        return false;
    }

    /** Flat list of every required field's id in the form. Used to scrub
     *  required ids from the POSTed hidden array as defence-in-depth. */
    private static function required_field_ids(array $form): array
    {
        $out = [];
        foreach (($form['fields'] ?? []) as $f) {
            if (!is_array($f) || empty($f['id'])) continue;
            if (self::is_field_required($f)) $out[] = (string)$f['id'];
        }
        return $out;
    }

    private static function collect_column(array $col, array $field_by_id, string $where, array &$out): void
    {
        $kind = strtolower((string)($col['kind'] ?? $col['Kind'] ?? ''));
        $id   = (string)($col['id'] ?? '');
        switch ($kind) {
            case 'field':
                $fid   = (string)($col['fieldId'] ?? '');
                $field = $field_by_id[$fid] ?? null;
                if ($field) {
                    $out[] = [
                        'id'       => $fid,                                   // hide-by-field-id; BundleBuilder strips referencing columns
                        'label'    => (string)($field['label'] ?? $field['name'] ?? $fid),
                        'kind'     => 'field — ' . (string)($field['kind'] ?? '?'),
                        'where'    => $where,
                        'required' => self::is_field_required($field),
                    ];
                }
                break;
            case 'group':
                $groupLabel = (string)($col['title'] ?? '') !== '' ? (string)$col['title'] : __('(group)', 'fobo-data-maker-forms');
                if ($id) {
                    $out[] = ['id' => $id, 'label' => $groupLabel, 'kind' => 'group', 'where' => $where];
                }
                $whereInner = $where . ' → ' . $groupLabel;
                foreach (($col['rows'] ?? []) as $r) {
                    foreach (($r['columns'] ?? []) as $c) {
                        self::collect_column($c, $field_by_id, $whereInner, $out);
                    }
                }
                break;
            case 'heading':
                if ($id) $out[] = ['id' => $id, 'label' => (string)($col['text'] ?? '') !== '' ? (string)$col['text'] : __('(heading)', 'fobo-data-maker-forms'), 'kind' => 'heading', 'where' => $where];
                break;
            case 'richtext':
                if ($id) {
                    $md = (string)($col['markdown'] ?? '');
                    $preview = strlen($md) > 60 ? substr($md, 0, 57) . '…' : ($md ?: __('(rich text)', 'fobo-data-maker-forms'));
                    $out[] = ['id' => $id, 'label' => $preview, 'kind' => 'rich text', 'where' => $where];
                }
                break;
            case 'image':
                if ($id) $out[] = ['id' => $id, 'label' => (string)($col['altText'] ?? $col['source'] ?? '') !== '' ? (string)($col['altText'] ?? $col['source']) : __('(image)', 'fobo-data-maker-forms'), 'kind' => 'image', 'where' => $where];
                break;
            case 'divider':
                if ($id) $out[] = ['id' => $id, 'label' => __('(divider)', 'fobo-data-maker-forms'), 'kind' => 'divider', 'where' => $where];
                break;
            case 'spacer':
                if ($id) $out[] = ['id' => $id, 'label' => __('(spacer)', 'fobo-data-maker-forms'), 'kind' => 'spacer', 'where' => $where];
                break;
            case 'button':
                if ($id) $out[] = ['id' => $id, 'label' => (string)($col['label'] ?? $col['name'] ?? '') !== '' ? (string)($col['label'] ?? $col['name']) : __('(button)', 'fobo-data-maker-forms'), 'kind' => 'button — ' . (string)($col['action'] ?? 'None'), 'where' => $where];
                break;
        }
    }
}
