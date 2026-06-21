<?php
namespace DataMaker\Forms\Renderer\WordPress;

if (!defined('ABSPATH')) exit;

/**
 * Server-side proxy that turns a plaintext submission from the browser into
 * a sealed SubmissionEnvelope and forwards it to the Data Maker API.
 * Lives in PHP because libsodium-PHP is the easiest place to seal against
 * the form's recipient pubkey without shipping the recipient secret to the
 * page.
 *
 * Wire shape — the bridge POSTs:
 *   { slug: string, values: { fieldName: any }, submission_id?: string, edit_token?: string }
 * which becomes
 *   POST /submissions or PUT /submissions/{id}
 * on the Lambda. The Lambda response (including any new edit_token from POST)
 * is forwarded back to the browser verbatim so wp-bridge.js can stash it in
 * localStorage.
 */
final class SubmitProxy
{
    public static function register_routes(): void
    {
        register_rest_route('datamaker/v1', '/submit', [
            'methods'             => 'POST',
            'callback'            => [self::class, 'handle_submit'],
            'permission_callback' => '__return_true',
        ]);
        register_rest_route('datamaker/v1', '/update', [
            'methods'             => 'POST',
            'callback'            => [self::class, 'handle_update'],
            'permission_callback' => '__return_true',
        ]);
        // Storage v2 — binary blobs upload-slot endpoint. The browser
        // POSTs { slug, hash, mime?, sizeBytes? }; we resolve the slug
        // → form.recipient_user_id and forward to the Lambda's
        // POST /submissions/upload-slot, which mints a pre-signed PUT
        // URL into the datamaker-submission-blobs bucket. The browser
        // uploads bytes directly to S3 — the WP plugin never handles
        // the binary payload, so PHP body limits don't apply.
        // See docs/PLAN-STORAGE-V2.md §#18a phase 4.
        register_rest_route('datamaker/v1', '/upload-slot', [
            'methods'             => 'POST',
            'callback'            => [self::class, 'handle_upload_slot'],
            'permission_callback' => '__return_true',
        ]);
    }

    public static function handle_submit(\WP_REST_Request $req): \WP_REST_Response
    {
        return self::dispatch($req, /*update*/ false);
    }

    public static function handle_update(\WP_REST_Request $req): \WP_REST_Response
    {
        return self::dispatch($req, /*update*/ true);
    }

