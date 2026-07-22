<?php
namespace Fobo\DataMakerForms\Admin;

use Fobo\DataMakerForms\FormStore;

if (!defined('ABSPATH')) exit;

final class FormsListPage
{
    public static function render(): void
    {
        if (!\fobo_data_maker_forms_user_can_manage()) return;

        if (!empty($_POST['dm_delete_nonce']) && !empty($_POST['form_id'])) {
            $delete_id = (int)$_POST['form_id'];
            // Row-scoped nonce — `dm_delete_{id}` — so a leaked token
            // for one row can't be replayed against another. A page-
            // global nonce would let an attacker capture the field via
            // CSRF on one row and use it to delete every form in the
            // 12-24h nonce TTL window.
            if (wp_verify_nonce(
                    sanitize_text_field(wp_unslash($_POST['dm_delete_nonce'])),
                    'dm_delete_' . $delete_id)) {
                FormStore::delete($delete_id);
                echo '<div class="notice notice-success"><p>' . esc_html__('Form deleted.', 'fobo-data-maker-forms') . '</p></div>';
            } else {
                wp_die(esc_html__('Forbidden — nonce check failed.', 'fobo-data-maker-forms'), 403);
            }
        }

        $rows = FormStore::list_all();
        ?>
        <div class="wrap">
            <?php PageHeader::render(__('FOBO Data Maker Forms', 'fobo-data-maker-forms')); ?>
            <p><a href="<?php echo esc_url(admin_url('admin.php?page=fobo-data-maker-forms')); ?>" class="button button-primary"><?php esc_html_e('Upload .dmf', 'fobo-data-maker-forms'); ?></a></p>
            <table class="widefat fixed striped">
                <thead>
                    <tr>
                        <th><?php esc_html_e('Slug (shortcode id)', 'fobo-data-maker-forms'); ?></th>
                        <th><?php esc_html_e('Form id', 'fobo-data-maker-forms'); ?></th>
                        <th><?php esc_html_e('Schema', 'fobo-data-maker-forms'); ?></th>
                        <th><?php esc_html_e('Signed', 'fobo-data-maker-forms'); ?></th>
                        <th><?php esc_html_e('Uploaded', 'fobo-data-maker-forms'); ?></th>
                        <th></th>
                    </tr>
                </thead>
                <tbody>
                <?php if (!$rows): ?>
                    <tr><td colspan="6"><?php esc_html_e('No forms uploaded yet.', 'fobo-data-maker-forms'); ?></td></tr>
                <?php else: foreach ($rows as $row):
                    $settings_url = admin_url('admin.php?page=fobo-data-maker-forms-form&form_id=' . (int)$row['id']);
                    $preview_url  = admin_url('admin.php?page=fobo-data-maker-forms-preview&form_id=' . (int)$row['id']);
                ?>
                    <tr>
                        <td><code>[fobo_data_maker_form id="<?php echo esc_html($row['slug']); ?>"]</code></td>
                        <td><?php echo esc_html($row['form_id']); ?></td>
                        <td>v<?php echo (int)$row['schema_version']; ?></td>
                        <td><?php echo $row['signature_verified'] ? esc_html__('yes', 'fobo-data-maker-forms') : esc_html__('no', 'fobo-data-maker-forms'); ?></td>
                        <td><?php echo esc_html($row['uploaded_at']); ?></td>
                        <td>
                            <a href="<?php echo esc_url($preview_url); ?>" class="button button-secondary"><?php esc_html_e('Preview', 'fobo-data-maker-forms'); ?></a>
                            <a href="<?php echo esc_url($settings_url); ?>" class="button button-secondary"><?php esc_html_e('Settings', 'fobo-data-maker-forms'); ?></a>
                            <form method="post" onsubmit="return confirm('<?php echo esc_js(__('Delete this form?', 'fobo-data-maker-forms')); ?>');" style="display:inline">
                                <?php wp_nonce_field('dm_delete_' . (int)$row['id'], 'dm_delete_nonce'); ?>
                                <input type="hidden" name="form_id" value="<?php echo (int)$row['id']; ?>">
                                <button class="button button-secondary" type="submit"><?php esc_html_e('Delete', 'fobo-data-maker-forms'); ?></button>
                            </form>
                        </td>
                    </tr>
                <?php endforeach; endif; ?>
                </tbody>
            </table>
        </div>
        <?php
    }
}
