<?php
namespace DataMaker\Forms\Renderer\WordPress;

if (!defined('ABSPATH')) exit;

/**
 * Registers `[datamaker_form id="slug"]` and the asset bundle (renderer.js,
 * fn.js, styles.css, wp-bridge.js). The shortcode resolves the slug to a
 * stored form row, emits a mount div + inline JSON bundle, and the bridge
 * script wires the renderer's submit/edit hooks to the WP REST proxy.
 */
final class Shortcode
{
    public static function register(): void
    {
        add_shortcode('datamaker_form', [self::class, 'render']);
    }

    public static function register_assets(): void
    {
        $useMin = !(defined('WP_DEBUG') && WP_DEBUG);

        // Resolve the relative URL for an asset, preferring the minified
        // copy at assets/dist/*.min.* when present and we're not in
        // WP_DEBUG mode. Falls back to the unminified source in /assets/
        // so a fresh checkout (no `make minify` run) still works.
        $asset = static function (string $file) use ($useMin): string {
            if ($useMin) {
                $minRel = 'assets/dist/' . preg_replace('/\.(js|css)$/', '.min.$1', $file);
                if (file_exists(DM_RENDERER_DIR . $minRel)) return $minRel;
            }
            return 'assets/' . $file;
        };

        // File-mtime version so any asset edit busts the browser cache
        // without bumping DM_RENDERER_VERSION. WP_DEBUG → mtime; otherwise
        // pin to plugin version so production caches don't churn on every
        // deploy that touches an asset file.
        $ver = static function (string $relPath): string {
            $path = DM_RENDERER_DIR . $relPath;
            return (defined('WP_DEBUG') && WP_DEBUG && file_exists($path))
                ? (string)filemtime($path)
                : DM_RENDERER_VERSION;
        };

        $layoutRel = $asset('layout.css');
        $stylesRel = $asset('styles.css');
        $fnRel     = $asset('fn.js');
        $bridgeRel = $asset('wp-bridge.js');
        $coreRel   = $asset('renderer.js');

        wp_register_style(
            'datamaker-renderer-layout',
            DM_RENDERER_URL . $layoutRel,
            [],
            $ver($layoutRel)
        );
        wp_register_style(
            'datamaker-renderer-styles',
            DM_RENDERER_URL . $stylesRel,
            ['datamaker-renderer-layout'],
            $ver($stylesRel)
        );
        wp_register_script(
            'datamaker-renderer-fn',
            DM_RENDERER_URL . $fnRel,
            [],
            $ver($fnRel),
            ['in_footer' => false, 'strategy' => 'defer']
        );
        // Browser sealed-box (tweetnacl + sealedbox) — E2E-encrypts blob bytes
        // before the direct-to-S3 PUT (#45). Pre-built + committed at
        // assets/vendor/ (built via `make vendor-crypto`); exposes the global
        // window.DataMakerBlobCrypto the bridge reads. Hard dependency of the
        // bridge so it's defined before any upload runs.
        $blobCryptoRel = 'assets/vendor/dm-blob-crypto.min.js';
        wp_register_script(
            'datamaker-renderer-blobcrypto',
            DM_RENDERER_URL . $blobCryptoRel,
            [],
            $ver($blobCryptoRel),
            ['in_footer' => false, 'strategy' => 'defer']
        );
        wp_register_script(
            'datamaker-renderer-bridge',
            DM_RENDERER_URL . $bridgeRel,
            ['datamaker-renderer-fn', 'datamaker-renderer-blobcrypto'],
            $ver($bridgeRel),
            ['in_footer' => false, 'strategy' => 'defer']
        );
        wp_register_script(
            'datamaker-renderer-core',
            DM_RENDERER_URL . $coreRel,
            ['datamaker-renderer-bridge'],
            $ver($coreRel),
            ['in_footer' => true, 'strategy' => 'defer']
        );
        // Cloudflare Turnstile API. Defer + async per Cloudflare docs;
        // the widget mounts via explicit class="cf-turnstile" so we don't
        // need a JS callback wire-up. Only enqueued by render() when a
        // form actually requires Turnstile (saves a request for plain forms).
        wp_register_script(
            'datamaker-renderer-turnstile',
            'https://challenges.cloudflare.com/turnstile/v0/api.js',
            [],
            null,
            ['in_footer' => false, 'strategy' => 'defer']
        );
    }