    /**
     * Mint a pre-signed PUT URL for a binary submission blob. Browser
     * POSTs { slug, hash, mime?, sizeBytes? }; we resolve slug → form
     * → recipient_user_id and forward to the Lambda. Returns the same
     * { url, key, expiresAtUtc } shape the Lambda emits so wp-bridge.js
     * can hand it back to renderer.js's uploadSlot hook untouched.
     *
     * Rate-limit is shared with the submit endpoint per IP+form-slug —
     * a single submitter can't burn the bucket by spamming picks even
     * when each pick is small. Lambda's own size-cap + S3's 24h
     * pending/* lifecycle handle storage abuse downstream.
     */
    public static function handle_upload_slot(\WP_REST_Request $req): \WP_REST_Response
    {
        $params = $req->get_json_params();
        $slug   = isset($params['slug']) ? sanitize_title((string)$params['slug']) : '';
        $hash   = isset($params['hash']) ? (string)$params['hash'] : '';
        $mime   = isset($params['mime']) && is_string($params['mime']) ? $params['mime'] : null;
        $size   = isset($params['sizeBytes']) ? (int)$params['sizeBytes'] : null;

        if (!$slug || !$hash) {
            return new \WP_REST_Response(['error' => __('slug + hash required.', 'datamaker-renderer')], 400);
        }
        // Hash format guard — Lambda would reject too, but a fast
        // local check avoids a billable round-trip on bot traffic.
        if (!preg_match('/^[0-9a-f]{64}$/', $hash)) {
            return new \WP_REST_Response(['error' => __('hash must be 64-char lowercase hex SHA-256.', 'datamaker-renderer')], 400);
        }

        $row = FormStore::find_by_slug($slug);
        if (!$row) {
            return new \WP_REST_Response(['error' => __('form not found.', 'datamaker-renderer')], 404);
        }
        if (empty($row['recipient_user_id'])) {
            return new \WP_REST_Response(['error' => __('form has no recipient configured.', 'datamaker-renderer')], 400);
        }

        // Reuse the submit endpoint's per-IP per-form rate limiter so a
        // bot can't churn upload-slot tokens.
        $rateLimit = (int)($row['rate_limit_per_min'] ?? 0);
        if ($rateLimit <= 0) {
            $rateLimit = (int)apply_filters('dm_renderer_default_rate_limit_per_min', 60);
        }
        if ($rateLimit > 0) {
            $ip = self::client_ip();
            $window = (int)floor(time() / 60);
            $key = 'dm_rl_slot_' . $slug . '_' . md5($ip) . '_' . $window;
            $count = (int)get_transient($key);
            if ($count >= $rateLimit) {
                return new \WP_REST_Response(['error' => __('Too many uploads. Please wait a moment.', 'datamaker-renderer')], 429);
            }
            set_transient($key, $count + 1, 90);
        }

        $api_base = rtrim(\dm_renderer_sync_api_url(), '/');
        if (!$api_base || !self::url_is_safe_outbound($api_base)) {
            return new \WP_REST_Response(['error' => __('Data Maker API URL not configured.', 'datamaker-renderer')], 500);
        }

        $body = [
            'recipientUserId' => (string)$row['recipient_user_id'],
            'hash'            => $hash,
        ];
        if ($mime !== null && $mime !== '') $body['mime']      = $mime;
        if ($size !== null && $size > 0)    $body['sizeBytes'] = $size;

        $resp = wp_remote_post($api_base . '/submissions/upload-slot', [
            'headers' => ['Content-Type' => 'application/json'],
            'body'    => wp_json_encode($body),
            'timeout' => 10,
        ]);
        if (is_wp_error($resp)) {
            return new \WP_REST_Response(['error' => $resp->get_error_message()], 502);
        }

        $code = (int)wp_remote_retrieve_response_code($resp);
        $bodyOut = wp_remote_retrieve_body($resp);
        $decoded = $bodyOut ? (json_decode($bodyOut, true) ?: []) : [];
        return new \WP_REST_Response($decoded, $code ?: 502);
    }

    /**
     * Default maximum request body the public submit endpoint accepts.
     * Sized so the sealed + base64-wrapped envelope still fits inside
     * API Gateway's 10 MB sync-invoke payload limit (Lambda's downstream
     * S3 spillover at 320 KB only changes how the envelope is stored in
     * DynamoDB — the WP → Lambda hop still ships the full envelope
     * inline). 6 MB plaintext → ~8 MB envelope → fits with headroom.
     * Hosts can shrink (anti-DoS) or grow via the
     * `dm_renderer_max_submit_bytes` filter. Set to 0 to disable.
     */
    private const MAX_BODY_BYTES_DEFAULT = 6 * 1024 * 1024;

