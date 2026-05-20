using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace MonoForge.Editor.Views;

/// <summary>
/// Renders the MonoForge hexagonal mark. Used in two sizes:
/// - Large, on the empty start page (≈140px).
/// - Small, as a 20px badge next to the "MonoForge" wordmark in the menu bar.
/// Also exposes <see cref="RenderToBitmap"/> for the window icon.
/// </summary>
public sealed class MonoForgeLogo : Control
{
    public double Scale { get; set; } = 1.0;
    public bool Light { get; set; }

    public MonoForgeLogo(double scale = 1.0, bool light = false)
    {
        Scale = scale;
        Light = light;
        // Bounding box of the actual geometry (lobe extents on each axis). The control
        // sizes itself to the real mark so the inline badge doesn't get clipped or
        // padded asymmetrically when sat next to text.
        Width = 52 * scale;
        Height = 47 * scale;
    }

    public override void Render(DrawingContext context)
    {
        Draw(context, new Rect(Bounds.Size), Scale, Light);
    }

    /// <summary>Pure drawing routine — usable both for the inline control and for rendering to a bitmap.</summary>
    public static void Draw(DrawingContext context, Rect bounds, double scale, bool light = false)
    {
        // The geometry is asymmetrical around its origin (bottom lobe extends further
        // than the upper pair). Compute the visual centroid bias so we can shift the
        // origin up; otherwise the mark sits low inside its bounding box.
        var lobeOffset = 12 * scale;
        var visualBias = lobeOffset * 0.465; // ≈ midpoint between top of top-lobes and bottom of bottom-lobe
        var center = new Point(bounds.Center.X, bounds.Center.Y - visualBias);
        var penThickness = Math.Max(1, 2 * scale);
        // Two palettes: dark for the large start-page mark (sits on dark bg, low contrast
        // is intentional), light for the inline header badge that needs to read clearly
        // against the menu bar.
        Pen pen; IBrush fill; IBrush coreFill;
        if (light)
        {
            pen = new Pen(Brush.Parse("#dde3eb"), penThickness);
            fill = Brush.Parse("#c5ccd6");
            coreFill = Brush.Parse("#1a1f24");
        }
        else
        {
            pen = new Pen(Brush.Parse("#3c4248"), penThickness);
            fill = Brush.Parse("#343a40");
            coreFill = Brush.Parse("#20262a");
        }

        var lobeRadius = 14 * scale;
        var coreRadius = 10 * scale;

        var left = Hex(center + new Vector(-lobeOffset, -lobeOffset * 0.32), lobeRadius);
        var right = Hex(center + new Vector(lobeOffset, -lobeOffset * 0.32), lobeRadius);
        var bottom = Hex(center + new Vector(0, lobeOffset * 1.25), lobeRadius);
        var core = Hex(center + new Vector(0, lobeOffset * 0.2), coreRadius);

        context.DrawGeometry(fill, null, left);
        context.DrawGeometry(fill, null, right);
        context.DrawGeometry(fill, null, bottom);
        context.DrawGeometry(coreFill, pen, core);
        context.DrawGeometry(null, pen, left);
        context.DrawGeometry(null, pen, right);
        context.DrawGeometry(null, pen, bottom);
    }

    private static StreamGeometry Hex(Point center, double radius)
    {
        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        for (var i = 0; i < 6; i++)
        {
            var angle = Math.PI / 6 + i * Math.PI / 3;
            var point = new Point(center.X + Math.Cos(angle) * radius, center.Y + Math.Sin(angle) * radius);
            if (i == 0) ctx.BeginFigure(point, true);
            else ctx.LineTo(point);
        }
        ctx.EndFigure(true);
        return geometry;
    }

    /// <summary>Render the logo into an off-screen bitmap. Used as the window icon.</summary>
    public static Bitmap RenderToBitmap(int pixelSize)
    {
        var size = new PixelSize(pixelSize, pixelSize);
        var dpi = new Vector(96, 96);
        var rtb = new RenderTargetBitmap(size, dpi);
        using var ctx = rtb.CreateDrawingContext();
        ctx.DrawRectangle(Brush.Parse("#1f2427"), null, new Rect(0, 0, pixelSize, pixelSize));
        Draw(ctx, new Rect(0, 0, pixelSize, pixelSize), pixelSize / 48.0);
        return rtb;
    }
}
