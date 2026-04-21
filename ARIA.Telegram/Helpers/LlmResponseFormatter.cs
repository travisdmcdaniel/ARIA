using System.Text;
using System.Text.RegularExpressions;

namespace ARIA.Telegram.Helpers;

/// <summary>
/// Converts standard Markdown produced by LLMs into Telegram-compatible HTML.
/// Telegram HTML supports: &lt;b&gt;, &lt;i&gt;, &lt;s&gt;, &lt;code&gt;, &lt;pre&gt;, &lt;a&gt;.
/// Everything else is stripped or left as readable plain text.
/// </summary>
public static class LlmResponseFormatter
{
    private static readonly Regex FencedCodeBlock =
        new(@"```(\w*)\n?([\s\S]*?)```", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex InlineCode =
        new(@"`([^`\n]+)`", RegexOptions.Compiled);

    private static readonly Regex Link =
        new(@"\[([^\]]+)\]\((https?://[^\)]+)\)", RegexOptions.Compiled);

    private static readonly Regex HorizontalRule =
        new(@"(?m)^\s*(\*{3,}|-{3,}|_{3,})\s*$", RegexOptions.Compiled);

    private static readonly Regex Header =
        new(@"(?m)^#{1,6}\s+(.+)$", RegexOptions.Compiled);

    private static readonly Regex BoldItalic =
        new(@"\*\*\*(.+?)\*\*\*", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Bold =
        new(@"\*\*(.+?)\*\*", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex BoldUnderscore =
        new(@"__(.+?)__", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex Italic =
        new(@"(?<![*\n])\*(?!\s)(.+?)(?<!\s)\*(?!\*)", RegexOptions.Compiled);

    private static readonly Regex ItalicUnderscore =
        new(@"(?<![_\w])_(?!\s)(.+?)(?<!\s)_(?![_\w])", RegexOptions.Compiled);

    private static readonly Regex Strikethrough =
        new(@"~~(.+?)~~", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex LatexInline =
        new(@"\$([^$\n]+)\$", RegexOptions.Compiled);

    public static string ToTelegramHtml(string markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var sb = new StringBuilder();
        var pos = 0;

        foreach (Match m in FencedCodeBlock.Matches(markdown))
        {
            if (m.Index > pos)
                sb.Append(FormatTextSegment(markdown[pos..m.Index]));

            sb.Append("<pre><code>");
            sb.Append(HtmlEncode(m.Groups[2].Value.Trim()));
            sb.Append("</code></pre>");

            pos = m.Index + m.Length;
        }

        if (pos < markdown.Length)
            sb.Append(FormatTextSegment(markdown[pos..]));

        return sb.ToString();
    }

    private static string FormatTextSegment(string text)
    {
        var inlineCodes = new List<(int Start, int End, string Html)>();
        foreach (Match m in InlineCode.Matches(text))
            inlineCodes.Add((m.Index, m.Index + m.Length,
                $"<code>{HtmlEncode(m.Groups[1].Value)}</code>"));

        var sb = new StringBuilder();
        var pos = 0;
        foreach (var (start, end, html) in inlineCodes)
        {
            if (start > pos)
                sb.Append(ApplyFormatting(text[pos..start]));
            sb.Append(html);
            pos = end;
        }
        if (pos < text.Length)
            sb.Append(ApplyFormatting(text[pos..]));

        return sb.ToString();
    }

    private static string ApplyFormatting(string text)
    {
        // Extract links before encoding to preserve URLs.
        var linkMap = new Dictionary<string, string>();
        text = Link.Replace(text, m =>
        {
            var key = $"\x01L{linkMap.Count}\x01";
            linkMap[key] = $"<a href=\"{HtmlEncode(m.Groups[2].Value)}\">{HtmlEncode(m.Groups[1].Value)}</a>";
            return key;
        });

        text = HtmlEncode(text);

        // Strip LaTeX math delimiters; keep inner text readable.
        text = LatexInline.Replace(text, "$1");

        text = HorizontalRule.Replace(text, string.Empty);
        text = Header.Replace(text, "<b>$1</b>");
        text = BoldItalic.Replace(text, "<b><i>$1</i></b>");
        text = Bold.Replace(text, "<b>$1</b>");
        text = BoldUnderscore.Replace(text, "<b>$1</b>");
        text = Italic.Replace(text, "<i>$1</i>");
        text = ItalicUnderscore.Replace(text, "<i>$1</i>");
        text = Strikethrough.Replace(text, "<s>$1</s>");

        foreach (var (key, html) in linkMap)
            text = text.Replace(key, html);

        return text;
    }

    private static string HtmlEncode(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
}