    public static function render($atts): string
    {
        $atts = shortcode_atts(
            ['id' => '', 'theme' => ''],
            $atts,
            'datamaker_form'
        );
        $slug = sanitize_title($atts['id']);
        if (!$slug) {
            return '<!-- datamaker_form: id required -->';
        }

        $row = FormStore::find_by_slug($slug);
        if (!$row) {
            return '<!-- datamaker_form: form not found: ' . esc_html($slug) . ' -->';
        }

        $payload = BundleBuilder::build_payload($row);

        // Theme cascade: shortcode/block 'theme' attr (on/off/empty=inherit)
        // wins over the per-form `use_theme` column. Per-form column NULL
        // means "no opinion" → fall back to true (themed) since most authors
        // who upload a styled .dmf want their styling to show up.
        $override = strtolower((string)$atts['theme']);
        if      ($override === 'on'  || $override === '1' || $override === 'true')  { $use_theme_brand = true; }
        else if ($override === 'off' || $override === '0' || $override === 'false') { $use_theme_brand = false; }
        else if ($row['use_theme'] !== null && $row['use_theme'] !== '')           { $use_theme_brand = (bool)(int)$row['use_theme']; }
        else                                                                        { $use_theme_brand = true; }

        $settings  = Admin\SettingsPage::get();
        $api_base  = (string)$settings['sync_lambda_url'];
        $rest_base = esc_url_raw(rest_url('datamaker/v1'));

        // Per-form edit-flow: default OFF. Storing localStorage on a public
        // form is the kind of thing the publisher should opt into per form
        // (privacy + storage hygiene), not silently get out of the box.
        // NULL column = no opinion = off; explicit toggle via Form Settings.
        $edit_flow_on = $row['edit_flow'] === null || $row['edit_flow'] === ''
            ? false
            : (bool)(int)$row['edit_flow'];

        // Resolve the per-form after-submit target. '' = stay on page;
        // any non-empty value gets handed to wp-bridge as a redirect URL.
        $after_submit_url = FormStore::resolve_after_submit_url($row['after_submit'] ?? '');
        // Per-form success Markdown shown in stay-on-page mode. Default
        // injected at resolve so admins who never visit Form Settings
        // still get a polite confirmation.
        $success_message  = FormStore::resolve_success_message($row['success_message'] ?? '');

        $mount_id  = 'dm-mount-' . wp_generate_uuid4();
        $bundle_id = 'dm-bundle-' . wp_generate_uuid4();

        // layout.css ships unconditionally — it's the structural baseline
        // (grid, row/col, field, sheet container, spacer, divider, date
        // picker open/closed). styles.css is the brand layer (FOBO palette,
        // button variants, heading sizes, FA glyphs, dark mode); only ship
        // it when the host wants the form's DataMaker theme. wp-bridge also
        // zeros out the palette CSS-vars + per-element brand overrides
        // baked into the bundle when theme is off.
        wp_enqueue_style('datamaker-renderer-layout');
        if ($use_theme_brand) {
            wp_enqueue_style('datamaker-renderer-styles');
        }
        wp_enqueue_script('datamaker-renderer-fn');
        wp_enqueue_script('datamaker-renderer-bridge');
        wp_enqueue_script('datamaker-renderer-core');

        // Translatable user-visible strings the JS reads via t(). Emit
        // before the bridge script so both bridge + renderer see them.
        // Stored on `window.DataMakerRenderer.i18n` directly (not a
        // separate global) because that's the namespace the renderer
        // hooks already live on. Translations come from active .mo
        // files via __() at runtime.
        $i18n_json = wp_json_encode(self::i18n_strings(),
            JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE
            | JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT);
        wp_add_inline_script(
            'datamaker-renderer-bridge',
            '(function(){var R=(window.DataMakerRenderer=window.DataMakerRenderer||{});R.i18n=' . $i18n_json . ';})();',
            'before'
        );

        // Per-form anti-abuse / consent surfaced as data attrs so
        // wp-bridge.js can gate submission client-side. Server-side
        // enforcement (SubmitProxy) is the real gate; client-side is UX.
        $honeypot_on    = !empty($row['honeypot_on']);
        $consent_on     = !empty($row['consent_required']);
        // Turnstile: per-form toggle + plugin-wide site key. Both must
        // be set or the widget is suppressed (matches server-side gating
        // in SubmitProxy::dispatch). Surface as data attrs so wp-bridge
        // can gate the submit on a present token.
        $turnstile_on   = !empty($row['turnstile_on'])
                       && !empty($settings['turnstile_site_key'])
                       && !empty($settings['turnstile_secret_key']);
        $turnstile_key  = $turnstile_on ? (string)$settings['turnstile_site_key'] : '';
        if ($turnstile_on) {
            wp_enqueue_script('datamaker-renderer-turnstile');
        }
        $consent_label  = (string)($row['consent_label'] ?? '');
        // privacy_url is stored as either a fully-qualified URL or
        // 'page:N' (selected from the WP page dropdown). Resolve to an
        // absolute permalink for the front-end link.
        $privacy_url       = FormStore::resolve_privacy_url($row['privacy_url'] ?? '');
        $privacy_link_text = trim((string)($row['privacy_link_text'] ?? ''));
        if ($privacy_link_text === '') {
            $privacy_link_text = __('privacy policy', 'datamaker-renderer');
        }

        // The bridge reads these as data-* on the mount div.
        $mount_attrs = sprintf(
            'id="%s" class="dm-form-mount" data-form-id="%s" data-form-slug="%s" data-bundle="%s" '
            . 'data-rest-base="%s" data-api-base="%s" data-recipient-user-id="%s" data-recipient-pubkey="%s" '
            . 'data-edit-flow="%s" data-use-form-theme="%s" data-after-submit-url="%s" '
            . 'data-success-message-md="%s" data-honeypot="%s" data-consent-required="%s" '
            . 'data-turnstile="%s"',
            esc_attr($mount_id),
            esc_attr($row['form_id']),
            esc_attr($row['slug']),
            esc_attr($bundle_id),
            esc_attr($rest_base),
            esc_attr($api_base),
            esc_attr((string)$row['recipient_user_id']),
            esc_attr((string)$row['recipient_pubkey']),
            $edit_flow_on    ? '1' : '0',
            $use_theme_brand ? '1' : '0',
            esc_attr($after_submit_url),
            esc_attr($success_message),
            $honeypot_on    ? '1' : '0',
            $consent_on     ? '1' : '0',
            $turnstile_on   ? '1' : '0'
        );

        // Hex-escape <, >, &, ', " inside the inline JSON so a string
        // value from the .dmf cannot break out of the <script type=
        // "application/json"> container regardless of context (HTML
        // comment, attribute boundary, conditional comment).
        $bundle_json = wp_json_encode(
            $payload,
            JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE
            | JSON_HEX_TAG | JSON_HEX_AMP | JSON_HEX_APOS | JSON_HEX_QUOT
        );

        // Designer-selected fonts as data: URIs, baked into the .dmf as
        // `fonts.css`. Only emit when the form is being rendered with its
        // designer styling — without it the WP theme drives the look so
        // shipping ~MBs of font payload would be wasted bandwidth.
        $fonts_inline = '';
        if ($use_theme_brand && !empty($row['fonts_css'])) {
            $fonts_inline = '<style id="dm-fonts-' . esc_attr($mount_id) . '">'
                          . self::sanitize_fonts_css((string)$row['fonts_css'])
                          . '</style>';
        }

        // Honeypot — hidden text input named exactly `dm_hp_email`. Bots
        // that auto-fill every input on the page tick it; real submitters
        // never see it (visually hidden + aria-hidden). wp-bridge reads
        // the value and submits it as `hp`; SubmitProxy rejects when set.
        $honeypot_html = '';
        if ($honeypot_on) {
            $honeypot_html =
                '<div class="dm-hp" aria-hidden="true" tabindex="-1"'
                . ' style="position:absolute !important;left:-9999px;width:1px;height:1px;overflow:hidden;">'
                . '<label>Leave this field empty<input type="text" name="dm_hp_email"'
                . ' class="dm-hp-input" autocomplete="off" tabindex="-1" /></label>'
                . '</div>';
        }

        // GDPR consent block — rendered just above the submit row so it
        // anchors near the action. wp-bridge gates submit on the checkbox
        // being ticked; SubmitProxy verifies the consent flag on the
        // wire too (defence in depth).
        $consent_html = '';
        if ($consent_on) {
            $label_template = $consent_label !== '' ? $consent_label : __('I agree to the privacy policy.', 'datamaker-renderer');
            $linked_label   = esc_html($privacy_link_text);
            $linked_anchor  = $privacy_url !== ''
                ? '<a href="' . esc_url($privacy_url) . '" target="_blank" rel="noopener noreferrer">' . $linked_label . '</a>'
                : '';

            if ($privacy_url !== '' && strpos($label_template, '{privacy}') !== false) {
                // Author used the placeholder — drop the anchor in place.
                // esc_html the surrounding text, leave the anchor as the only HTML.
                $label_html = str_replace('{privacy}', $linked_anchor, esc_html($label_template));
            } else {
                $label_html = esc_html($label_template);
                // No placeholder + URL set → append the link inline so the
                // submitter can still reach the policy without forcing the
                // author to learn the {privacy} convention.
                if ($privacy_url !== '') {
                    $label_html .= ' <span class="dm-consent-link">(' . $linked_anchor . ')</span>';
                }
            }
            $consent_html =
                '<div class="dm-consent">'
                . '<label class="dm-consent-label">'
                . '<input type="checkbox" class="dm-consent-checkbox" /> '
                . '<span>' . $label_html . '</span>'
                . '</label>'
                . '</div>';
        }

        // Turnstile widget — `cf-turnstile` class is what the Cloudflare
        // API bootstrap auto-mounts on page load. The hidden response
        // input is populated by the widget; wp-bridge.js reads it at
        // submit time. Placed right above the (auto or schema) submit
        // row by virtue of being the last child of the mount div before
        // the form root.
        $turnstile_html = '';
        if ($turnstile_on) {
            $turnstile_html =
                '<div class="dm-turnstile">'
                . '<div class="cf-turnstile" data-sitekey="' . esc_attr($turnstile_key) . '"'
                . ' data-theme="auto" data-size="flexible"></div>'
                . '</div>';
        }

        return sprintf(
            '%s<div %s><script type="application/json" id="%s">%s</script>%s%s%s<div class="dm-sheet" data-dm-form-root></div></div>',
            $fonts_inline,
            $mount_attrs,
            esc_attr($bundle_id),
            $bundle_json,
            $honeypot_html,
            $consent_html,
            $turnstile_html
        );
    }

