<?php
namespace DataMaker\Forms\Renderer\WordPress\Admin;

if (!defined('ABSPATH')) exit;

/**
 * WP admin settings — Data Maker API URL, theme switch, edit-flow toggle,
 * optional .dmf signature verification. Stored under the single
 * `datamaker_renderer_settings` option.
 */
final class SettingsPage
{
    private const OPTION = 'datamaker_renderer_settings';

    public static function register_settings(): void
    {
        register_setting('datamaker_renderer', self::OPTION, [
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
        // `dm_renderer_sync_api_url` filter + wp-config override are
        // honored for every consumer.
        $merged['sync_lambda_url'] = function_exists('dm_renderer_sync_api_url')
            ? \dm_renderer_sync_api_url()
            : (defined('DM_RENDERER_SYNC_API_URL') ? DM_RENDERER_SYNC_API_URL : '');
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
            <?php PageHeader::render('Data Maker Forms — Settings'); ?>
            <form method="post" action="options.php">
                <?php settings_fields('datamaker_renderer'); ?>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row">Signature verification</th>
                        <td>
                            <label>
                                <input type="checkbox" name="datamaker_renderer_settings[verify_signature]" value="1" <?php checked($s['verify_signature']); ?>>
                                Require uploaded .dmf bundles to be Ed25519-signed
                            </label>
                            <br>
                            <input type="text" class="regular-text" name="datamaker_renderer_settings[expected_signer_pubkey]"
                                value="<?php echo esc_attr($s['expected_signer_pubkey']); ?>" placeholder="base64-encoded signer pubkey (optional)">
                            <p class="description">If set, the uploaded .dmf must be signed with exactly this pubkey. Leave blank to accept any signed bundle.</p>
                        </td>
                    </tr>
                </table>

                <h2 style="margin-top:32px;">Cloudflare Turnstile</h2>
                <p class="description">Privacy-friendly CAPTCHA challenge. Enroll for free at <a href="https://www.cloudflare.com/products/turnstile/" target="_blank" rel="noopener noreferrer">cloudflare.com/turnstile</a>; paste the site &amp; secret keys here. Each form chooses whether to require it via Form Settings.</p>
                <table class="form-table" role="presentation">
                    <tr>
                        <th scope="row">Site key</th>
                        <td>
                            <input type="text" class="regular-text" name="datamaker_renderer_settings[turnstile_site_key]"
                                value="<?php echo esc_attr($s['turnstile_site_key']); ?>" placeholder="0x4AAAAAAA…" autocomplete="off">
                            <p class="description">Public key embedded in the form page (data-sitekey).</p>
                        </td>
                    </tr>
                    <tr>
                        <th scope="row">Secret key</th>
                        <td>
                            <input type="password" class="regular-text" name="datamaker_renderer_settings[turnstile_secret_key]"
                                value="<?php echo esc_attr($s['turnstile_secret_key']); ?>" placeholder="0x4AAAAAAA…" autocomplete="off">
                            <p class="description">Used server-side to verify tokens with Cloudflare. Never sent to the browser.</p>
                        </td>
                    </tr>
                </table>
                <?php submit_button(); ?>
            </form>
        </div>
        <?php
    }
}
