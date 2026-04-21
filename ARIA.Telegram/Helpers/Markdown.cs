using System.Text;

namespace ARIA.Telegram.Helpers;

/// <summary>
/// Utilities for building Telegram MarkdownV2-formatted strings.
///
/// Rule: every string that originates from user data, LLM output, or runtime
/// values MUST be passed through Escape() before being embedded in a MarkdownV2
/// message. Formatting markers (*bold*, _italic_, etc.) are applied by the
/// helper methods here, which call Escape() on the inner text automatically.
/// </summary>
public static class Markdown
{
    // All characters that Telegram MarkdownV2 requires to be escaped in plain text.
    private static readonly char[] SpecialChars =
        ['_', '*', '[', ']', '(', ')', '~', '`', '>', '#', '+', '-', '=', '|', '{', '}', '.', '!', '\\'];

    /// <summary>Escapes all MarkdownV2 special characters in a plain-text string.</summary>
    public static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var sb = new StringBuilder(text.Length + 16);
        foreach (var c in text)
        {
            if (Array.IndexOf(SpecialChars, c) >= 0)
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Bold text. Inner text is escaped automatically.</summary>
    public static string Bold(string text) => $"*{Escape(text)}*";

    /// <summary>Italic text. Inner text is escaped automatically.</summary>
    public static string Italic(string text) => $"_{Escape(text)}_";

    /// <summary>Inline code. Inner text is escaped automatically.</summary>
    public static string Code(string text) => $"`{Escape(text)}`";

    /// <summary>
    /// Fenced code block. Inside code blocks only backticks and backslashes
    /// need escaping (Telegram MarkdownV2 spec).
    /// </summary>
    public static string CodeBlock(string text, string? language = null)
    {
        var escaped = text.Replace("\\", "\\\\").Replace("`", "\\`");
        return language is null
            ? $"```\n{escaped}\n```"
            : $"```{language}\n{escaped}\n```";
    }

    /// <summary>Inline link. Both label and URL are escaped automatically.</summary>
    public static string Link(string label, string url) =>
        $"[{Escape(label)}]({Escape(url)})";

    /// <summary>Strikethrough text. Inner text is escaped automatically.</summary>
    public static string Strike(string text) => $"~{Escape(text)}~";
}