    private static function dispatch(\WP_REST_Request $req, bool $update): \WP_REST_Response
    {
        if (!function_exists('sodium_crypto_box_seal')) {
            return new \WP_REST_Response(['error' => __('libsodium PHP extension required.', 'datamaker-renderer')], 500);
        }

        $max_bytes = (int)apply_filters('dm_renderer_max_submit_bytes', self::MAX_BODY_BYTES_DEFAULT);
        $body_raw = $req->get_body();
        if (is_string($body_raw) && $max_bytes > 0 && strlen($body_raw) > $max_bytes) {
            return new \WP_REST_Response(['error' => __('Submission too large.', 'datamaker-renderer')], 413);
        }

        $params = $req->get_json_params();
        $slug   = isset($params['slug'])   ? sanitize_title((string)$params['slug']) : '';
        $values = isset($params['values']) && is_array($params['values']) ? $params['values'] : null;
        if (!$slug || $values === null) {
            return new \WP_REST_Response(['error' => __('slug + values required.', 'datamaker-renderer')], 400);
        }

        $row = FormStore::find_by_slug($slug);
        if (!$row) {
            return new \WP_REST_Response(['error' => __('form not found.', 'datamaker-renderer')], 404);
        }
        if (empty($row['recipient_user_id']) || empty($row['recipient_pubkey'])) {
            return new \WP_REST_Response(['error' => __('form has no recipient configured; submissions are not supported.', 'datamaker-renderer')], 400);
        }

        // ── Anti-abuse gate ──────────────────────────────────────────────
        // Honeypot: if the per-form toggle is on AND the hidden honeypot
        // field arrived with a non-empty value, drop with a 422. Real
        // submitters never see the field; bots auto-fill every input on
        // the page. Quiet rejection — don't leak which gate caught them.
        if (!empty($row['honeypot_on'])) {
            $hp = isset($params['hp']) ? trim((string)$params['hp']) : '';
            if ($hp !== '') {
                return new \WP_REST_Response(['error' => __('Submission rejected.', 'datamaker-renderer')], 422);
            }
        }

        // GDPR consent gate: when required, the bridge sends consent=true
        // alongside the values. Bail with a 422 if missing.
        if (!empty($row['consent_required']) && empty($params['consent'])) {
            return new \WP_REST_Response(['error' => __('Consent is required before submitting.', 'datamaker-renderer')], 422);
        }

        // Cloudflare Turnstile gate. Per-form `turnstile_on` flag + a
        // plugin-wide secret are both required; missing either disables
        // the check (mirrors the client-side Shortcode gating). Verified
        // with Cloudflare BEFORE we touch the sealed envelope path so a
        // failed challenge can't burn libsodium cycles or Lambda quota.
        if (!empty($row['turnstile_on'])) {
            $settings = Admin\SettingsPage::get();
            $secret   = (string)($settings['turnstile_secret_key'] ?? '');
            if ($secret !== '') {
                $token = isset($params['captcha_token']) ? (string)$params['captcha_token'] : '';
                if ($token === '' || !self::verify_turnstile($token, $secret, self::client_ip())) {
                    return new \WP_REST_Response(['error' => __('Challenge verification failed. Please try again.', 'datamaker-renderer')], 422);
                }
            }
        }

        // Rate limit per IP per form. Soft throttle via WP transients —
        // misses Lambda quota costs, doesn't require an external store.
        // Per-form value of 0 falls through to a plugin-wide default
        // (filter `dm_renderer_default_rate_limit_per_min`, default 60).
        // Set the filter to 0 to disable globally.
        $rateLimit = (int)($row['rate_limit_per_min'] ?? 0);
        if ($rateLimit <= 0) {
            $rateLimit = (int)apply_filters('dm_renderer_default_rate_limit_per_min', 60);
        }
        if ($rateLimit > 0) {
            $ip = self::client_ip();
            $window = (int)floor(time() / 60);    // bucket per minute
            $key = 'dm_rl_' . $slug . '_' . md5($ip) . '_' . $window;
            $count = (int)get_transient($key);
            if ($count >= $rateLimit) {
                return new \WP_REST_Response(['error' => __('Too many submissions. Please wait a moment.', 'datamaker-renderer')], 429);
            }
            set_transient($key, $count + 1, 90); // expire shortly after the bucket window
        }

        $api_base = rtrim(\dm_renderer_sync_api_url(), '/');
        if (!$api_base) {
            return new \WP_REST_Response(['error' => __('Data Maker API URL not configured.', 'datamaker-renderer')], 500);
        }
        if (!self::url_is_safe_outbound($api_base)) {
            return new \WP_REST_Response(['error' => __('Data Maker API URL is not allowed.', 'datamaker-renderer')], 500);
        }

        $form          = json_decode($row['form_json'], true) ?: [];
        $submission_id = $update
            ? (string)($params['submission_id'] ?? '')
            : wp_generate_uuid4();
        if ($update && !$submission_id) {
            return new \WP_REST_Response(['error' => 'submission_id required for update.'], 400);
        }

        // Storage v2 (#18a phase 5): submission ciphertext no longer
        // carries the full form definition. Receivers look up the
        // matching (FormId, FormVersion) in their local archive
        // (#18b), so we ship just the version pointer + values.
        // FormVersion is hard-coded to 1 until #18b adds real version
        // tracking on the desktop form-save path.
        $payload = [
            'values'       => $values,
            'submittedAt'  => gmdate('c'),
            'formVersion'  => 1,
            'mode'         => $update ? 'Update' : 'Create',
            'editToken'    => $update ? (string)($params['edit_token'] ?? '') : null,
        ];
        $payload_json = wp_json_encode($payload, JSON_UNESCAPED_SLASHES | JSON_UNESCAPED_UNICODE);

        try {
            $recipient_pubkey = self::b64_decode_strict((string)$row['recipient_pubkey']);
        } catch (\Throwable $e) {
            return new \WP_REST_Response(['error' => 'recipient pubkey is not valid base64.'], 500);
        }

        $ciphertext = sodium_crypto_box_seal($payload_json, $recipient_pubkey);

        // recipientPubkey is the routing DESTINATION (the box public key the form
        // is sealed to); the server fingerprints it to deliver to the right
        // database. recipientUserId is no longer part of the submission envelope.
        $envelope = [
            'submissionId'   => $submission_id,
            'formId'         => (string)$row['form_id'],
            'recipientPubkey' => (string)$row['recipient_pubkey'],
            'submitterId'    => null,
            'ciphertext'     => base64_encode($ciphertext),
        ];
        if ($update) {
            $envelope['editToken'] = (string)$params['edit_token'];
        }

        $url = $api_base . '/submissions' . ($update ? '/' . rawurlencode($submission_id) : '');
        $method = $update ? 'PUT' : 'POST';
        $resp = wp_remote_request($url, [
            'method'  => $method,
            'headers' => ['Content-Type' => 'application/json'],
            'body'    => wp_json_encode($envelope),
            'timeout' => 15,
        ]);
        if (is_wp_error($resp)) {
            return new \WP_REST_Response(['error' => $resp->get_error_message()], 502);
        }

        $code = (int)wp_remote_retrieve_response_code($resp);
        $body = wp_remote_retrieve_body($resp);
        $decoded = $body ? (json_decode($body, true) ?: []) : [];

        if ($code >= 400) {
            return new \WP_REST_Response(['error' => $decoded['error'] ?? "Data Maker API HTTP {$code}"], $code);
        }

        // ── Side effects (fire-and-forget, do not block the response) ────
        // Metadata only — never the plaintext field values, since the
        // whole point of the sealed envelope is that only the recipient's
        // private key can open it. Webhook + WP action hook get the same
        // shape; users wire whatever notifier they want.
        $meta = [
            'submissionId'  => (string)($decoded['submissionId'] ?? $submission_id),
            'formId'        => (string)$row['form_id'],
            'formSlug'      => $slug,
            'formName'      => (string)($form['name'] ?? ''),
            'updated'       => $update,
            'receivedAtUtc' => gmdate('c'),
            'submitterIp'   => self::client_ip(),
        ];

        if (!empty($row['webhook_url']) && self::url_is_safe_outbound((string)$row['webhook_url'])) {
            // Fire-and-forget — block until response only briefly so a
            // slow webhook can't stall the submitter's UX. URL re-checked
            // at send time so an option-write between admin save and now
            // can't smuggle in an internal target (SSRF defence).
            wp_remote_post((string)$row['webhook_url'], [
                'timeout'  => 5,
                'blocking' => false,
                'sslverify'=> true,
                'headers'  => ['Content-Type' => 'application/json'],
                'body'     => wp_json_encode($meta),
            ]);
        }

        /**
         * Fires after a sealed submission is accepted by the API.
         * Hook this for Post SMTP / Mailpoet / WPForms add-ons / Zapier
         * bridges / custom email or Slack notifiers. Plaintext field
         * values are intentionally not exposed — submission contents
         * are end-to-end sealed to the recipient.
         *
         * @param string $form_id       Stable form id (from the .dmf).
         * @param string $submission_id UUID of the new (or updated) submission.
         * @param array  $meta          { formId, formSlug, formName, updated, receivedAtUtc, submitterIp }.
         */
        do_action('dm_renderer_submission_received', (string)$row['form_id'], (string)($meta['submissionId']), $meta);

        return new \WP_REST_Response($decoded, $code ?: 200);
    }

