using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class AnimationEditor : Window
{
    private AnimationClip _clip = new();
    private Bitmap? _sheet;
    private readonly PreviewCanvas _preview;
    private readonly FrameStrip _strip;
    private readonly TextBlock _status = new() { Foreground = Brush(TextMuted), FontSize = 12, Padding = new Thickness(10, 4) };
    private DispatcherTimer? _timer;
    private int _previewFrameIndex;

    public AnimationEditor()
    {
        Title = "Animation Editor";
        Width = 1000; Height = 640;
        Background = Brush(EditorBackground);

        _preview = new PreviewCanvas(this);
        _strip = new FrameStrip(this);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,160,Auto"),
            ColumnDefinitions = new ColumnDefinitions("260,*"),
            Background = Brush(EditorBackground)
        };

        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        toolbar.Children.Add(MenuButton("Load Sheet...", async (_, _) => await LoadSheet()));
        toolbar.Children.Add(MenuButton("New", (_, _) => { _clip = new AnimationClip(); Refresh(); }));
        toolbar.Children.Add(MenuButton("Open...", async (_, _) => await Open()));
        toolbar.Children.Add(MenuButton("Save...", async (_, _) => await Save()));
        toolbar.Children.Add(MenuButton("▶ Play", (_, _) => Play()));
        toolbar.Children.Add(MenuButton("◼ Stop", (_, _) => Stop()));
        toolbar.Children.Add(MenuButton("Onion", (_, _) =>
        {
            _preview.OnionSkin = !_preview.OnionSkin;
            _preview.InvalidateVisual();
        }));
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar.At(row: 0, column: 0));

        var sidebar = BuildSidebar();
        root.Children.Add(sidebar.At(row: 1, column: 0));
        root.Children.Add(_preview.At(row: 1, column: 1));
        root.Children.Add(_strip.At(row: 2, column: 0));
        Grid.SetColumnSpan(_strip, 2);

        Grid.SetColumnSpan(_status, 2);
        root.Children.Add(_status.At(row: 3, column: 0));

        Content = root;
        Refresh();
    }

    public AnimationClip Clip => _clip;
    public Bitmap? Sheet => _sheet;
    public int PreviewFrame => _previewFrameIndex;

    private Control BuildSidebar()
    {
        var panel = new StackPanel { Margin = new Thickness(10), Spacing = 6 };
        panel.Children.Add(Text("Clip", TextPrimary, FontWeight.Bold));

        var name = new TextBox { Text = _clip.Name, Watermark = "Name", Background = Brush(InputBackground), Foreground = Brush(TextSecondary), BorderBrush = Brush(BorderColor) };
        name.TextChanged += (_, _) => _clip.Name = name.Text ?? "";
        panel.Children.Add(LabelRow("Name", name));

        var fw = new NumericUpDown { Value = _clip.FrameWidth, Minimum = 1, Maximum = 1024 };
        fw.ValueChanged += (_, _) => { _clip.FrameWidth = (int)(fw.Value ?? 32); _preview.InvalidateVisual(); _strip.InvalidateVisual(); };
        panel.Children.Add(LabelRow("Frame W", fw));

        var fh = new NumericUpDown { Value = _clip.FrameHeight, Minimum = 1, Maximum = 1024 };
        fh.ValueChanged += (_, _) => { _clip.FrameHeight = (int)(fh.Value ?? 32); _preview.InvalidateVisual(); _strip.InvalidateVisual(); };
        panel.Children.Add(LabelRow("Frame H", fh));

        var fps = new NumericUpDown { Value = _clip.Fps, Minimum = 1, Maximum = 120 };
        fps.ValueChanged += (_, _) => { _clip.Fps = (int)(fps.Value ?? 12); RestartTimer(); };
        panel.Children.Add(LabelRow("FPS", fps));

        var loop = new CheckBox { Content = "Loop", IsChecked = _clip.Loop, Foreground = Brush(TextSecondary) };
        loop.IsCheckedChanged += (_, _) => _clip.Loop = loop.IsChecked == true;
        panel.Children.Add(loop);

        panel.Children.Add(Text("Frames", TextPrimary, FontWeight.Bold).WithMargin(0, 10, 0, 4));
        panel.Children.Add(new TextBlock
        {
            Text = "Click frames on the sheet to add/remove from the sequence.",
            Foreground = Brush(TextDim),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap
        });

        return panel;
    }

    private static Control LabelRow(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("70,*"), Height = 28 };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        grid.Children.Add(input.At(column: 1));
        return grid;
    }

    private async Task LoadSheet()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("Image") { Patterns = ["*.png", "*.jpg"] }]
        });
        var f = files.FirstOrDefault();
        if (f is null) return;
        try
        {
            _sheet = new Bitmap(f.Path.LocalPath);
            _clip.TexturePath = f.Path.LocalPath;
            Refresh();
        }
        catch (Exception ex) { _status.Text = "Load failed: " + ex.Message; }
    }

    private async Task Save()
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = _clip.Name + ".anim.json",
            FileTypeChoices = [new FilePickerFileType("Animation JSON") { Patterns = ["*.json"] }]
        });
        if (f is null) return;
        File.WriteAllText(f.Path.LocalPath, JsonSerializer.Serialize(_clip, new JsonSerializerOptions { WriteIndented = true }));
        _status.Text = "Saved " + Path.GetFileName(f.Path.LocalPath);
    }

    private async Task Open()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("Animation JSON") { Patterns = ["*.json"] }]
        });
        var f = files.FirstOrDefault();
        if (f is null) return;
        try
        {
            var json = File.ReadAllText(f.Path.LocalPath);
            var clip = JsonSerializer.Deserialize<AnimationClip>(json);
            if (clip is null) return;
            _clip = clip;
            if (!string.IsNullOrEmpty(_clip.TexturePath) && File.Exists(_clip.TexturePath))
                _sheet = new Bitmap(_clip.TexturePath);
            Refresh();
        }
        catch (Exception ex) { _status.Text = "Open failed: " + ex.Message; }
    }

    public void ToggleFrame(int frameIndex)
    {
        if (_clip.Frames.Contains(frameIndex)) _clip.Frames.Remove(frameIndex);
        else _clip.Frames.Add(frameIndex);
        _strip.InvalidateVisual();
        _status.Text = $"{_clip.Frames.Count} frames";
    }

    private void Play()
    {
        if (_clip.Frames.Count == 0) return;
        Stop();
        _previewFrameIndex = 0;
        RestartTimer();
    }

    private void Stop()
    {
        _timer?.Stop();
        _timer = null;
    }

    private void RestartTimer()
    {
        Stop();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1.0 / Math.Max(1, _clip.Fps)) };
        _timer.Tick += (_, _) =>
        {
            if (_clip.Frames.Count == 0) { Stop(); return; }
            _previewFrameIndex++;
            if (_previewFrameIndex >= _clip.Frames.Count)
            {
                if (!_clip.Loop) { Stop(); return; }
                _previewFrameIndex = 0;
            }
            _preview.InvalidateVisual();
        };
        _timer.Start();
    }

    private void Refresh()
    {
        _preview.InvalidateVisual();
        _strip.InvalidateVisual();
        _status.Text = _sheet is null ? "Load a sprite sheet to begin." : $"{_clip.Frames.Count} frames @ {_clip.Fps} fps";
    }
}

