<?php
namespace Fobo\DataMakerForms\Admin;

if (!defined('ABSPATH')) exit;

/**
 * Shared chrome for the plugin's admin pages — small FOBO mark next to
 * the page title. Kept tiny on purpose so it reads as the brand owner
 * rather than the WordPress page title itself.
 */
final class PageHeader
{
    public static function render(string $title): void
    {
        $logo = FOBO_DATA_MAKER_FORMS_URL . 'assets/fobo_logo.svg';
        ?>
        <h1 style="display:flex;align-items:center;gap:14px;margin:12px 0 20px">
            <img src="<?php echo esc_url($logo); ?>" alt="FOBO" height="44" style="height:44px;width:auto;display:block">
            <span><?php echo esc_html($title); ?></span>
        </h1>
        <?php
    }
}