    /**
     * Best-effort client IP for rate-limit bucketing. Proxy headers
     * (CF-Connecting-IP, X-Forwarded-For, X-Real-IP) are spoofable when
     * a request reaches PHP directly, so we only honour them when the
     * immediate peer (REMOTE_ADDR) is in the site's trusted-proxy list.
     *
     * Hosts behind Cloudflare / a load balancer wire their trusted ranges
     * via the `dm_renderer_trusted_proxies` filter. Accepts IPs ("1.2.3.4"),
     * CIDRs ("10.0.0.0/8") or the wildcard "*" (trust any peer — only
     * use when the WP host is unreachable except through a known proxy).
     * Default: empty array — always use REMOTE_ADDR, never trust headers.
     */
    private static function client_ip(): string {
        $remote = isset($_SERVER['REMOTE_ADDR']) ? sanitize_text_field(wp_unslash($_SERVER['REMOTE_ADDR'])) : '0.0.0.0';
        $trusted = (array)apply_filters('dm_renderer_trusted_proxies', []);
        if (!self::peer_is_trusted_proxy($remote, $trusted)) {
            return $remote;
        }
        foreach (['HTTP_CF_CONNECTING_IP', 'HTTP_X_FORWARDED_FOR', 'HTTP_X_REAL_IP'] as $h) {
            if (!empty($_SERVER[$h])) {
                $candidate = trim(explode(',', sanitize_text_field(wp_unslash($_SERVER[$h])))[0]);
                if (filter_var($candidate, FILTER_VALIDATE_IP)) return $candidate;
            }
        }
        return $remote;
    }