internal sealed class PreviewCanvas : Control
{
    private readonly AnimationEditor _host;
    public bool OnionSkin { get; set; }
    public PreviewCanvas(AnimationEditor host) { _host = host; ClipToBounds = true; }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Avalonia.Media.Brush.Parse("#111316"), Bounds);
        var sheet = _host.Sheet;
        if (sheet is null || _host.Clip.Frames.Count == 0) return;

        var n = _host.Clip.Frames.Count;
        var curIdx = Math.Clamp(_host.PreviewFrame, 0, n - 1);

        var perRow = Math.Max(1, sheet.PixelSize.Width / Math.Max(1, _host.Clip.FrameWidth));
        var scale = Math.Min(Bounds.Width / _host.Clip.FrameWidth, Bounds.Height / _host.Clip.FrameHeight) * 0.6;
        var dw = _host.Clip.FrameWidth * scale;
        var dh = _host.Clip.FrameHeight * scale;
        var dx = (Bounds.Width - dw) / 2;
        var dy = (Bounds.Height - dh) / 2;

        void DrawFrame(int frame, double opacity)
        {
            var sx = (frame % perRow) * _host.Clip.FrameWidth;
            var sy = (frame / perRow) * _host.Clip.FrameHeight;
            using var p = context.PushOpacity(opacity);
            context.DrawImage(sheet, new Rect(sx, sy, _host.Clip.FrameWidth, _host.Clip.FrameHeight), new Rect(dx, dy, dw, dh));
        }

        if (OnionSkin)
        {
            var prev = (curIdx - 1 + n) % n;
            var next = (curIdx + 1) % n;
            DrawFrame(_host.Clip.Frames[prev], 0.25);
            DrawFrame(_host.Clip.Frames[next], 0.25);
        }

        DrawFrame(_host.Clip.Frames[curIdx], 1.0);
    }
}

