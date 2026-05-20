using Avalonia;
using Avalonia.Media;
using AvaloniaEdit.Rendering;

namespace MonoForge.Editor.Views;

/// <summary>One compiler diagnostic anchored to a document line.</summary>
public sealed record DiagnosticInfo(int Line, string Severity, string Code, string Message);

/// <summary>
/// Background renderer that draws a wavy red/yellow underline on lines that have build
/// diagnostics. The squiggle is approximated as a series of short line segments forming
/// a zigzag — close enough to a sine wave at typical font sizes and a lot cheaper than
/// stroking a real curve.
/// </summary>
public sealed class DiagnosticsRenderer : IBackgroundRenderer
{
    private readonly Dictionary<int, DiagnosticInfo> _byLine = new();

    public KnownLayer Layer => KnownLayer.Selection; // drawn over text background, under caret

    public void Set(IEnumerable<DiagnosticInfo> diagnostics)
    {
        _byLine.Clear();
        foreach (var d in diagnostics)
        {
            // Severity priority: error > warning. If a line has both, keep the error.
            if (_byLine.TryGetValue(d.Line, out var existing) && existing.Severity == "error" && d.Severity != "error") continue;
            _byLine[d.Line] = d;
        }
    }

    public void Add(DiagnosticInfo diagnostic)
    {
        if (_byLine.TryGetValue(diagnostic.Line, out var existing) && existing.Severity == "error" && diagnostic.Severity != "error") return;
        _byLine[diagnostic.Line] = diagnostic;
    }

    public void Clear() => _byLine.Clear();

    public IReadOnlyDictionary<int, DiagnosticInfo> Diagnostics => _byLine;

    public void Draw(TextView textView, DrawingContext drawingContext)
    {
        if (textView?.Document is not { } doc) return;
        if (!textView.VisualLinesValid || _byLine.Count == 0) return;

        var errorPen = new Pen(Brush.Parse("#ff6b6b"), 1.4);
        var warnPen = new Pen(Brush.Parse("#ffd166"), 1.4);

        foreach (var visualLine in textView.VisualLines)
        {
            var docLine = visualLine.FirstDocumentLine;
            if (!_byLine.TryGetValue(docLine.LineNumber, out var diag)) continue;

            var lineText = doc.GetText(docLine);
            // Skip purely blank lines — there's nothing to underline.
            if (string.IsNullOrWhiteSpace(lineText)) continue;

            // Underline runs from the first non-whitespace character to the end of the
            // text on the line; column data is unreliable across compilers so we paint
            // the whole significant range rather than just the offending token.
            var firstNonWs = 0;
            while (firstNonWs < lineText.Length && char.IsWhiteSpace(lineText[firstNonWs])) firstNonWs++;
            var startCol = firstNonWs;
            var endCol = lineText.TrimEnd().Length;
            if (endCol <= startCol) continue;

            var startX = visualLine.GetVisualColumn(startCol) * textView.WideSpaceWidth - textView.HorizontalOffset;
            var endX = visualLine.GetVisualColumn(endCol) * textView.WideSpaceWidth - textView.HorizontalOffset;
            var y = visualLine.VisualTop + visualLine.Height - textView.VerticalOffset - 1;

            if (endX <= 0 || startX >= textView.Bounds.Width) continue;
            startX = Math.Max(0, startX);
            endX = Math.Min(textView.Bounds.Width, endX);

            DrawSquiggle(drawingContext, diag.Severity == "error" ? errorPen : warnPen, startX, endX, y);
        }
    }

    /// <summary>Zigzag underline, ~3px tall, ~3px wavelength.</summary>
    private static void DrawSquiggle(DrawingContext ctx, IPen pen, double x0, double x1, double y)
    {
        const double dx = 2.0;
        const double amplitude = 1.5;
        var goingDown = true;
        var prev = new Point(x0, y);
        for (var x = x0 + dx; x <= x1; x += dx)
        {
            var p = new Point(x, y + (goingDown ? amplitude : -amplitude));
            ctx.DrawLine(pen, prev, p);
            prev = p;
            goingDown = !goingDown;
        }
    }
}
