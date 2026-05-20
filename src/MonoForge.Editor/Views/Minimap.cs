using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using AvaloniaEdit;

namespace MonoForge.Editor.Views;

public sealed class Minimap : Border
{
    private readonly TextEditor _editor;
    private readonly MinimapCanvas _canvas;
    private bool _dragging;
    private double _dragOffsetInViewport;

    public Minimap(TextEditor editor)
    {
        _editor = editor;
        Width = 110;
        ClipToBounds = true;
        Background = Brush.Parse("#1a1a1a");
        IsHitTestVisible = true;
        Cursor = new Cursor(StandardCursorType.Hand);

        _canvas = new MinimapCanvas(editor);
        Child = _canvas;

        _editor.TextChanged += (_, _) => _canvas.InvalidateVisual();
        _editor.TextArea.TextView.ScrollOffsetChanged += (_, _) => _canvas.InvalidateVisual();
        _editor.TextArea.TextView.LayoutUpdated += (_, _) => _canvas.InvalidateVisual();

        // Use AddHandler with handledEventsToo so we still get events even if a child marks them handled.
        AddHandler(PointerPressedEvent, OnMinimapPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerMovedEvent, OnMinimapMoved, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnMinimapReleased, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
        AddHandler(PointerWheelChangedEvent, OnMinimapWheel, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, handledEventsToo: true);
    }

    private void OnMinimapPressed(object? sender, PointerPressedEventArgs e)
    {
        var y = e.GetPosition(this).Y;
        e.Pointer.Capture(this);
        _dragging = true;

        var (vpTop, vpHeight) = _canvas.GetViewportRect();
        if (y >= vpTop && y <= vpTop + vpHeight)
        {
            _dragOffsetInViewport = y - vpTop;
        }
        else
        {
            _dragOffsetInViewport = vpHeight / 2;
            ScrollFromMinimapY(y);
        }
        e.Handled = true;
    }

    private void OnMinimapMoved(object? sender, PointerEventArgs e)
    {
        if (!_dragging) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _dragging = false;
            return;
        }
        ScrollFromMinimapY(e.GetPosition(this).Y);
        e.Handled = true;
    }

    private void OnMinimapReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragging = false;
        e.Pointer.Capture(null);
    }

    private void OnMinimapWheel(object? sender, PointerWheelEventArgs e)
    {
        _editor.ScrollToVerticalOffset(Math.Max(0, _editor.VerticalOffset - e.Delta.Y * 60));
        e.Handled = true;
    }

    private void ScrollFromMinimapY(double y)
    {
        var tv = _editor.TextArea.TextView;
        var docHeight = tv.DocumentHeight;
        if (docHeight <= 0) return;
        var fullHeight = (_editor.Document?.LineCount ?? 0) * MinimapCanvas.LineHeight;
        var minimapDocHeight = Math.Min(fullHeight, Bounds.Height);
        if (minimapDocHeight <= 0) return;

        var desiredVpTop = y - _dragOffsetInViewport;
        var ratio = Math.Clamp(desiredVpTop / minimapDocHeight, 0.0, 1.0);
        var target = ratio * docHeight;
        var maxOffset = Math.Max(0, docHeight - tv.Bounds.Height);
        _editor.ScrollToVerticalOffset(Math.Clamp(target, 0, maxOffset));
    }
}

internal sealed class MinimapCanvas : Control
{
    private readonly TextEditor _editor;
    private readonly Typeface _typeface = new("Menlo");
    public const double FontPx = 3.0;
    public const double LineHeight = 3.6;
    private const int MaxCharsPerLine = 140;

    public MinimapCanvas(TextEditor editor)
    {
        _editor = editor;
        IsHitTestVisible = false;
    }

