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
        // Reuse the shared logo drawing routine — scaled up for the empty start page.
        var center = new Point(Bounds.Width / 2, Bounds.Height / 2 - 72);
        const double logoSize = 240;
        var rect = new Rect(center.X - logoSize / 2, center.Y - logoSize / 2, logoSize, logoSize);
        MonoForgeLogo.Draw(context, rect, logoSize / 32.0);
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

}
