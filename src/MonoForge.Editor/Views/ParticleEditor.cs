using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class ParticleEditor : Window
{
    private ParticleSystem _sys = new();
    private readonly ParticlePreview _preview;
    private readonly TextBlock _status = new() { Foreground = Brush(TextDim), FontSize = 12, Padding = new Thickness(10, 4) };

    public ParticleEditor()
    {
        Title = "Particle Editor";
        Width = 980; Height = 640;
        Background = Brush(EditorBackground);

        _preview = new ParticlePreview(this);

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,*,Auto"),
            ColumnDefinitions = new ColumnDefinitions("300,*")
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(8) };
        toolbar.Children.Add(MenuButton("New", (_, _) => { _sys = new ParticleSystem(); RebuildSidebar(); _preview.Reset(); }));
        toolbar.Children.Add(MenuButton("Open...", async (_, _) => await Open()));
        toolbar.Children.Add(MenuButton("Save...", async (_, _) => await Save()));
        Grid.SetColumnSpan(toolbar, 2);
        root.Children.Add(toolbar.At(row: 0, column: 0));

        _sidebar = new StackPanel { Margin = new Thickness(10), Spacing = 4 };
        root.Children.Add(new ScrollViewer { Content = _sidebar, Background = Brush(PanelBackground) }.At(row: 1, column: 0));
        RebuildSidebar();

        root.Children.Add(_preview.At(row: 1, column: 1));

        Grid.SetColumnSpan(_status, 2);
        root.Children.Add(_status.At(row: 2, column: 0));

        Content = root;
        _status.Text = "Drag to set emitter. Sliders control particle behavior.";
    }

    public ParticleSystem System => _sys;

    private readonly StackPanel _sidebar;

    private void RebuildSidebar()
    {
        _sidebar.Children.Clear();
        _sidebar.Children.Add(Text("Emission", TextPrimary, FontWeight.Bold));
        Slider("Spawn rate", _sys.SpawnRate, 0, 200, 1, v => _sys.SpawnRate = v);
        Slider("Lifetime", _sys.Lifetime, 0.1, 10, 0.1, v => _sys.Lifetime = v);

        _sidebar.Children.Add(Text("Motion", TextPrimary, FontWeight.Bold).WithMargin(0, 8, 0, 0));
        Slider("Speed min", _sys.SpeedMin, 0, 500, 1, v => _sys.SpeedMin = v);
        Slider("Speed max", _sys.SpeedMax, 0, 500, 1, v => _sys.SpeedMax = v);
        Slider("Angle start °", _sys.AngleStart, 0, 360, 1, v => _sys.AngleStart = v);
        Slider("Angle spread °", _sys.AngleSpread, 0, 360, 1, v => _sys.AngleSpread = v);
        Slider("Gravity X", _sys.GravityX, -300, 300, 1, v => _sys.GravityX = v);
        Slider("Gravity Y", _sys.GravityY, -300, 300, 1, v => _sys.GravityY = v);

        _sidebar.Children.Add(Text("Appearance", TextPrimary, FontWeight.Bold).WithMargin(0, 8, 0, 0));
        Slider("Size start", _sys.SizeStart, 1, 64, 1, v => _sys.SizeStart = v);
        Slider("Size end", _sys.SizeEnd, 1, 64, 1, v => _sys.SizeEnd = v);

        var c1 = new ColorPickerButton(_sys.ColorStart);
        c1.ColorChanged += v => _sys.ColorStart = v;
        _sidebar.Children.Add(LabelRow("Color start", c1));
        var c2 = new ColorPickerButton(_sys.ColorEnd);
        c2.ColorChanged += v => _sys.ColorEnd = v;
        _sidebar.Children.Add(LabelRow("Color end", c2));

        Slider("Alpha start", _sys.AlphaStart, 0, 1, 0.05, v => _sys.AlphaStart = v);
        Slider("Alpha end", _sys.AlphaEnd, 0, 1, 0.05, v => _sys.AlphaEnd = v);
    }

    private void Slider(string label, double current, double min, double max, double step, Action<double> apply)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*,40"), Height = 24 };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        var s = new Slider { Minimum = min, Maximum = max, Value = current, SmallChange = step, LargeChange = step * 10 };
        var lbl = new TextBlock { Text = current.ToString("0.##"), Foreground = Brush(TextSecondary), FontSize = 10, FontFamily = new FontFamily("Menlo"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
        s.ValueChanged += (_, _) => { apply(s.Value); lbl.Text = s.Value.ToString("0.##"); };
        grid.Children.Add(s.At(column: 1));
        grid.Children.Add(lbl.At(column: 2));
        _sidebar.Children.Add(grid);
    }

    private static Control LabelRow(string label, Control input)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("100,*"), Height = 28 };
        grid.Children.Add(new TextBlock { Text = label, Foreground = Brush(TextDim), FontSize = 11, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        grid.Children.Add(input.At(column: 1));
        return grid;
    }

    private async Task Save()
    {
        var f = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = _sys.Name + ".particle.json",
            FileTypeChoices = [new FilePickerFileType("Particle JSON") { Patterns = ["*.json"] }]
        });
        if (f is null) return;
        File.WriteAllText(f.Path.LocalPath, JsonSerializer.Serialize(_sys, new JsonSerializerOptions { WriteIndented = true }));
        _status.Text = "Saved " + Path.GetFileName(f.Path.LocalPath);
    }

    private async Task Open()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            FileTypeFilter = [new FilePickerFileType("Particle JSON") { Patterns = ["*.json"] }]
        });
        var f = files.FirstOrDefault();
        if (f is null) return;
        try
        {
            var json = File.ReadAllText(f.Path.LocalPath);
            var sys = JsonSerializer.Deserialize<ParticleSystem>(json);
            if (sys is null) return;
            _sys = sys;
            RebuildSidebar();
            _preview.Reset();
        }
        catch (Exception ex) { _status.Text = "Open failed: " + ex.Message; }
    }
}