    /**
     * Strip `@import` rules and non-`data:` `url(...)` references from a
     * `fonts.css` blob before inlining inside <style>. Designer-baked
     * font CSS only ever carries `data:` URIs (web-safe + signed-bundle
     * portable), so a remote `url()` is a sign the .dmf is hostile and
     * trying to use the visitor's browser to leak CSS-detected state
     * to an attacker-controlled host. Also defends against `</style`
     * tag-break-out by neutralising the closing-tag sequence.
     */
    private static function sanitize_fonts_css(string $css): string
    {
        $css = preg_replace('/@import\s+[^;]+;?/i', '', $css) ?? $css;
        $css = preg_replace_callback(
            '/url\(\s*([\'"]?)([^\'")]+)\1\s*\)/i',
            static function (array $m): string {
                $url = trim($m[2]);
                return stripos($url, 'data:') === 0 ? $m[0] : 'url()';
            },
            $css
        ) ?? $css;
        return str_replace('</', '<\\/', $css);
    }

    /**
     * Active set of localizable strings consumed by renderer.js +
     * wp-bridge.js. Keys are stable identifiers; values are the English
     * source strings wrapped in __() so .mo files override them at the
     * active locale. Hosts can extend / replace via the
     * `dm_renderer_i18n` filter (e.g. to add tenant-specific copy).
     */
    private static function i18n_strings(): array
    {
        return apply_filters('dm_renderer_i18n', [
            // Renderer.js
            'submit'                    => __('Submit',                                                       'datamaker-renderer'),
            'preview'                   => __('Preview',                                                      'datamaker-renderer'),
            'edit'                      => __('Edit',                                                         'datamaker-renderer'),
            'no_items'                  => __('No items',                                                     'datamaker-renderer'),
            'add_and_press_enter'       => __('Add and press Enter',                                          'datamaker-renderer'),
            'click_to_upload'           => __('Click to upload',                                              'datamaker-renderer'),
            'no_file_selected'          => __('No file selected',                                             'datamaker-renderer'),
            'browse'                    => __('Browse…',                                                      'datamaker-renderer'),
            'clear'                     => __('Clear',                                                        'datamaker-renderer'),
            'please_fix_highlighted'    => __('Please fix the highlighted fields.',                           'datamaker-renderer'),
            'validation_banner_default' => __('Please fix the highlighted fields before submitting.',         'datamaker-renderer'),
            // wp-bridge.js
            'consent_required_hint'     => __('Please tick the consent box to submit.',                       'datamaker-renderer'),
            'captcha_required_hint'     => __('Please complete the challenge to submit.',                     'datamaker-renderer'),
            'submitted_redirecting'     => __('Submitted. Redirecting…',                                      'datamaker-renderer'),
            'continue_editing'          => __('Continue editing',                                             'datamaker-renderer'),
            'start_over'                => __('Start over',                                                   'datamaker-renderer'),
            'resume_prompt'             => __('You started this form earlier on this browser. Continue editing your previous submission?', 'datamaker-renderer'),
            'err_too_large'             => __('This submission is too large to send. Try shrinking large images or removing big attachments, then submit again.', 'datamaker-renderer'),
            'err_network'               => __('Network error — please check your connection and try submitting again.', 'datamaker-renderer'),
            'err_form_gone'             => __('This form is no longer available. Please contact the form owner.', 'datamaker-renderer'),
            'err_not_accepting'         => __('This form is not accepting submissions right now.',            'datamaker-renderer'),
            'err_server_unreachable'    => __('The form server is unreachable right now. Please try again in a moment.', 'datamaker-renderer'),
            'err_generic_5xx'           => __('Something went wrong sending your submission. Please try again — if it keeps happening, contact the form owner.', 'datamaker-renderer'),
            'err_generic'               => __('Submission failed',                                            'datamaker-renderer'),
        ]);
    }
}
