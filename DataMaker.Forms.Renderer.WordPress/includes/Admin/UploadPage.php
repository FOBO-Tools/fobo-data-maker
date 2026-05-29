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
            'Data Maker Forms',
            'Data Maker Forms',
            'manage_options',
            'datamaker-renderer',
            [self::class, 'render'],
            'dashicons-feedback',
            58
        );
        add_submenu_page('datamaker-renderer', 'Upload .dmf',  'Upload .dmf', 'manage_options', 'datamaker-renderer',          [self::class, 'render']);
        add_submenu_page('datamaker-renderer', 'Forms',        'Forms',       'manage_options', 'datamaker-renderer-forms',    [FormsListPage::class, 'render']);
        add_submenu_page('datamaker-renderer', 'Settings',     'Settings',    'manage_options', 'datamaker-renderer-settings', [SettingsPage::class, 'render']);
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
            <?php PageHeader::render('Data Maker Forms — Upload .dmf'); ?>
            <?php if ($notice): ?>
                <div class="notice notice-<?php echo esc_attr($notice['kind']); ?>"><p><?php echo wp_kses_post($notice['html']); ?></p></div>
            <?php endif; ?>

            <form method="post" enctype="multipart/form-data">
                <?php wp_nonce_field('dm_upload', 'dm_upload_nonce'); ?>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><label for="dm-slug">Shortcode slug</label></th>
                        <td>
                            <input id="dm-slug" type="text" class="regular-text" name="slug" required placeholder="customer-intake">
                            <p class="description">Used in the shortcode: <code>[datamaker_form id="customer-intake"]</code>. Re-uploading the same slug overwrites the form in place.</p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><label for="dm-file">.dmf bundle</label></th>
                        <td>
                            <input id="dm-file" type="file" name="dmf_file" accept=".dmf,application/vnd.datamaker.form" required>
                            <p class="description">
                                Signature verification is currently
                                <strong><?php echo $settings['verify_signature'] ? 'ON' : 'OFF'; ?></strong>
                                — change under Settings.
                            </p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row">Designer styling</th>
                        <td>
                            <label>
                                <input type="checkbox" name="use_theme" value="1" checked>
                                Apply Theme/Styling
                            </label>
                            <p class="description">On = render the form the way it looks in the desktop designer (palette, fonts, button variants, heading styles, per-element overrides). Off = strip all of that and let the active WordPress theme drive the look. Per-form; you can flip it later under Forms → Settings.</p>
                        </td>
                    </tr>
                </table>
                <?php submit_button('Upload form'); ?>
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
                'html' => 'This .dmf was exported in share-only mode (no recipient block). Submissions can\'t route back to a publisher, so the plugin won\'t accept it. Sign in to FOBO in the Data Maker desktop app, re-export the form, and try again.',
            ];
        }

        $use_theme = !empty($_POST['use_theme']);
        $id = FormStore::upsert($bundle, $slug, get_current_user_id(), $use_theme);
        $shortcode = '[datamaker_form id="' . esc_attr($slug) . '"]';
        return [
            'kind' => 'success',
            'html' => sprintf('Form saved (id #%d). Embed it with: <code>%s</code>', $id, esc_html($shortcode)),
        ];
    }
}
