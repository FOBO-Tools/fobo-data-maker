<?php
namespace Fobo\DataMakerForms;

if (!defined('ABSPATH')) exit;

/**
 * Registers `[fobo_data_maker_form id="slug"]` and the asset bundle (renderer.js,
 * fn.js, styles.css, wp-bridge.js). The shortcode resolves the slug to a
 * stored form row, emits a mount div + inline JSON bundle, and the bridge
 * script wires the renderer's submit/edit hooks to the WP REST proxy.
 */
final class Shortcode
{
    public static function register(): void
    {
        add_shortcode('fobo_data_maker_form', [self::class, 'render']);
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
                if (file_exists(FOBO_DATA_MAKER_FORMS_DIR . $minRel)) return $minRel;
            }
            return 'assets/' . $file;
        };

        // File-mtime version so any asset edit busts the browser cache
        // without bumping FOBO_DATA_MAKER_FORMS_VERSION. WP_DEBUG → mtime; otherwise
        // pin to plugin version so production caches don't churn on every
        // deploy that touches an asset file.
        $ver = static function (string $relPath): string {
            $path = FOBO_DATA_MAKER_FORMS_DIR . $relPath;
            return (defined('WP_DEBUG') && WP_DEBUG && file_exists($path))
                ? (string)filemtime($path)
                : FOBO_DATA_MAKER_FORMS_VERSION;
        };

        $layoutRel = $asset('layout.css');
        $stylesRel = $asset('styles.css');
        $fnRel     = $asset('fn.js');
        $bridgeRel = $asset('wp-bridge.js');
        $coreRel   = $asset('renderer.js');

        wp_register_style(
            'fobo-data-maker-forms-layout',
            FOBO_DATA_MAKER_FORMS_URL . $layoutRel,
            [],
            $ver($layoutRel)
        );
        wp_register_style(
            'fobo-data-maker-forms-styles',
            FOBO_DATA_MAKER_FORMS_URL . $stylesRel,
            ['fobo-data-maker-forms-layout'],
            $ver($stylesRel)
        );
        wp_register_script(
            'fobo-data-maker-forms-fn',
            FOBO_DATA_MAKER_FORMS_URL . $fnRel,
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
            'fobo-data-maker-forms-blobcrypto',
            FOBO_DATA_MAKER_FORMS_URL . $blobCryptoRel,
            [],
            $ver($blobCryptoRel),
            ['in_footer' => false, 'strategy' => 'defer']
        );
        wp_register_script(
            'fobo-data-maker-forms-bridge',
            FOBO_DATA_MAKER_FORMS_URL . $bridgeRel,
            ['fobo-data-maker-forms-fn', 'fobo-data-maker-forms-blobcrypto'],
            $ver($bridgeRel),
            ['in_footer' => false, 'strategy' => 'defer']
        );
        wp_register_script(
            'fobo-data-maker-forms-core',
            FOBO_DATA_MAKER_FORMS_URL . $coreRel,
            ['fobo-data-maker-forms-bridge'],
            $ver($coreRel),
            ['in_footer' => true, 'strategy' => 'defer']
        );
        // Cloudflare Turnstile API. Defer + async per Cloudflare docs;
        // the widget mounts via explicit class="cf-turnstile" so we don't
        // need a JS callback wire-up. Only enqueued by render() when a
        // form actually requires Turnstile (saves a request for plain forms).
        // The script MUST load from Cloudflare's origin — it's a remote CAPTCHA
        // service that can't be bundled — so it's exempt from the bundle-locally
        // rule. Version is null on purpose: Cloudflare versions the endpoint
        // itself (/v0/) and rejects an appended ?ver= query.
        // phpcs:disable PluginCheck.CodeAnalysis.EnqueuedResourceOffloading.OffloadedContent, WordPress.WP.EnqueuedResourceParameters.MissingVersion -- third-party CAPTCHA service; must load from challenges.cloudflare.com, versioned by Cloudflare (an appended ?ver= is rejected).
        wp_register_script(
            'fobo-data-maker-forms-turnstile',
            'https://challenges.cloudflare.com/turnstile/v0/api.js',
            [],
            null,
            ['in_footer' => false, 'strategy' => 'defer']
        );
        // phpcs:enable PluginCheck.CodeAnalysis.EnqueuedResourceOffloading.OffloadedContent, WordPress.WP.EnqueuedResourceParameters.MissingVersion
    }

    public static function render($atts): string
    {
        $atts = shortcode_atts(
            ['id' => '', 'theme' => ''],
            $atts,
            'fobo_data_maker_form'
        );
        $slug = sanitize_title($atts['id']);
        if (!$slug) {
            return '<!-- fobo_data_maker_form: id required -->';
        }

        $row = FormStore::find_by_slug($slug);
        if (!$row) {
            return '<!-- fobo_data_maker_form: form not found: ' . esc_html($slug) . ' -->';
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
        $rest_base = esc_url_raw(rest_url('fobo-data-maker/v1'));

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
        wp_enqueue_style('fobo-data-maker-forms-layout');
        if ($use_theme_brand) {
            wp_enqueue_style('fobo-data-maker-forms-styles');
            // Designer-selected fonts as data: URIs (baked into the .dmf as
            // fonts.css), carried on their own src-less handle — the
            // WordPress-sanctioned carrier for inline CSS. Registered and
            // enqueued here, at render time, so the inline attaches before
            // the handle is printed. Attaching to the brand stylesheet
            // instead silently drops it: that handle has already printed by
            // the time a shortcode runs. Repeat forms on one page append to
            // the same handle. Only shipped with the DataMaker theme —
            // otherwise the WP theme drives the look.
            if (!empty($row['fonts_css'])) {
                wp_register_style(
                    'fobo-data-maker-forms-fonts',
                    false,
                    ['fobo-data-maker-forms-styles'],
                    FOBO_DATA_MAKER_FORMS_VERSION
                );
                wp_enqueue_style('fobo-data-maker-forms-fonts');
                wp_add_inline_style(
                    'fobo-data-maker-forms-fonts',
                    self::sanitize_fonts_css((string)$row['fonts_css'])
                );
            }
        }
        wp_enqueue_script('fobo-data-maker-forms-fn');
        wp_enqueue_script('fobo-data-maker-forms-bridge');
        wp_enqueue_script('fobo-data-maker-forms-core');

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
            'fobo-data-maker-forms-bridge',
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
            wp_enqueue_script('fobo-data-maker-forms-turnstile');
        }
        $consent_label  = (string)($row['consent_label'] ?? '');
        // privacy_url is stored as either a fully-qualified URL or
        // 'page:N' (selected from the WP page dropdown). Resolve to an
        // absolute permalink for the front-end link.
        $privacy_url       = FormStore::resolve_privacy_url($row['privacy_url'] ?? '');
        $privacy_link_text = trim((string)($row['privacy_link_text'] ?? ''));
        if ($privacy_link_text === '') {
            $privacy_link_text = __('privacy policy', 'fobo-data-maker-forms');
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
            $label_template = $consent_label !== '' ? $consent_label : __('I agree to the privacy policy.', 'fobo-data-maker-forms');
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
            '<div %s><script type="application/json" id="%s">%s</script>%s%s%s<div class="dm-sheet" data-dm-form-root></div></div>',
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
     * `fobo_data_maker_forms_i18n` filter (e.g. to add tenant-specific copy).
     */
    private static function i18n_strings(): array
    {
        return apply_filters('fobo_data_maker_forms_i18n', [
            // Renderer.js
            'submit'                    => __('Submit',                                                       'fobo-data-maker-forms'),
            'step_back'                 => __('Back',                                                         'fobo-data-maker-forms'),
            'step_next'                 => __('Next',                                                         'fobo-data-maker-forms'),
            'please_fix_step'           => __('Please complete the required fields on this step.',            'fobo-data-maker-forms'),
            'preview'                   => __('Preview',                                                      'fobo-data-maker-forms'),
            'edit'                      => __('Edit',                                                         'fobo-data-maker-forms'),
            'no_items'                  => __('No items',                                                     'fobo-data-maker-forms'),
            'add_and_press_enter'       => __('Add and press Enter',                                          'fobo-data-maker-forms'),
            'click_to_upload'           => __('Click to upload',                                              'fobo-data-maker-forms'),
            'no_file_selected'          => __('No file selected',                                             'fobo-data-maker-forms'),
            'browse'                    => __('Browse…',                                                      'fobo-data-maker-forms'),
            'clear'                     => __('Clear',                                                        'fobo-data-maker-forms'),
            'uploading'                 => __('Uploading…',                                                   'fobo-data-maker-forms'),
            'upload_failed'             => __('Upload failed — try again',                                    'fobo-data-maker-forms'),
            'still_uploading'           => __('Still uploading attachments — try again in a moment.',         'fobo-data-maker-forms'),
            'geo_address_placeholder'   => __('Type an address…',                                             'fobo-data-maker-forms'),
            // Signature / initials pad
            'sign_here'                 => __('Sign here',                                                    'fobo-data-maker-forms'),
            'initials_here'             => __('Initials',                                                     'fobo-data-maker-forms'),
            'printed_name'              => __('Printed name',                                                 'fobo-data-maker-forms'),
            'signed'                    => __('Signed',                                                       'fobo-data-maker-forms'),
            'clear_signature'           => __('Clear signature',                                              'fobo-data-maker-forms'),
            'please_fix_highlighted'    => __('Please fix the highlighted fields.',                           'fobo-data-maker-forms'),
            'validation_banner_default' => __('Please fix the highlighted fields before submitting.',         'fobo-data-maker-forms'),
            // wp-bridge.js
            'consent_required_hint'     => __('Please tick the consent box to submit.',                       'fobo-data-maker-forms'),
            'captcha_required_hint'     => __('Please complete the challenge to submit.',                     'fobo-data-maker-forms'),
            'submitted_redirecting'     => __('Submitted. Redirecting…',                                      'fobo-data-maker-forms'),
            'continue_editing'          => __('Continue editing',                                             'fobo-data-maker-forms'),
            'start_over'                => __('Start over',                                                   'fobo-data-maker-forms'),
            'resume_prompt'             => __('You started this form earlier on this browser. Continue editing your previous submission?', 'fobo-data-maker-forms'),
            'err_too_large'             => __('This submission is too large to send. Try shrinking large images or removing big attachments, then submit again.', 'fobo-data-maker-forms'),
            'err_network'               => __('Network error — please check your connection and try submitting again.', 'fobo-data-maker-forms'),
            'err_form_gone'             => __('This form is no longer available. Please contact the form owner.', 'fobo-data-maker-forms'),
            'err_not_accepting'         => __('This form is not accepting submissions right now.',            'fobo-data-maker-forms'),
            'err_server_unreachable'    => __('The form server is unreachable right now. Please try again in a moment.', 'fobo-data-maker-forms'),
            'err_generic_5xx'           => __('Something went wrong sending your submission. Please try again — if it keeps happening, contact the form owner.', 'fobo-data-maker-forms'),
            'err_generic'               => __('Submission failed',                                            'fobo-data-maker-forms'),
        ]);
    }
}
