using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;

namespace MonoForge.Editor.Views;

public sealed class BookmarksRenderer : IBackgroundRenderer
{
    private readonly HashSet<int> _lines;
    public BookmarksRenderer(HashSet<int> lines) { _lines = lines; }

    public KnownLayer Layer => KnownLayer.Selection;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView?.Document is not { } doc) return;
        if (_lines.Count == 0) return;

        var brush = Brush.Parse("#3a5378");
        var stripe = Brush.Parse("#f0c400");
        foreach (var lineNum in _lines)
        {
            if (lineNum < 1 || lineNum > doc.LineCount) continue;
            var line = doc.GetLineByNumber(lineNum);
            foreach (var rect in BackgroundGeometryBuilder.GetRectsForSegment(textView, line))
            {
                drawingContext.FillRectangle(brush, new Rect(rect.X, rect.Y, textView.Bounds.Width, rect.Height), 0);
                drawingContext.FillRectangle(stripe, new Rect(0, rect.Y, 3, rect.Height));
            }
        }
    }
}
