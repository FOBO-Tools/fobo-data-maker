<?php
namespace Fobo\DataMakerForms\Tests;

use Fobo\DataMakerForms\BundleBuilder;
use PHPUnit\Framework\TestCase;
use ReflectionClass;

/**
 * Tests for the AARRGGBB → RRGGBBAA repair path. The desktop renderer
 * shipped a baked .dmf with Uno-convention 8-char hex; the WP plugin
 * rewrites those to CSS-correct form on read until every customer
 * re-exports against the patched binary.
 */
final class BundleBuilderTest extends TestCase
{
    /** @return callable(string):mixed */
    private static function priv(string $method): callable
    {
        $rc  = new ReflectionClass(BundleBuilder::class);
        $rm  = $rc->getMethod($method);
        $rm->setAccessible(true);
        return static fn(...$args) => $rm->invoke(null, ...$args);
    }

    public function test_argb_to_rgba_swap(): void
    {
        $swap = self::priv('swap_argb_to_rgba');
        $this->assertSame('background-color:#04C8FF14;', $swap('background-color:#1404C8FF;'));
        $this->assertSame('background-color:#04C8FF1A;', $swap('background-color:#1A04C8FF;'));
        // 6-char hex unchanged.
        $this->assertSame('background-color:#04C8FF;', $swap('background-color:#04C8FF;'));
        // Fully-transparent ambiguous case still swaps (#00000000 ↔ #00000000).
        $this->assertSame('background-color:#00000000;', $swap('background-color:#00000000;'));
    }

    public function test_looks_like_uno_hex_bundle_detects_uno(): void
    {
        $detect = self::priv('looks_like_uno_hex_bundle');
        $css = [
            'button/abc/hover'   => 'background-color:#1404C8FF;',
            'button/abc/pressed' => 'background-color:#2904C8FF;',
            'button/abc'         => 'background-color:#04C8FF;',
        ];
        $this->assertTrue($detect($css, ''));
    }

    public function test_looks_like_uno_hex_bundle_rejects_css_form(): void
    {
        $detect = self::priv('looks_like_uno_hex_bundle');
        // Alpha at the end (proper CSS RRGGBBAA). Must not be rewritten.
        $css = [
            'button/abc/hover' => 'background-color:#04C8FF14;',
        ];
        $this->assertFalse($detect($css, ''));
    }

    public function test_looks_like_uno_hex_bundle_ignores_neutral_values(): void
    {
        $detect = self::priv('looks_like_uno_hex_bundle');
        // #00000000 (transparent) round-trips identically; #FFFFFFFF same.
        // The detector should still classify the remaining FF-tailed value
        // as Uno-style.
        $css = [
            'button/a' => 'background-color:#00000000;',
            'button/b' => 'background-color:#1404C8FF;',
        ];
        $this->assertTrue($detect($css, ''));
    }

    public function test_looks_like_uno_hex_bundle_empty_returns_false(): void
    {
        $detect = self::priv('looks_like_uno_hex_bundle');
        $this->assertFalse($detect([], ''));
        $this->assertFalse($detect(['k' => 'no hex here'], ''));
    }

    public function test_build_payload_normalizes_baked_bundle(): void
    {
        $row = [
            'form_json'          => '{"id":"f1","name":"f","fields":[]}',
            'compiled_json'      => '{}',
            'element_css_json'   => json_encode([
                'button/x/hover' => 'background-color:#1404C8FF;',
                'button/x'       => 'background-color:#04C8FF;',
            ]),
            'palette_css'        => ':root{--dm-accent:#04C8FF;}',
            'hidden_elements'    => '',
            'message_overrides'  => '',
        ];
        $payload = BundleBuilder::build_payload($row);
        $this->assertSame(
            'background-color:#04C8FF14;',
            $payload['elementCss']['button/x/hover']
        );
        $this->assertSame(
            'background-color:#04C8FF;',
            $payload['elementCss']['button/x']
        );
    }
}
