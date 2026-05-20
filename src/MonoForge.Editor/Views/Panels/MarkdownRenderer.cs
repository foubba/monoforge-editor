using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views.Panels;

/// <summary>
/// Minimal markdown → Avalonia rendering. Supports the bits Claude actually emits in
/// chat replies: headings (#, ##, ###), bold (**), italic (*), inline code (`),
/// fenced code blocks (```), unordered lists (- / *), and ordered lists. Not a full
/// CommonMark implementation — just enough to make Claude's replies look styled.
///
/// The renderer rebuilds the entire StackPanel on every call, which is fine for our
/// streaming use case because the panel only ever holds one assistant message at a
/// time and the messages are short. If that ever changes, switch to delta updates.
/// </summary>
public static class MarkdownRenderer
{
    private const string BodyFont = "Inter, SF Pro Text, -apple-system, Segoe UI, Helvetica Neue, Arial";
    private const string CodeFont = "Menlo, Consolas, monospace";

    public static void Render(StackPanel target, string markdown)
    {
        target.Children.Clear();
        if (string.IsNullOrEmpty(markdown)) return;

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var inCode = false;
        var codeLang = "";
        var codeBuf = new StringBuilder();
        var paragraphBuf = new StringBuilder();

        void FlushParagraph()
        {
            if (paragraphBuf.Length == 0) return;
            target.Children.Add(BuildParagraph(paragraphBuf.ToString()));
            paragraphBuf.Clear();
        }

        void FlushCode()
        {
            target.Children.Add(BuildCodeBlock(codeBuf.ToString(), codeLang));
            codeBuf.Clear();
            codeLang = "";
        }

        foreach (var raw in lines)
        {
            var line = raw;

            if (line.StartsWith("```"))
            {
                if (inCode) { FlushCode(); inCode = false; }
                else { FlushParagraph(); inCode = true; codeLang = line[3..].Trim(); }
                continue;
            }
            if (inCode)
            {
                if (codeBuf.Length > 0) codeBuf.Append('\n');
                codeBuf.Append(line);
                continue;
            }

            // Headings
            if (line.StartsWith("### "))
            {
                FlushParagraph();
                target.Children.Add(BuildHeading(line[4..], 14));
                continue;
            }
            if (line.StartsWith("## "))
            {
                FlushParagraph();
                target.Children.Add(BuildHeading(line[3..], 15));
                continue;
            }
            if (line.StartsWith("# "))
            {
                FlushParagraph();
                target.Children.Add(BuildHeading(line[2..], 17));
                continue;
            }

            // Lists
            if (line.Length >= 2 && (line.StartsWith("- ") || line.StartsWith("* ")))
            {
                FlushParagraph();
                target.Children.Add(BuildListItem("•", line[2..]));
                continue;
            }
            // Ordered lists "1. ..."
            var dotIdx = line.IndexOf(". ");
            if (dotIdx > 0 && dotIdx <= 3 && int.TryParse(line[..dotIdx], out _))
            {
                FlushParagraph();
                target.Children.Add(BuildListItem(line[..(dotIdx + 1)], line[(dotIdx + 2)..]));
                continue;
            }

            // Blank line separates paragraphs.
            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            if (paragraphBuf.Length > 0) paragraphBuf.Append(' ');
            paragraphBuf.Append(line);
        }
        if (inCode) FlushCode();
        else FlushParagraph();
    }

    private static Control BuildParagraph(string text)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily(BodyFont),
            FontSize = 13,
            Foreground = Brush(TextSecondary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19,
            Margin = new Thickness(0, 0, 0, 4)
        };
        ParseInlines(text, tb.Inlines!);
        return tb;
    }

    private static Control BuildHeading(string text, double size)
    {
        var tb = new TextBlock
        {
            FontFamily = new FontFamily(BodyFont),
            FontSize = size,
            FontWeight = FontWeight.SemiBold,
            Foreground = Brush(TextPrimary),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 6, 0, 4)
        };
        ParseInlines(text, tb.Inlines!);
        return tb;
    }

    private static Control BuildListItem(string bullet, string text)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Margin = new Thickness(0, 1, 0, 1)
        };
        grid.Children.Add(new TextBlock
        {
            Text = bullet,
            Foreground = Brush(TextMuted),
            FontFamily = new FontFamily(BodyFont),
            FontSize = 13,
            Margin = new Thickness(4, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Top
        }.At(column: 0));

        var tb = new TextBlock
        {
            FontFamily = new FontFamily(BodyFont),
            FontSize = 13,
            Foreground = Brush(TextSecondary),
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 19
        };
        ParseInlines(text, tb.Inlines!);
        grid.Children.Add(tb.At(column: 1));
        return grid;
    }

    private static Control BuildCodeBlock(string code, string lang)
    {
        var stack = new StackPanel { Margin = new Thickness(0, 4, 0, 4) };
        if (!string.IsNullOrEmpty(lang))
        {
            stack.Children.Add(new TextBlock
            {
                Text = lang,
                FontFamily = new FontFamily(BodyFont),
                FontSize = 10.5,
                Foreground = Brush(TextMuted),
                Margin = new Thickness(8, 0, 0, 2)
            });
        }
        var border = new Border
        {
            Background = Brush("#15171b"),
            BorderBrush = Brush("#262a30"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(12, 8),
            Child = new TextBlock
            {
                Text = code,
                FontFamily = new FontFamily(CodeFont),
                FontSize = 12,
                Foreground = Brush("#d4d4d4"),
                TextWrapping = TextWrapping.NoWrap
            }
        };
        // Horizontal scroll for long code lines so wrapping doesn't break formatting.
        stack.Children.Add(new ScrollViewer
        {
            Content = border,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        });
        return stack;
    }

    /// <summary>Tokenize inline markdown: **bold**, *italic*, `code`.</summary>
    private static void ParseInlines(string text, InlineCollection inlines)
    {
        var buf = new StringBuilder();
        void Flush()
        {
            if (buf.Length > 0)
            {
                inlines.Add(new Run(buf.ToString()));
                buf.Clear();
            }
        }

        var i = 0;
        while (i < text.Length)
        {
            // **bold**
            if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
            {
                var end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                if (end > 0)
                {
                    Flush();
                    inlines.Add(new Run(text.Substring(i + 2, end - i - 2)) { FontWeight = FontWeight.Bold });
                    i = end + 2;
                    continue;
                }
            }
            // `inline code`
            if (text[i] == '`')
            {
                var end = text.IndexOf('`', i + 1);
                if (end > 0)
                {
                    Flush();
                    inlines.Add(new Run(text.Substring(i + 1, end - i - 1))
                    {
                        FontFamily = new FontFamily(CodeFont),
                        FontSize = 12,
                        Foreground = Brush("#dcdcaa"),
                        Background = Brush("#1f2226")
                    });
                    i = end + 1;
                    continue;
                }
            }
            // *italic* (single star, not part of **)
            if (text[i] == '*' && (i == 0 || text[i - 1] != '*') && (i + 1 >= text.Length || text[i + 1] != '*'))
            {
                var end = -1;
                for (var k = i + 1; k < text.Length; k++)
                {
                    if (text[k] == '*' && (k + 1 >= text.Length || text[k + 1] != '*'))
                    {
                        end = k;
                        break;
                    }
                }
                if (end > 0)
                {
                    Flush();
                    inlines.Add(new Run(text.Substring(i + 1, end - i - 1)) { FontStyle = FontStyle.Italic });
                    i = end + 1;
                    continue;
                }
            }
            buf.Append(text[i]);
            i++;
        }
        Flush();
    }
}