    public (double top, double height) GetViewportRect()
    {
        var tv = _editor.TextArea.TextView;
        var docHeight = tv.DocumentHeight;
        if (docHeight <= 0) return (0, 0);
        var fullHeight = _editor.Document.LineCount * LineHeight;
        var minimapDocHeight = Math.Min(fullHeight, Bounds.Height);
        var top = (tv.VerticalOffset / docHeight) * minimapDocHeight;
        var height = Math.Max(18, (tv.Bounds.Height / docHeight) * minimapDocHeight);
        return (top, height);
    }

    public override void Render(DrawingContext context)
    {
        var bounds = Bounds;
        var doc = _editor.Document;
        if (doc is null) return;
        var totalLines = doc.LineCount;
        if (totalLines == 0) return;

        var scale = 1.0;
        var fullHeight = totalLines * LineHeight;
        if (fullHeight > bounds.Height)
        {
            scale = bounds.Height / fullHeight;
        }
        var step = LineHeight * scale;
        var fontSize = Math.Max(2.0, FontPx * scale);

        var fgKeyword = BrushOf("#569cd6");
        var fgString = BrushOf("#ce9178");
        var fgComment = BrushOf("#6a9955");
        var fgIdent = BrushOf("#d4d4d4");
        var fgNumber = BrushOf("#b5cea8");

        for (var i = 1; i <= totalLines; i++)
        {
            var line = doc.GetLineByNumber(i);
            if (line.Length == 0) continue;
            var raw = doc.GetText(line.Offset, Math.Min(line.Length, MaxCharsPerLine));
            if (string.IsNullOrWhiteSpace(raw)) continue;

            var trimmedLeft = raw.TrimStart();
            var indentCount = raw.Length - trimmedLeft.Length;
            var indentX = Math.Min(indentCount * fontSize * 0.55, bounds.Width - 4);
            var y = (i - 1) * step;
            if (y > bounds.Height) break;

            IBrush brush;
            if (trimmedLeft.StartsWith("//") || trimmedLeft.StartsWith("#") || trimmedLeft.StartsWith("/*") || trimmedLeft.StartsWith("*"))
            {
                brush = fgComment;
            }
            else if (StartsWithKeyword(trimmedLeft))
            {
                brush = fgKeyword;
            }
            else if (trimmedLeft.Length > 0 && char.IsDigit(trimmedLeft[0]))
            {
                brush = fgNumber;
            }
            else if (trimmedLeft.Contains('"') || trimmedLeft.Contains('\''))
            {
                brush = fgString;
            }
            else
            {
                brush = fgIdent;
            }

            var ft = new FormattedText(
                trimmedLeft,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                _typeface,
                fontSize,
                brush)
            {
                MaxTextWidth = Math.Max(1, bounds.Width - indentX - 2),
                MaxTextHeight = step
            };
            context.DrawText(ft, new Point(indentX, y));
        }

        var (vpTop, vpHeight) = GetViewportRect();
        if (vpHeight > 0)
        {
            var viewport = new Rect(0, vpTop, bounds.Width, vpHeight);
            context.FillRectangle(BrushOf("#22ffffff"), viewport);
            context.DrawRectangle(new Pen(BrushOf("#44ffffff"), 1), viewport);
        }
    }

    private static IBrush BrushOf(string color) => Brush.Parse(color);

    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "using", "namespace", "public", "private", "internal", "protected", "static", "class",
        "struct", "interface", "enum", "record", "void", "int", "string", "bool", "double",
        "float", "var", "return", "if", "else", "for", "foreach", "while", "switch", "case",
        "break", "continue", "new", "this", "base", "null", "true", "false", "async", "await",
        "throw", "try", "catch", "finally", "def", "import", "from", "function", "let", "const",
        "fn", "pub", "mut", "package", "func", "yield", "in", "out", "as", "is", "with"
    };

    private static bool StartsWithKeyword(string trimmed)
    {
        var end = 0;
        while (end < trimmed.Length && (char.IsLetter(trimmed[end]) || trimmed[end] == '_')) end++;
        if (end == 0) return false;
        return Keywords.Contains(trimmed[..end]);
    }
}
