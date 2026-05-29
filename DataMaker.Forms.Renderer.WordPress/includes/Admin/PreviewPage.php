<?php
namespace DataMaker\Forms\Renderer\WordPress\Admin;

use DataMaker\Forms\Renderer\WordPress\FormStore;

if (!defined('ABSPATH')) exit;

/**
 * Admin form preview. WP admin loads global CSS (form-table inputs,
 * .button, .description, body backgrounds) that bleed into shortcode
 * output and visibly distort native controls — date-time pickers,
 * selects, buttons. Embedding the shortcode in an iframe pointing at a
 * frontend preview endpoint isolates the form from admin CSS entirely;
 * what you see in the iframe is what frontend visitors see.
 *
 * Public endpoint URL: `{home_url}/?dm_renderer_preview={slug}&_wpnonce=…`.
 * Nonce is action-scoped to the slug; verification accepts either an
 * admin-managed user OR a valid nonce (so admins clicking from the
 * Forms list don't need to re-auth in the iframe context).
 */
final class PreviewPage
{
    public static function register_menu(): void
    {
        add_submenu_page(
            'datamaker-renderer',
            __('Preview form', 'datamaker-renderer'),
            null,
            'manage_options',
            'datamaker-renderer-preview',
            [self::class, 'render']
        );
    }

    /** Register the frontend `?dm_renderer_preview=…` interception. Wired by the main plugin file on `template_redirect`. */
    public static function maybe_serve_frontend_preview(): void
    {
        if (!isset($_GET['dm_renderer_preview'])) return;
        $slug = sanitize_title((string)$_GET['dm_renderer_preview']);
        if (!$slug) return;

        // Preview is admin-only. The earlier "share a link" path used a
        // 24h nonce in the URL, which let anyone with the link view the
        // unpublished form for the full nonce TTL. Until we have a
        // proper short-lived signed share token (with revocation +
        // explicit publisher consent), the iframe is the only consumer
        // and the admin is always logged in.
        if (!\dm_renderer_user_can_manage()) {
            status_header(403);
            exit('forbidden');
        }

        nocache_headers();
        // Anti-clickjack: allow same-origin embedding so the admin iframe
        // works but block third-party framing.
        header('X-Frame-Options: SAMEORIGIN');

        // Suppress the WP admin bar in the iframe — logged-in admins get
        // it injected on every frontend request otherwise, and it serves
        // no purpose inside a preview surface.
        show_admin_bar(false);
        add_filter('show_admin_bar', '__return_false');
        remove_action('wp_head',   '_admin_bar_bump_cb');
        remove_action('wp_footer', 'wp_admin_bar_render', 1000);

        ?><!DOCTYPE html>
<html class="dm-preview" <?php language_attributes(); ?>>
<head>
    <meta charset="<?php bloginfo('charset'); ?>">
    <meta name="viewport" content="width=device-width, initial-scale=1">
    <title><?php echo esc_html(sprintf(__('Preview — %s', 'datamaker-renderer'), $slug)); ?></title>
    <style>
        html, body { margin: 0; padding: 0; background: #ffffff; }
        body { padding: 24px; box-sizing: border-box; min-height: 100vh; }
    </style>
    <?php wp_head(); ?>
</head>
<body>
<?php echo do_shortcode('[datamaker_form id="' . esc_attr($slug) . '"]'); ?>
<?php wp_footer(); ?>
</body>
</html><?php
        exit;
    }

    public static function render(): void
    {
        if (!\dm_renderer_user_can_manage()) return;

        $form_id = isset($_GET['form_id']) ? (int)$_GET['form_id'] : 0;
        if ($form_id <= 0) {
            echo '<div class="wrap"><h1>' . esc_html__('Form preview', 'datamaker-renderer') . '</h1><p>' . esc_html__('Missing or invalid form id.', 'datamaker-renderer') . '</p></div>';
            return;
        }

        global $wpdb;
        $row = $wpdb->get_row($wpdb->prepare("SELECT * FROM " . FormStore::table() . " WHERE id = %d", $form_id), ARRAY_A);
        if (!$row) {
            echo '<div class="wrap"><h1>' . esc_html__('Form preview', 'datamaker-renderer') . '</h1><p>' . esc_html__('Form not found.', 'datamaker-renderer') . '</p></div>';
            return;
        }

        $slug         = (string)$row['slug'];
        $back_url     = admin_url('admin.php?page=datamaker-renderer-forms');
        $settings_url = admin_url('admin.php?page=datamaker-renderer-form&form_id=' . $form_id);
        $shortcode    = sprintf('[datamaker_form id="%s"]', esc_attr($slug));

        // Admin-only preview: no shareable token needed. The iframe inherits
        // the admin's WP session cookies; `maybe_serve_frontend_preview`
        // gates on `dm_renderer_user_can_manage()`.
        $iframe_src   = add_query_arg([
            'dm_renderer_preview' => $slug,
        ], home_url('/'));
        ?>
        <div class="wrap">
            <?php PageHeader::render(__('Form preview — ', 'datamaker-renderer') . $slug); ?>
            <p>
                <a href="<?php echo esc_url($back_url); ?>">← <?php esc_html_e('Back to forms', 'datamaker-renderer'); ?></a>
                &nbsp;|&nbsp;
                <a href="<?php echo esc_url($settings_url); ?>"><?php esc_html_e('Form settings', 'datamaker-renderer'); ?></a>
                &nbsp;|&nbsp;
                <a href="<?php echo esc_url($iframe_src); ?>" target="_blank" rel="noopener"><?php esc_html_e('Open in new tab', 'datamaker-renderer'); ?></a>
            </p>
            <p class="description"><?php
                printf(
                    /* translators: %s = shortcode */
                    esc_html__('Shortcode: %s — paste this anywhere on the site to embed the form.', 'datamaker-renderer'),
                    '<code>' . esc_html($shortcode) . '</code>'
                );
            ?></p>
            <hr>
            <iframe
                src="<?php echo esc_url($iframe_src); ?>"
                style="width:100%;max-width:1200px;min-height:800px;border:1px solid #e0e0e0;background:#fff;"
                title="<?php esc_attr_e('Form preview', 'datamaker-renderer'); ?>"></iframe>
            <p class="description" style="margin-top:16px;"><?php esc_html_e('Preview submits go through the same pipeline as the live site (sealed POST → API). Use a test recipient inbox before sharing the form publicly.', 'datamaker-renderer'); ?></p>
        </div>
        <?php
    }
}
