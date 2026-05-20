using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace MonoForge.Editor.Views;

public sealed class StartPageView : Control
{
    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush.Parse("#1f2427"), Bounds);
        DrawMark(context);
        DrawShortcuts(context);
    }

    private void DrawMark(DrawingContext context)
    {
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2 - 72);
        var pen = new Pen(Brush.Parse("#3c4248"), 5);
        var fill = Brush.Parse("#343a40");

        var left = Hex(center + new Vector(-52, -18), 58);
        var right = Hex(center + new Vector(52, -18), 58);
        var bottom = Hex(center + new Vector(0, 70), 58);
        var core = Hex(center + new Vector(0, 12), 42);

        context.DrawGeometry(fill, null, left);
        context.DrawGeometry(fill, null, right);
        context.DrawGeometry(fill, null, bottom);
        context.DrawGeometry(Brush.Parse("#20262a"), pen, core);
        context.DrawGeometry(null, pen, left);
        context.DrawGeometry(null, pen, right);
        context.DrawGeometry(null, pen, bottom);
    }

    private void DrawShortcuts(DrawingContext context)
    {
        var rows = new[]
        {
            ("Open Asset", "⌘P"),
            ("Reopen Closed File", "⇧⌘T"),
            ("Search in Files", "⇧⌘F"),
            ("Build and Run Project", "⌘B"),
            ("Start or Attach Debugger", "F5")
        };

        var y = Bounds.Height / 2 + 92;
        foreach (var (label, key) in rows)
        {
            DrawText(context, label, new Point(Bounds.Width / 2 - 128, y), "#a9b3bf", 13);
            DrawKey(context, key, new Point(Bounds.Width / 2 + 96, y - 4));
            y += 36;
        }
    }

    private static void DrawKey(DrawingContext context, string text, Point origin)
    {
        var formatted = MakeText(text, "#9ba5af", 12);
        var rect = new Rect(origin.X, origin.Y, formatted.Width + 14, 22);
        context.FillRectangle(Brush.Parse("#2a3035"), rect, 4);
        context.DrawRectangle(new Pen(Brush.Parse("#343b42"), 1), rect, 4);
        context.DrawText(formatted, origin + new Vector(7, 3));
    }

    private static void DrawText(DrawingContext context, string text, Point point, string color, double size)
    {
        context.DrawText(MakeText(text, color, size), point);
    }

    private static FormattedText MakeText(string text, string color, double size)
    {
        return new FormattedText(
            text,
            System.Globalization.CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface("Menlo"),
            size,
            Brush.Parse(color));
    }

    private static StreamGeometry Hex(Point center, double radius)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        for (var i = 0; i < 6; i++)
        {
            var angle = Math.PI / 6 + i * Math.PI / 3;
            var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            if (i == 0)
            {
                ctx.BeginFigure(point, true);
            }
            else
            {
                ctx.LineTo(point);
            }
        }

        ctx.EndFigure(true);
        return geometry;
    }
}