    private static function peer_is_trusted_proxy(string $ip, array $list): bool {
        foreach ($list as $entry) {
            $entry = trim((string)$entry);
            if ($entry === '') continue;
            if ($entry === '*' || $entry === $ip) return true;
            if (strpos($entry, '/') !== false && self::ip_in_cidr($ip, $entry)) return true;
        }
        return false;
    }

    private static function ip_in_cidr(string $ip, string $cidr): bool {
        [$subnet, $bits] = array_pad(explode('/', $cidr, 2), 2, null);
        if ($subnet === null || $bits === null) return false;
        $bits = (int)$bits;
        $ipBin     = @inet_pton($ip);
        $subnetBin = @inet_pton($subnet);
        if ($ipBin === false || $subnetBin === false || strlen($ipBin) !== strlen($subnetBin)) return false;
        $byteLen = intdiv($bits, 8);
        $remBits = $bits % 8;
        if ($byteLen && substr($ipBin, 0, $byteLen) !== substr($subnetBin, 0, $byteLen)) return false;
        if ($remBits === 0) return true;
        $mask = chr((0xFF << (8 - $remBits)) & 0xFF);
        return (($ipBin[$byteLen] ?? "\0") & $mask) === (($subnetBin[$byteLen] ?? "\0") & $mask);
    }