internal sealed class FrameStrip : Control
{
    private readonly AnimationEditor _host;
    private const double Cell = 64;
    public FrameStrip(AnimationEditor host) { _host = host; ClipToBounds = true; Focusable = true; }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Avalonia.Media.Brush.Parse("#1a1c20"), Bounds);
        var sheet = _host.Sheet;
        if (sheet is null) return;

        var perRow = Math.Max(1, sheet.PixelSize.Width / Math.Max(1, _host.Clip.FrameWidth));
        var rows = Math.Max(1, sheet.PixelSize.Height / Math.Max(1, _host.Clip.FrameHeight));
        var total = perRow * rows;

        var x = 8.0;
        var y = 8.0;
        for (var i = 0; i < total && i < 300; i++)
        {
            if (x + Cell > Bounds.Width) { x = 8; y += Cell + 6; }
            if (y + Cell > Bounds.Height) break;

            var dest = new Rect(x, y, Cell, Cell);
            var col = i % perRow;
            var row = i / perRow;
            var src = new Rect(col * _host.Clip.FrameWidth, row * _host.Clip.FrameHeight, _host.Clip.FrameWidth, _host.Clip.FrameHeight);
            context.DrawImage(sheet, src, dest);

            if (_host.Clip.Frames.Contains(i))
            {
                context.DrawRectangle(new Pen(Avalonia.Media.Brush.Parse("#65a7ff"), 2), dest);
                var order = _host.Clip.Frames.IndexOf(i) + 1;
                var ft = new FormattedText(order.ToString(), System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, Typeface.Default, 11, Avalonia.Media.Brush.Parse("#ffffff"));
                context.DrawText(ft, new Point(x + 4, y + 2));
            }
            else
            {
                context.DrawRectangle(new Pen(Avalonia.Media.Brush.Parse("#343943"), 1), dest);
            }

            x += Cell + 6;
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var sheet = _host.Sheet;
        if (sheet is null) return;
        var p = e.GetPosition(this);
        var perRow = Math.Max(1, sheet.PixelSize.Width / Math.Max(1, _host.Clip.FrameWidth));
        var rows = Math.Max(1, sheet.PixelSize.Height / Math.Max(1, _host.Clip.FrameHeight));
        var total = perRow * rows;
        var x = 8.0;
        var y = 8.0;
        for (var i = 0; i < total && i < 300; i++)
        {
            if (x + Cell > Bounds.Width) { x = 8; y += Cell + 6; }
            if (y + Cell > Bounds.Height) break;
            var dest = new Rect(x, y, Cell, Cell);
            if (dest.Contains(p)) { _host.ToggleFrame(i); InvalidateVisual(); return; }
            x += Cell + 6;
        }
    }
}
