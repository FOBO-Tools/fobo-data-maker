<?php
namespace Fobo\DataMakerForms\Admin;

if (!defined('ABSPATH')) exit;

/**
 * WP admin settings — Data Maker API URL, theme switch, edit-flow toggle,
 * optional .dmf signature verification. Stored under the single
 * `fobo_data_maker_forms_settings` option.
 */
final class SettingsPage
{
    private const OPTION = 'fobo_data_maker_forms_settings';

    public static function register_settings(): void
    {
        register_setting('fobo_data_maker_forms', self::OPTION, [
            'type'              => 'array',
            'sanitize_callback' => [self::class, 'sanitize'],
            'default'           => self::defaults(),
        ]);
    }

    public static function defaults(): array
    {
        // Default ON: an unsigned .dmf can specify any recipient pubkey
        // and silently re-route every sealed submission to an attacker.
        // Existing installs keep whatever they previously saved; only new
        // installs flip to safe-by-default.
        return [
            'verify_signature'       => true,
            'expected_signer_pubkey' => '',
            // Cloudflare Turnstile. Plugin-wide keys so admins paste once,
            // then enable per-form via Form Settings. Empty key = effectively
            // disabled even if a form's `turnstile_on` flag is set.
            'turnstile_site_key'     => '',
            'turnstile_secret_key'   => '',
        ];
    }

    public static function get(): array
    {
        $opt = get_option(self::OPTION, []);
        $merged = array_merge(self::defaults(), is_array($opt) ? $opt : []);
        // Data Maker API URL resolves through the plugin helper so the
        // `fobo_data_maker_forms_sync_api_url` filter + wp-config override are
        // honored for every consumer.
        $merged['sync_lambda_url'] = function_exists('fobo_data_maker_forms_sync_api_url')
            ? \fobo_data_maker_forms_sync_api_url()
            : (defined('FOBO_DATA_MAKER_FORMS_SYNC_API_URL') ? FOBO_DATA_MAKER_FORMS_SYNC_API_URL : '');
        return $merged;
    }

    public static function sanitize($input): array
    {
        $out = self::defaults();
        if (!is_array($input)) return $out;
        $out['verify_signature']       = !empty($input['verify_signature']);
        $out['expected_signer_pubkey'] = sanitize_text_field((string)($input['expected_signer_pubkey'] ?? ''));
        // Turnstile site key is rendered into the page (data-sitekey) and
        // the secret key is sent over HTTPS to challenges.cloudflare.com.
        // Both are short alphanumeric tokens — sanitize_text_field is enough.
        $out['turnstile_site_key']     = sanitize_text_field((string)($input['turnstile_site_key']     ?? ''));
        $out['turnstile_secret_key']   = sanitize_text_field((string)($input['turnstile_secret_key']   ?? ''));
        return $out;
    }

    public static function render(): void
    {
        if (!current_user_can('manage_options')) return;
        $s = self::get();
        ?>
        <div class="wrap">
            <?php PageHeader::render(__('FOBO Data Maker Forms — Settings', 'fobo-data-maker-forms')); ?>
            <form method="post" action="options.php">
                <?php settings_fields('fobo_data_maker_forms'); ?>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><?php esc_html_e('Signature verification', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <label>
                                <input type="checkbox" name="fobo_data_maker_forms_settings[verify_signature]" value="1" <?php checked($s['verify_signature']); ?>>
                                <?php esc_html_e('Require uploaded .dmf bundles to be Ed25519-signed', 'fobo-data-maker-forms'); ?>
                            </label>
                            <br>
                            <input type="text" class="regular-text" name="fobo_data_maker_forms_settings[expected_signer_pubkey]"
                                value="<?php echo esc_attr($s['expected_signer_pubkey']); ?>" placeholder="<?php esc_attr_e('base64-encoded signer pubkey (optional)', 'fobo-data-maker-forms'); ?>">
                            <p class="description"><?php esc_html_e('If set, the uploaded .dmf must be signed with exactly this pubkey. Leave blank to accept any signed bundle.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                </table>

                <h2 style="margin-top:32px;"><?php esc_html_e('Cloudflare Turnstile', 'fobo-data-maker-forms'); ?></h2>
                <p class="description"><?php
                    printf(
                        /* translators: %s = link to cloudflare.com/turnstile */
                        esc_html__('Privacy-friendly CAPTCHA challenge. Enroll for free at %s; paste the site & secret keys here. Each form chooses whether to require it via Form Settings.', 'fobo-data-maker-forms'),
                        '<a href="https://www.cloudflare.com/products/turnstile/" target="_blank" rel="noopener noreferrer">cloudflare.com/turnstile</a>'
                    );
                ?></p>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row"><?php esc_html_e('Site key', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <input type="text" class="regular-text" name="fobo_data_maker_forms_settings[turnstile_site_key]"
                                value="<?php echo esc_attr($s['turnstile_site_key']); ?>" placeholder="0x4AAAAAAA…" autocomplete="off">
                            <p class="description"><?php esc_html_e('Public key embedded in the form page (data-sitekey).', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row"><?php esc_html_e('Secret key', 'fobo-data-maker-forms'); ?></th>
                        <td>
                            <input type="password" class="regular-text" name="fobo_data_maker_forms_settings[turnstile_secret_key]"
                                value="<?php echo esc_attr($s['turnstile_secret_key']); ?>" placeholder="0x4AAAAAAA…" autocomplete="off">
                            <p class="description"><?php esc_html_e('Used server-side to verify tokens with Cloudflare. Never sent to the browser.', 'fobo-data-maker-forms'); ?></p>
                        </td>
                    </tr>
                </table>
                <?php submit_button(); ?>
            </form>
        </div>
        <?php
    }
}
