using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class NineSliceEditor : Window
{
    private readonly SceneObject _obj;
    private readonly Bitmap? _bitmap;
    private readonly SliceCanvas _canvas;
    public bool Applied { get; private set; }

    public NineSliceEditor(SceneObject obj)
    {
        _obj = obj;
        try { _bitmap = !string.IsNullOrEmpty(obj.TexturePath) ? new Bitmap(obj.TexturePath) : null; }
        catch { _bitmap = null; }

        Title = "9-Slice Editor";
        Width = 720; Height = 560;
        Background = Brush(MenuBackground);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        _canvas = new SliceCanvas(this);

        var sidebar = new StackPanel { Margin = new Thickness(12), Spacing = 6, Width = 200 };
        sidebar.Children.Add(Text("Slice (px)", TextPrimary, FontWeight.Bold));
        sidebar.Children.Add(SliderRow("Left", obj.SliceLeft, v => { obj.SliceLeft = v; _canvas.InvalidateVisual(); }));
        sidebar.Children.Add(SliderRow("Right", obj.SliceRight, v => { obj.SliceRight = v; _canvas.InvalidateVisual(); }));
        sidebar.Children.Add(SliderRow("Top", obj.SliceTop, v => { obj.SliceTop = v; _canvas.InvalidateVisual(); }));
        sidebar.Children.Add(SliderRow("Bottom", obj.SliceBottom, v => { obj.SliceBottom = v; _canvas.InvalidateVisual(); }));

        sidebar.Children.Add(new TextBlock
        {
            Text = "Drag the dashed guides on the preview to set slice borders. The 4 corners stay at native size; the edges stretch along one axis; the center stretches both.",
            Foreground = Brush(TextDim),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 12, 0, 0)
        });

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
        actions.Children.Add(MenuButton("Cancel", (_, _) => Close()));
        actions.Children.Add(PrimaryButton("Apply", (_, _) => { Applied = true; Close(); }));
        sidebar.Children.Add(actions);

        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("220,*") };
        root.Children.Add(sidebar.At(column: 0));
        root.Children.Add(_canvas.At(column: 1));
        Content = root;
    }

    public Bitmap? Bitmap => _bitmap;
    public SceneObject Target => _obj;

    private Control SliderRow(string label, int initial, Action<int> apply)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("60,*,40") };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        var max = _bitmap is null ? 256 : Math.Max(_bitmap.PixelSize.Width, _bitmap.PixelSize.Height) / 2;
        var s = new Slider { Minimum = 0, Maximum = max, Value = initial, SmallChange = 1 };
        var lbl = new TextBlock { Text = initial.ToString(), Foreground = Brush(TextSecondary), FontSize = 11, FontFamily = new FontFamily("Menlo"), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Right };
        s.ValueChanged += (_, _) => { var v = (int)s.Value; apply(v); lbl.Text = v.ToString(); };
        grid.Children.Add(s.At(column: 1));
        grid.Children.Add(lbl.At(column: 2));
        return grid;
    }
}

internal sealed class SliceCanvas : Control
{
    private readonly NineSliceEditor _host;
    public SliceCanvas(NineSliceEditor host) { _host = host; ClipToBounds = true; }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Avalonia.Media.Brush.Parse("#0e0f12"), Bounds);
        var bmp = _host.Bitmap;
        if (bmp is null)
        {
            var ft = new FormattedText("Object has no TexturePath.", System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 13, Avalonia.Media.Brush.Parse("#8e96a3"));
            context.DrawText(ft, new Point(20, 20));
            return;
        }

        var scale = Math.Min(Bounds.Width / bmp.PixelSize.Width, Bounds.Height / bmp.PixelSize.Height) * 0.85;
        var dw = bmp.PixelSize.Width * scale;
        var dh = bmp.PixelSize.Height * scale;
        var dx = (Bounds.Width - dw) / 2;
        var dy = (Bounds.Height - dh) / 2;
        var dest = new Rect(dx, dy, dw, dh);
        context.DrawImage(bmp, new Rect(0, 0, bmp.PixelSize.Width, bmp.PixelSize.Height), dest);
        context.DrawRectangle(new Pen(Avalonia.Media.Brush.Parse("#5a5e66"), 1), dest);

        var pen = new Pen(Avalonia.Media.Brush.Parse("#65a7ff"), 1.5, dashStyle: new DashStyle(new[] { 4.0, 3.0 }, 0));
        var obj = _host.Target;
        var xL = dx + obj.SliceLeft * scale;
        var xR = dx + dw - obj.SliceRight * scale;
        var yT = dy + obj.SliceTop * scale;
        var yB = dy + dh - obj.SliceBottom * scale;
        context.DrawLine(pen, new Point(xL, dy), new Point(xL, dy + dh));
        context.DrawLine(pen, new Point(xR, dy), new Point(xR, dy + dh));
        context.DrawLine(pen, new Point(dx, yT), new Point(dx + dw, yT));
        context.DrawLine(pen, new Point(dx, yB), new Point(dx + dw, yB));
    }
}
