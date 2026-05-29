using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace DataMaker.Schema.Layout;

/// <summary>
/// Tiny Markdown converter for the <see cref="RichTextColumn.Markdown"/>
/// body. Supports the subset DataMaker authors actually need: ATX headings
/// (<c>#</c> / <c>##</c> / <c>###</c>), <c>**bold**</c>, <c>*italic*</c>,
/// inline <c>`code`</c>, dash / star unordered lists, numeric ordered
/// lists, and blank-line paragraph separation.
///
/// <para>
/// Lives in <c>DataMaker.Schema</c> because the Markdown format is part of
/// the data contract — every renderer interprets the same syntax. C#
/// renderers (Uno desktop, Terminal) use this; the web renderer has a
/// matching JS implementation in <c>renderer.js</c> that produces the
/// same output. Tests pin both sides to the same expected output.
/// </para>
///
/// <para>
/// Out of scope: links, images, blockquotes, tables, raw HTML inlining.
/// We can extend the subset later — keep round-trip coverage in
/// <c>MarkdownTests</c> for any new construct.
/// </para>
/// </summary>
public static class Markdown
{
    public static string ToHtml(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        // Normalise every line-terminator flavour to '\n'. WinUI's
        // TextBox.AcceptsReturn=true emits bare '\r' (CR) as the break char,
        // so a markdown string authored in the layout editor's textbox has
        // CR-only newlines — without this normalisation the paragraph-split
        // (Split("\n\n")) never matches and the whole thing renders as one
        // run-on block.
        var src = markdown!.Replace("\r\n", "\n").Replace('\r', '\n');

        var sb = new StringBuilder(src.Length + 64);
        // Blocks separated by blank line(s).
        foreach (var block in src.Split("\n\n", StringSplitOptions.None))
        {
            var trimmed = block.Trim('\n');
            if (trimmed.Length == 0) continue;
            sb.Append(BlockToHtml(trimmed));
        }
        return sb.ToString();
    }

    /// <summary>
    /// Strip Markdown syntax for plain-text rendering — used by the
    /// terminal client and the Uno desktop fallback. Preserves paragraph
    /// breaks (double newline); collapses whitespace inside paragraphs.
    /// </summary>
    public static string ToPlain(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown)) return "";
        var t = markdown!.Replace("\r\n", "\n");

        // Strip ATX header markers.
        t = Regex.Replace(t, @"^#{1,6}\s+", "", RegexOptions.Multiline);
        // Strip list markers.
        t = Regex.Replace(t, @"^\s*[-*]\s+", "", RegexOptions.Multiline);
        t = Regex.Replace(t, @"^\s*\d+\.\s+", "", RegexOptions.Multiline);
        // Strip inline emphasis / code fences.
        t = Regex.Replace(t, @"\*\*([^*]+)\*\*", "$1");
        t = Regex.Replace(t, @"(?<!\*)\*([^*]+)\*(?!\*)", "$1");
        t = Regex.Replace(t, @"_([^_]+)_", "$1");
        t = Regex.Replace(t, @"`([^`]+)`", "$1");

        return t.Trim();
    }

    private static string BlockToHtml(string block)
    {
        var lines = block.Split('\n');

        // Single-line heading.
        if (lines.Length == 1)
        {
            var line = lines[0];
            if (line.StartsWith("### ", StringComparison.Ordinal)) return "<h3>" + InlineHtml(line[4..]) + "</h3>";
            if (line.StartsWith("## ",  StringComparison.Ordinal)) return "<h2>" + InlineHtml(line[3..]) + "</h2>";
            if (line.StartsWith("# ",   StringComparison.Ordinal)) return "<h1>" + InlineHtml(line[2..]) + "</h1>";
        }

        // Unordered list — every line starts with "- " or "* ".
        if (lines.All(l => l.StartsWith("- ", StringComparison.Ordinal) || l.StartsWith("* ", StringComparison.Ordinal)))
        {
            var items = string.Concat(lines.Select(l => "<li>" + InlineHtml(l[2..]) + "</li>"));
            return "<ul>" + items + "</ul>";
        }

        // Ordered list — every line matches "<digits>. ".
        if (lines.All(l => Regex.IsMatch(l, @"^\d+\.\s+")))
        {
            var items = string.Concat(lines.Select(l =>
            {
                var dot = l.IndexOf('.');
                return "<li>" + InlineHtml(l[(dot + 1)..].TrimStart()) + "</li>";
            }));
            return "<ol>" + items + "</ol>";
        }

        // Default: paragraph. Multi-line paragraphs collapse newlines to spaces.
        return "<p>" + InlineHtml(string.Join(' ', lines)) + "</p>";
    }

    private static string InlineHtml(string text)
    {
        // Encode first so user-authored angle brackets stay literal,
        // then layer Markdown emphasis on top of the encoded string.
        var encoded = WebUtility.HtmlEncode(text);
        encoded = Regex.Replace(encoded, @"\*\*([^*]+)\*\*", "<strong>$1</strong>");
        encoded = Regex.Replace(encoded, @"(?<!\*)\*([^*]+)\*(?!\*)", "<em>$1</em>");
        encoded = Regex.Replace(encoded, @"_([^_]+)_", "<em>$1</em>");
        encoded = Regex.Replace(encoded, @"`([^`]+)`", "<code>$1</code>");
        return encoded;
    }
}