internal sealed class ParticlePreview : Border
{
    private readonly ParticleEditor _host;
    private readonly List<Particle> _particles = new();
    private readonly DispatcherTimer _timer;
    private readonly Random _rng = new();
    private Point _emitter = new(400, 250);
    private double _spawnAcc;
    private readonly Inner _inner = new();

    public ParticlePreview(ParticleEditor host)
    {
        _host = host;
        Background = Avalonia.Media.Brush.Parse("#0e0f12");
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.Cross);
        Child = _inner;
        _inner.Particles = _particles;

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Tick(0.016);
        _timer.Start();
    }

    public void Reset()
    {
        _particles.Clear();
        _inner.InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        _emitter = e.GetPosition(this);
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            _emitter = e.GetPosition(this);
    }

    private void Tick(double dt)
    {
        var sys = _host.System;
        _spawnAcc += sys.SpawnRate * dt;
        while (_spawnAcc >= 1)
        {
            _spawnAcc -= 1;
            var angle = (sys.AngleStart + (_rng.NextDouble() - 0.5) * sys.AngleSpread) * Math.PI / 180.0;
            var speed = sys.SpeedMin + _rng.NextDouble() * Math.Max(0, sys.SpeedMax - sys.SpeedMin);
            _particles.Add(new Particle
            {
                X = _emitter.X,
                Y = _emitter.Y,
                Vx = Math.Cos(angle) * speed,
                Vy = Math.Sin(angle) * speed,
                Age = 0,
                Life = sys.Lifetime
            });
        }
        for (var i = _particles.Count - 1; i >= 0; i--)
        {
            var p = _particles[i];
            p.Age += dt;
            if (p.Age >= p.Life) { _particles.RemoveAt(i); continue; }
            p.Vx += sys.GravityX * dt;
            p.Vy += sys.GravityY * dt;
            p.X += p.Vx * dt;
            p.Y += p.Vy * dt;
        }
        _inner.SysSnapshot = sys;
        _inner.InvalidateVisual();
    }

    internal sealed class Particle
    {
        public double X, Y, Vx, Vy, Age, Life;
    }

    private sealed class Inner : Control
    {
        public List<Particle> Particles = new();
        public ParticleSystem? SysSnapshot;

        public override void Render(DrawingContext context)
        {
            if (SysSnapshot is null) return;
            Color cStart, cEnd;
            try { cStart = Color.Parse(SysSnapshot.ColorStart); } catch { cStart = Colors.White; }
            try { cEnd = Color.Parse(SysSnapshot.ColorEnd); } catch { cEnd = Colors.White; }

            foreach (var p in Particles)
            {
                var t = Math.Clamp(p.Age / Math.Max(0.001, p.Life), 0, 1);
                var size = Lerp(SysSnapshot.SizeStart, SysSnapshot.SizeEnd, t);
                var alpha = Lerp(SysSnapshot.AlphaStart, SysSnapshot.AlphaEnd, t);
                var c = LerpColor(cStart, cEnd, t);
                c = Color.FromArgb((byte)Math.Clamp(alpha * 255, 0, 255), c.R, c.G, c.B);
                context.FillRectangle(new SolidColorBrush(c), new Rect(p.X - size / 2, p.Y - size / 2, size, size));
            }
        }
        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
        private static Color LerpColor(Color a, Color b, double t) => Color.FromArgb(
            (byte)Lerp(a.A, b.A, t),
            (byte)Lerp(a.R, b.R, t),
            (byte)Lerp(a.G, b.G, t),
            (byte)Lerp(a.B, b.B, t));
    }
}