    /**
     * Reject URLs that point at internal infrastructure before issuing an
     * outbound HTTP request. Covers webhook + sync API destinations.
     *
     * Defence layers:
     *   1. Scheme must be http/https — strips javascript:, file://, gopher://, …
     *   2. Host is parsed; literal IP hosts are checked directly.
     *   3. DNS-resolved hosts are checked against every A/AAAA record
     *      (defends against DNS rebinding / split-horizon DNS).
     *   4. Any address in RFC1918 private / reserved (loopback, link-local,
     *      cloud metadata 169.254.169.254, multicast, broadcast) is rejected.
     *
     * Hosts can opt a specific URL back in via the `dm_renderer_url_is_safe`
     * filter — needed for localhost dev / internal webhooks where the
     * default policy is too strict.
     */
    private static function url_is_safe_outbound(string $url): bool {
        $override = apply_filters('dm_renderer_url_is_safe', null, $url);
        if (is_bool($override)) return $override;

        $parts = wp_parse_url($url);
        if (!is_array($parts)) return false;
        $scheme = strtolower((string)($parts['scheme'] ?? ''));
        if ($scheme !== 'http' && $scheme !== 'https') return false;
        $host = (string)($parts['host'] ?? '');
        if ($host === '') return false;
        // Strip IPv6 brackets so filter_var / inet_pton recognise the literal.
        if ($host !== '' && $host[0] === '[' && substr($host, -1) === ']') {
            $host = substr($host, 1, -1);
        }
        $ips = self::resolve_host($host);
        if (!$ips) return false;
        foreach ($ips as $ip) {
            if (!filter_var(
                $ip,
                FILTER_VALIDATE_IP,
                FILTER_FLAG_NO_PRIV_RANGE | FILTER_FLAG_NO_RES_RANGE
            )) {
                return false;
            }
        }
        return true;
    }

    /** Return every A/AAAA address for a host; if host is already a literal IP, returns [host]. */
    private static function resolve_host(string $host): array {
        if (filter_var($host, FILTER_VALIDATE_IP)) return [$host];
        $out = [];
        $a = @gethostbynamel($host);
        if (is_array($a)) $out = array_merge($out, $a);
        if (function_exists('dns_get_record')) {
            $aaaa = @dns_get_record($host, DNS_AAAA);
            if (is_array($aaaa)) {
                foreach ($aaaa as $rec) {
                    if (!empty($rec['ipv6'])) $out[] = (string)$rec['ipv6'];
                }
            }
        }
        return $out;
    }

    /**
     * Verify a Turnstile token with Cloudflare. POSTs to siteverify and
     * returns the boolean `success` field from the response. Failures
     * (network error, bad JSON, Cloudflare rejection) all collapse to
     * false so the caller can short-circuit with one boolean.
     *
     * @param string $token  Token harvested from the cf-turnstile-response field.
     * @param string $secret Site's Turnstile secret key (from Settings).
     * @param string $ip     Submitter IP for Cloudflare's risk model.
     */
    private static function verify_turnstile(string $token, string $secret, string $ip): bool {
        if ($token === '' || $secret === '') return false;
        $resp = wp_remote_post('https://challenges.cloudflare.com/turnstile/v0/siteverify', [
            'timeout'   => 5,
            'sslverify' => true,
            'body'      => [
                'secret'   => $secret,
                'response' => $token,
                'remoteip' => $ip,
            ],
        ]);
        if (is_wp_error($resp)) return false;
        $body = wp_remote_retrieve_body($resp);
        if (!$body) return false;
        $decoded = json_decode($body, true);
        if (!is_array($decoded)) return false;
        return !empty($decoded['success']);
    }

    private static function b64_decode_strict(string $s): string
    {
        $b = base64_decode($s, true);
        if ($b === false) {
            throw new \RuntimeException('Invalid base64.');
        }
        return $b;
    }
}
