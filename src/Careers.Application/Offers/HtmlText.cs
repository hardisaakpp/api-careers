using System.Net;
using System.Text;

namespace Careers.Application.Offers;

/// <summary>HTML from a third party to plain text: nothing of its markup, scripts or styles travels any further.</summary>
public static class HtmlText
{
    private static readonly HashSet<string> Blocks = new(StringComparer.OrdinalIgnoreCase)
    {
        "p", "div", "br", "li", "ul", "ol", "h1", "h2", "h3", "h4", "h5", "h6", "tr", "table", "section", "article",
    };

    private static readonly HashSet<string> Ignored = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "iframe", "object", "embed", "noscript",
    };

    public static string From(string? html, int maxLength = 6000)
    {
        if (string.IsNullOrEmpty(html))
        {
            return string.Empty;
        }

        var text = new StringBuilder(html.Length);
        var ignoring = 0;
        for (var i = 0; i < html.Length;)
        {
            if (html[i] != '<')
            {
                var next = html.IndexOf('<', i);
                next = next < 0 ? html.Length : next;
                if (ignoring == 0)
                {
                    text.Append(html, i, next - i);
                }

                i = next;
                continue;
            }

            var end = html.IndexOf('>', i);
            if (end < 0)
            {
                break; // an unterminated tag: drop the rest instead of guessing
            }

            var tag = html.Substring(i + 1, end - i - 1).Trim();
            var closing = tag.StartsWith('/');
            var name = new string(tag.TrimStart('/').TakeWhile(c => char.IsLetterOrDigit(c)).ToArray());
            if (Ignored.Contains(name))
            {
                ignoring = closing ? Math.Max(0, ignoring - 1) : ignoring + (tag.EndsWith('/') ? 0 : 1);
            }
            else if (ignoring == 0 && Blocks.Contains(name))
            {
                text.Append('\n');
            }

            i = end + 1;
        }

        var decoded = WebUtility.HtmlDecode(text.ToString());
        var lines = decoded.Split('\n').Select(l => string.Join(' ', l.Split([' ', '\t', '\r'], StringSplitOptions.RemoveEmptyEntries)));
        var joined = new StringBuilder();
        var blank = 0;
        foreach (var line in lines)
        {
            blank = line.Length == 0 ? blank + 1 : 0;
            if (blank <= 1)
            {
                joined.Append(line).Append('\n');
            }
        }

        var result = joined.ToString().Trim();
        return result.Length <= maxLength ? result : result[..maxLength];
    }
}
