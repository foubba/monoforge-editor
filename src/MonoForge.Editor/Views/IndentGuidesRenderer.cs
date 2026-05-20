using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace MonoForge.Editor.Views;

/// <summary>
/// Draws vertical dashed lines at each indentation level for any line containing
/// leading whitespace, similar to VS Code's indent guides.
/// </summary>
public sealed class IndentGuidesRenderer : IBackgroundRenderer
{
    public KnownLayer Layer => KnownLayer.Background;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView?.Document is not { } doc) return;
        if (!textView.VisualLinesValid) return;

        var pen = new Pen(Brush.Parse("#363a45"), 1, dashStyle: new DashStyle(new[] { 1.0, 3.0 }, 0));
        var indentSize = 4;
        var charWidth = textView.WideSpaceWidth;
        if (charWidth <= 0) return;

        foreach (var line in textView.VisualLines)
        {
            var firstDocLine = line.FirstDocumentLine;
            var rawText = doc.GetText(firstDocLine.Offset, firstDocLine.Length);
            var leading = 0;
            foreach (var c in rawText)
            {
                if (c == ' ') leading++;
                else if (c == '\t') leading += indentSize;
                else break;
            }
            if (leading == 0) continue;

            var visualTop = line.VisualTop - textView.VerticalOffset;
            var lineHeight = line.Height;

            for (var level = indentSize; level <= leading; level += indentSize)
            {
                var x = level * charWidth - textView.HorizontalOffset;
                if (x < 0 || x > textView.Bounds.Width) continue;
                drawingContext.DrawLine(pen, new Point(x, visualTop), new Point(x, visualTop + lineHeight));
            }
        }
    }
}
