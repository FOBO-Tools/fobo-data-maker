<?php
namespace Fobo\DataMakerForms\Tests;

use Fobo\DataMakerForms\Shortcode;
use PHPUnit\Framework\TestCase;
use ReflectionClass;

/**
 * Pure-function tests against Shortcode::sanitize_fonts_css. Validates
 * the defense-in-depth strip of @import + remote url(...) references
 * that a hostile unsigned .dmf could otherwise use to leak visitor
 * state via CSS to an attacker-controlled host.
 */
final class ShortcodeTest extends TestCase
{
    private static function sanitize(string $css): string
    {
        $rc = new ReflectionClass(Shortcode::class);
        $rm = $rc->getMethod('sanitize_fonts_css');
        $rm->setAccessible(true);
        return $rm->invoke(null, $css);
    }

    public function test_data_url_fonts_pass_through(): void
    {
        $css = "@font-face{font-family:Foo;src:url(data:font/woff2;base64,AAAA);}";
        $this->assertSame($css, self::sanitize($css));
    }

    public function test_remote_url_replaced_with_empty(): void
    {
        $css = "@font-face{font-family:Foo;src:url(https://evil.example/leak.woff2);}";
        $this->assertStringContainsString('url()', self::sanitize($css));
        $this->assertStringNotContainsString('evil.example', self::sanitize($css));
    }

    public function test_import_rule_stripped(): void
    {
        $css = "@import url('https://evil.example/exfil.css');\n@font-face{font-family:A;src:url(data:font/woff2;base64,AAA);}";
        $cleaned = self::sanitize($css);
        $this->assertStringNotContainsString('@import', $cleaned);
        $this->assertStringNotContainsString('evil.example', $cleaned);
        // Legitimate data-URI font survives.
        $this->assertStringContainsString('data:font/woff2', $cleaned);
    }

    public function test_close_style_tag_neutralised(): void
    {
        $css = "x{} </style><script>alert(1)</script>";
        $cleaned = self::sanitize($css);
        $this->assertStringNotContainsString('</style>', $cleaned);
        // The neutralised form must still contain the backslash escape so
        // the bytes can't be reassembled into a tag-break-out inside <style>.
        $this->assertStringContainsString('<\\/style>', $cleaned);
    }

    public function test_quoted_url_variants_handled(): void
    {
        $cases = [
            "src:url('https://evil/x')",
            'src:url("https://evil/x")',
            'src:url( https://evil/x )',
        ];
        foreach ($cases as $css) {
            $cleaned = self::sanitize($css);
            $this->assertStringNotContainsString('evil', $cleaned, "remote URL leaked from: {$css}");
        }
    }
}
