<?php
namespace DataMaker\Forms\Renderer\WordPress\Admin;

use DataMaker\Forms\Renderer\WordPress\DmfReader;
use DataMaker\Forms\Renderer\WordPress\FormStore;

if (!defined('ABSPATH')) exit;

/**
 * Top-level admin menu — Upload, Forms list, Settings. Upload posts a .dmf
 * file + a chosen slug; SUCCESS persists the parsed form into wp_dm_forms
 * and surfaces the resulting shortcode.
 */
final class UploadPage
{
    public static function register_menu(): void
    {
        add_menu_page(
            __('Data Maker Forms', 'datamaker-renderer'),
            __('Data Maker Forms', 'datamaker-renderer'),
            'manage_options',
            'datamaker-renderer',
            [self::class, 'render'],
            'dashicons-feedback',
            58
        );
        add_submenu_page('datamaker-renderer', __('Upload .dmf', 'datamaker-renderer'), __('Upload .dmf', 'datamaker-renderer'), 'manage_options', 'datamaker-renderer',          [self::class, 'render']);
        add_submenu_page('datamaker-renderer', __('Forms', 'datamaker-renderer'),       __('Forms', 'datamaker-renderer'),       'manage_options', 'datamaker-renderer-forms',    [FormsListPage::class, 'render']);
        add_submenu_page('datamaker-renderer', __('Settings', 'datamaker-renderer'),    __('Settings', 'datamaker-renderer'),    'manage_options', 'datamaker-renderer-settings', [SettingsPage::class, 'render']);
    }

    public static function render(): void
    {
        if (!\dm_renderer_user_can_manage()) return;

        $notice = null;
        if (!empty($_POST['dm_upload_nonce']) && wp_verify_nonce(sanitize_text_field(wp_unslash($_POST['dm_upload_nonce'])), 'dm_upload')) {
            $notice = self::handle_upload();
        }
        $settings = SettingsPage::get();
        ?>
        <div class="wrap">
            <?php PageHeader::render(__('Data Maker Forms — Upload .dmf', 'datamaker-renderer')); ?>
            <?php if ($notice): ?>
                <div class="notice notice-<?php echo esc_attr($notice['kind']); ?>"><p><?php echo wp_kses_post($notice['html']); ?></p></div>
            <?php endif; ?>

            <form method="post" enctype="multipart/form-data">
                <?php wp_nonce_field('dm_upload', 'dm_upload_nonce'); ?>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><label for="dm-slug"><?php esc_html_e('Shortcode slug', 'datamaker-renderer'); ?></label></th>
                        <td>
                            <input id="dm-slug" type="text" class="regular-text" name="slug" required placeholder="customer-intake">
                            <p class="description"><?php
                                printf(
                                    /* translators: %s = example shortcode */
                                    esc_html__('Used in the shortcode: %s. Re-uploading the same slug overwrites the form in place.', 'datamaker-renderer'),
                                    '<code>[datamaker_form id="customer-intake"]</code>'
                                );
                            ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><label for="dm-file"><?php esc_html_e('.dmf bundle', 'datamaker-renderer'); ?></label></th>
                        <td>
                            <input id="dm-file" type="file" name="dmf_file" accept=".dmf,application/vnd.datamaker.form" required>
                            <p class="description">
                                <?php
                                printf(
                                    /* translators: %s = ON or OFF */
                                    esc_html__('Signature verification is currently %s — change under Settings.', 'datamaker-renderer'),
                                    '<strong>' . ($settings['verify_signature'] ? esc_html__('ON', 'datamaker-renderer') : esc_html__('OFF', 'datamaker-renderer')) . '</strong>'
                                );
                                ?>
                            </p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Designer styling', 'datamaker-renderer'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="use_theme" value="1" checked>
                                <?php esc_html_e('Apply Theme/Styling', 'datamaker-renderer'); ?>
                            </label>
                            <p class="description"><?php esc_html_e('On = render the form the way it looks in the desktop designer (palette, fonts, button variants, heading styles, per-element overrides). Off = strip all of that and let the active WordPress theme drive the look. Per-form; you can flip it later under Forms → Settings.', 'datamaker-renderer'); ?></p>
                        </td>
                    </tr>
                </table>
                <?php submit_button(__('Upload form', 'datamaker-renderer')); ?>
            </form>
        </div>
        <?php
    }

