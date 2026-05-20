using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace MonoForge.Editor.Views;

public sealed class PixelEditor : Control
{
    public string ActiveColor { get; set; } = "#65a7ff";
    public string[,] Pixels { get; } = new string[16, 16];
    public bool EyedropperMode { get; set; }

    public event Action<string>? Painted;
    public event Action<string>? Picked;

    private bool _painting;
    private bool _erasing;

    public PixelEditor()
    {
        Width = 192;
        Height = 192;
        Focusable = true;
        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                Pixels[x, y] = "";
            }
        }
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Brush.Parse("#101217"), Bounds);
        var cell = Math.Min(Bounds.Width, Bounds.Height) / 16;
        var line = new Pen(Brush.Parse("#242a33"), 1);

        for (var y = 0; y < 16; y++)
        {
            for (var x = 0; x < 16; x++)
            {
                var rect = new Rect(x * cell, y * cell, cell, cell);
                var color = Pixels[x, y];
                context.FillRectangle(Brush.Parse(string.IsNullOrWhiteSpace(color) ? "#101217" : color), rect);
                context.DrawRectangle(line, rect);
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        Focus();
        var props = e.GetCurrentPoint(this).Properties;
        _erasing = props.IsRightButtonPressed;
        _painting = true;
        e.Pointer.Capture(this);
        Apply(e.GetPosition(this), e.KeyModifiers);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_painting)
        {
            return;
        }

        Apply(e.GetPosition(this), e.KeyModifiers);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _painting = false;
        _erasing = false;
        e.Pointer.Capture(null);
    }

    private void Apply(Point point, KeyModifiers modifiers)
    {
        var cell = Math.Min(Bounds.Width, Bounds.Height) / 16;
        var x = (int)(point.X / cell);
        var y = (int)(point.Y / cell);
        if (x < 0 || y < 0 || x >= 16 || y >= 16)
        {
            return;
        }

        if (EyedropperMode || modifiers.HasFlag(KeyModifiers.Alt))
        {
            var sampled = Pixels[x, y];
            if (!string.IsNullOrWhiteSpace(sampled))
            {
                Picked?.Invoke(sampled);
            }

            return;
        }

        if (_erasing)
        {
            if (Pixels[x, y] == "")
            {
                return;
            }

            Pixels[x, y] = "";
        }
        else
        {
            if (Pixels[x, y] == ActiveColor)
            {
                return;
            }

            Pixels[x, y] = ActiveColor;
        }

        Painted?.Invoke(ActiveColor);
        InvalidateVisual();
    }
}
