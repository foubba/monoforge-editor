using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;

namespace MonoForge.Editor.Views;

public sealed class ColorPickerButton : UserControl
{
    private readonly Button _swatch;
    private readonly TextBox _hexBox;
    private readonly Popup _popup;
    private string _value = "#ffffff";
    public event Action<string>? ColorChanged;

    public string Color
    {
        get => _value;
        set
        {
            if (_value == value) return;
            _value = NormalizeHex(value);
            Sync();
        }
    }

    public ColorPickerButton(string initial)
    {
        _value = NormalizeHex(initial);

        _swatch = new Button
        {
            Width = 28,
            Height = 22,
            BorderBrush = Avalonia.Media.Brush.Parse("#343943"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(0)
        };

        _hexBox = new TextBox
        {
            Width = 96,
            Background = Avalonia.Media.Brush.Parse("#16191f"),
            Foreground = Avalonia.Media.Brush.Parse("#d7dce5"),
            BorderBrush = Avalonia.Media.Brush.Parse("#343943"),
            Padding = new Thickness(6, 2),
            FontFamily = new FontFamily("Menlo")
        };

        _popup = BuildPopup();

        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        row.Children.Add(_swatch);
        row.Children.Add(_hexBox);
        Content = row;

        _swatch.Click += (_, _) =>
        {
            _popup.PlacementTarget = _swatch;
            _popup.IsOpen = !_popup.IsOpen;
        };

        _hexBox.LostFocus += (_, _) => CommitHex(_hexBox.Text);
        _hexBox.KeyDown += (_, e) =>
        {
            if (e.Key == Avalonia.Input.Key.Enter) CommitHex(_hexBox.Text);
        };

        Sync();
    }

    private void CommitHex(string? text)
    {
        var norm = NormalizeHex(text ?? "");
        if (norm == _value) return;
        _value = norm;
        Sync();
        ColorChanged?.Invoke(_value);
    }

    private void Sync()
    {
        try
        {
            _swatch.Background = new SolidColorBrush(Avalonia.Media.Color.Parse(_value));
        }
        catch
        {
            _swatch.Background = Avalonia.Media.Brush.Parse("#65a7ff");
        }
        _hexBox.Text = _value;
    }

    private Popup BuildPopup()
    {
        var panel = new StackPanel
        {
            Background = Avalonia.Media.Brush.Parse("#24272b"),
            Margin = new Thickness(0),
            Spacing = 6,
            Width = 220
        };

        // Preset palette
        var palette = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*,*,*"),
            RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
            Margin = new Thickness(8, 8, 8, 0)
        };
        var presets = new[]
        {
            "#ffffff", "#000000", "#65a7ff", "#7bd88f", "#ffd166", "#ff6b6b", "#d7dce5", "#c7a76c",
            "#42b7ff", "#42c21a", "#f0c400", "#ff8c42", "#a76cff", "#5a5e66", "#1f2427", "#2b2d32",
            "#fb7185", "#f97316", "#facc15", "#84cc16", "#22c55e", "#06b6d4", "#3b82f6", "#a855f7"
        };
        for (var i = 0; i < presets.Length; i++)
        {
            var color = presets[i];
            var swatch = new Button
            {
                Width = 22,
                Height = 22,
                Background = Avalonia.Media.Brush.Parse(color),
                BorderBrush = Avalonia.Media.Brush.Parse("#343943"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(1),
                Padding = new Thickness(0),
                Tag = color
            };
            swatch.Click += (s, _) =>
            {
                if (s is Button { Tag: string c })
                {
                    _value = c;
                    Sync();
                    ColorChanged?.Invoke(_value);
                    _popup.IsOpen = false;
                }
            };
            Grid.SetColumn(swatch, i % 8);
            Grid.SetRow(swatch, i / 8);
            palette.Children.Add(swatch);
        }
        panel.Children.Add(palette);

        // RGB sliders
        var rSlider = MakeChannelRow("R", ref _value, 0);
        var gSlider = MakeChannelRow("G", ref _value, 1);
        var bSlider = MakeChannelRow("B", ref _value, 2);
        panel.Children.Add(rSlider.Container);
        panel.Children.Add(gSlider.Container);
        panel.Children.Add(bSlider.Container);

        void Refresh()
        {
            var (r, g, b) = ParseRgb(_value);
            rSlider.Slider.Value = r;
            gSlider.Slider.Value = g;
            bSlider.Slider.Value = b;
        }

        void OnSlider()
        {
            var r = (int)rSlider.Slider.Value;
            var g = (int)gSlider.Slider.Value;
            var b = (int)bSlider.Slider.Value;
            _value = $"#{r:x2}{g:x2}{b:x2}";
            Sync();
            ColorChanged?.Invoke(_value);
        }

        rSlider.Slider.ValueChanged += (_, _) => OnSlider();
        gSlider.Slider.ValueChanged += (_, _) => OnSlider();
        bSlider.Slider.ValueChanged += (_, _) => OnSlider();

        Refresh();

        return new Popup
        {
            Child = new Border
            {
                Child = panel,
                Background = Avalonia.Media.Brush.Parse("#24272b"),
                BorderBrush = Avalonia.Media.Brush.Parse("#343943"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(0, 0, 0, 8)
            },
            Placement = PlacementMode.BottomEdgeAlignedLeft,
            IsLightDismissEnabled = true,
            WindowManagerAddShadowHint = false
        };
    }

    private static (Grid Container, Slider Slider) MakeChannelRow(string label, ref string _, int _idx)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("14,*,30"),
            Margin = new Thickness(8, 0, 8, 0),
            Height = 22
        };
        grid.Children.Add(new TextBlock
        {
            Text = label,
            Foreground = Avalonia.Media.Brush.Parse("#8e96a3"),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center
        });
        var s = new Slider
        {
            Minimum = 0,
            Maximum = 255,
            SmallChange = 1,
            LargeChange = 16,
            Padding = new Thickness(0)
        };
        Grid.SetColumn(s, 1);
        grid.Children.Add(s);
        var valLabel = new TextBlock
        {
            Foreground = Avalonia.Media.Brush.Parse("#d7dce5"),
            FontSize = 10,
            FontFamily = new FontFamily("Menlo"),
            VerticalAlignment = VerticalAlignment.Center,
            TextAlignment = TextAlignment.Right
        };
        Grid.SetColumn(valLabel, 2);
        grid.Children.Add(valLabel);
        s.ValueChanged += (_, _) => valLabel.Text = ((int)s.Value).ToString();
        return (grid, s);
    }

    private static string NormalizeHex(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "#ffffff";
        var s = input.Trim();
        if (!s.StartsWith('#')) s = "#" + s;
        if (s.Length == 4) s = $"#{s[1]}{s[1]}{s[2]}{s[2]}{s[3]}{s[3]}";
        if (s.Length != 7) return "#ffffff";
        return s.ToLowerInvariant();
    }

    private static (int R, int G, int B) ParseRgb(string hex)
    {
        try
        {
            var c = Avalonia.Media.Color.Parse(hex);
            return (c.R, c.G, c.B);
        }
        catch
        {
            return (255, 255, 255);
        }
    }
}