    /**
     * Hard cap on .dmf uploads (bytes). Keeps the admin from OOM-ing
     * the PHP process on a maliciously-large file before DmfReader's
     * own entry-level caps kick in. Filterable for hosts shipping
     * legitimately large theme/font payloads.
     */
    private const MAX_DMF_BYTES_DEFAULT = 16 * 1024 * 1024;

    private static function handle_upload(): array
    {
        if (empty($_FILES['dmf_file']['tmp_name']) || !is_uploaded_file($_FILES['dmf_file']['tmp_name'])) {
            return ['kind' => 'error', 'html' => esc_html__('No file uploaded.', 'datamaker-renderer')];
        }
        $slug = isset($_POST['slug']) ? sanitize_title(wp_unslash($_POST['slug'])) : '';
        if (!$slug) {
            return ['kind' => 'error', 'html' => esc_html__('Slug is required.', 'datamaker-renderer')];
        }

        $max_bytes = (int)apply_filters('dm_renderer_max_dmf_bytes', self::MAX_DMF_BYTES_DEFAULT);
        $file_size = (int)($_FILES['dmf_file']['size'] ?? 0);
        if ($max_bytes > 0 && $file_size > $max_bytes) {
            return [
                'kind' => 'error',
                'html' => sprintf(
                    /* translators: %s = human-readable size */
                    esc_html__('Uploaded .dmf is larger than the %s limit.', 'datamaker-renderer'),
                    esc_html(size_format($max_bytes))
                ),
            ];
        }

        $bytes = file_get_contents($_FILES['dmf_file']['tmp_name']);
        if ($bytes === false) {
            return ['kind' => 'error', 'html' => esc_html__('Could not read the uploaded file.', 'datamaker-renderer')];
        }
        $settings = SettingsPage::get();
        try {
            $bundle = DmfReader::read(
                $bytes,
                !empty($settings['verify_signature']),
                $settings['expected_signer_pubkey'] ?: null
            );
        } catch (\Throwable $e) {
            // Generic message — exception text can leak internal file
            // paths from the ZipArchive layer. The full reason is logged
            // server-side for the admin to inspect via error_log.
            error_log('[datamaker-renderer] .dmf parse failed: ' . $e->getMessage());
            return ['kind' => 'error', 'html' => esc_html__('Could not parse the .dmf bundle (signature, format, or signing-key mismatch).', 'datamaker-renderer')];
        }

        // Reject share-only .dmf bundles. Without a recipient block in
        // the manifest the WP plugin has no pubkey to seal submissions
        // against and no userId to route them to — accepting the upload
        // just sets up a confusing "submit fails on every click" UX. Tell
        // the publisher to re-export while signed in to FOBO.
        $recipient = $bundle['recipient'] ?? null;
        $hasRecipient = is_array($recipient)
            && !empty($recipient['userId'])
            && !empty($recipient['publicKey']);
        if (!$hasRecipient) {
            return [
                'kind' => 'error',
                'html' => esc_html__('This .dmf was exported in share-only mode (no recipient block). Submissions can\'t route back to a publisher, so the plugin won\'t accept it. Sign in to FOBO in the Data Maker desktop app, re-export the form, and try again.', 'datamaker-renderer'),
            ];
        }

        $use_theme = !empty($_POST['use_theme']);
        $id = FormStore::upsert($bundle, $slug, get_current_user_id(), $use_theme);
        $shortcode = '[datamaker_form id="' . esc_attr($slug) . '"]';
        return [
            'kind' => 'success',
            'html' => sprintf(
                /* translators: 1: numeric form id, 2: shortcode */
                esc_html__('Form saved (id #%1$d). Embed it with: %2$s', 'datamaker-renderer'),
                $id,
                '<code>' . esc_html($shortcode) . '</code>'
            ),
        ];
    }
}
