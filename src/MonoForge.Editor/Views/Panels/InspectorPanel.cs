using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views.Panels;

public sealed class InspectorPanel : UserControl
{
    private readonly StackPanel _items = new();

    public event Action? PropertyCommitted;
    public event Action? PropertyChanging;
    public Func<string?, Task<string?>>? PickTexture { get; set; }
    public Func<Models.SceneObject, Task<bool>>? OpenNineSlice { get; set; }

    public InspectorPanel()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("34,*"),
            Background = Brush(PanelBackground)
        };
        grid.Children.Add(Text("Properties", TextMuted).WithMargin(12, 10, 10, 0).At(row: 0));
        _items.Margin = new Thickness(10);
        _items.Spacing = 8;
        var scroll = new ScrollViewer { Content = _items, Background = Brush(PanelBackground) };
        grid.Children.Add(scroll.At(row: 1));
        Content = BorderBox(grid, BorderColor, 0, 0, 0, 0);
    }

    public void Update(SceneObject? selected)
    {
        _items.Children.Clear();
        if (selected is null)
        {
            _items.Children.Add(Text("Select an object to inspect it.", TextDim));
            return;
        }

        SectionHeader("General");
        Row("Name", selected.Name, v => selected.Name = v);
        Row("Type", selected.Type, v => selected.Type = v);

        SectionHeader("Transform");
        Row("X", selected.X.ToString("0"), v => selected.X = ParseDouble(v, selected.X));
        Row("Y", selected.Y.ToString("0"), v => selected.Y = ParseDouble(v, selected.Y));
        Row("Width", selected.Width.ToString("0"), v => selected.Width = ParseDouble(v, selected.Width));
        Row("Height", selected.Height.ToString("0"), v => selected.Height = ParseDouble(v, selected.Height));
        Row("Rotation", selected.Rotation.ToString("0.##"), v => selected.Rotation = ParseDouble(v, selected.Rotation));
        Row("PivotX", selected.PivotX.ToString("0.##"), v => selected.PivotX = ParseDouble(v, selected.PivotX));
        Row("PivotY", selected.PivotY.ToString("0.##"), v => selected.PivotY = ParseDouble(v, selected.PivotY));
        FlipRow(selected);

        SectionHeader("Sprite");
        ColorRow("Color", selected.Color, v => selected.Color = v);
        TextureRow("Texture", selected);
        Row("Layer", selected.Layer.ToString(), v => selected.Layer = ParseInt(v, selected.Layer));

        if (!string.IsNullOrEmpty(selected.TexturePath))
        {
            var sliceBtn = new Button
            {
                Content = $"9-Slice…  ({selected.SliceLeft},{selected.SliceTop},{selected.SliceRight},{selected.SliceBottom})",
                Background = Brush(FilterBackground),
                BorderBrush = Brush(BorderColor),
                Foreground = Brush(TextSecondary),
                FontSize = 11,
                Padding = new Thickness(8, 4),
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch
            };
            sliceBtn.Click += async (_, _) =>
            {
                if (OpenNineSlice is null) return;
                PropertyChanging?.Invoke();
                await OpenNineSlice(selected);
                PropertyCommitted?.Invoke();
            };
            _items.Children.Add(sliceBtn);
        }

        foreach (var c in selected.Components.ToList())
        {
            SectionHeader($"{c.Kind}");
            Row("Source", c.Source, v => c.Source = v);
            foreach (var prop in c.Properties.Keys.ToList())
            {
                var key = prop;
                Row(key, c.Properties[key], v => c.Properties[key] = v);
            }
            var removeRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            var removeBtn = new Button
            {
                Content = "Remove",
                Background = Avalonia.Media.Brushes.Transparent,
                BorderBrush = Brush(BorderColor),
                Foreground = Brush(TextDim),
                FontSize = 11,
                Padding = new Thickness(8, 2)
            };
            removeBtn.Click += (_, _) =>
            {
                PropertyChanging?.Invoke();
                selected.Components.Remove(c);
                PropertyCommitted?.Invoke();
            };
            removeRow.Children.Add(removeBtn.At(column: 1));
            _items.Children.Add(removeRow);
        }

        AddComponentRow(selected);
    }

    private void AddComponentRow(Models.SceneObject selected)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(0, 12, 0, 0)
        };
        var combo = new ComboBox
        {
            ItemsSource = new[] { "Script", "Collision", "Animation", "Audio", "Particles", "Camera", "Custom" },
            PlaceholderText = "Add Component...",
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            FontSize = 12
        };
        combo.SelectionChanged += (_, _) =>
        {
            if (combo.SelectedItem is not string kind) return;
            PropertyChanging?.Invoke();
            selected.Components.Add(NewComponent(kind));
            combo.SelectedItem = null;
            PropertyCommitted?.Invoke();
        };
        row.Children.Add(combo.At(column: 0));
        _items.Children.Add(row);
    }

    private static Models.ComponentData NewComponent(string kind)
    {
        var c = new Models.ComponentData { Kind = kind };
        switch (kind)
        {
            case "Script":
                c.Properties["Class"] = "MyScript";
                break;
            case "Collision":
                c.Properties["Shape"] = "Box";
                c.Properties["Group"] = "default";
                c.Properties["IsTrigger"] = "false";
                break;
            case "Animation":
                c.Source = ""; // path to .anim.json
                c.Properties["Speed"] = "1.0";
                c.Properties["Loop"] = "true";
                c.Properties["AutoPlay"] = "true";
                break;
            case "Audio":
                c.Properties["Clip"] = "";
                c.Properties["Volume"] = "1.0";
                c.Properties["Loop"] = "false";
                break;
            case "Particles":
                c.Source = "";
                c.Properties["AutoPlay"] = "true";
                break;
            case "Camera":
                c.Properties["Width"] = "1280";
                c.Properties["Height"] = "720";
                c.Properties["Zoom"] = "1.0";
                c.Properties["IsActive"] = "true";
                break;
        }
        return c;
    }

    private void TextureRow(string label, Models.SceneObject selected)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("82,*,Auto"),
            Height = 30
        };
        row.Children.Add(Text(label, TextDim).At(column: 0));
        var input = new TextBox
        {
            Text = selected.TexturePath,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            BorderBrush = Brush(BorderColor),
            Padding = new Thickness(7, 3)
        };
        input.LostFocus += (_, _) =>
        {
            PropertyChanging?.Invoke();
            selected.TexturePath = input.Text ?? "";
            PropertyCommitted?.Invoke();
        };
        row.Children.Add(input.At(column: 1));
        var pickBtn = new Button
        {
            Content = "…",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(BorderColor),
            Foreground = Brush(TextSecondary),
            Padding = new Thickness(8, 2),
            Margin = new Thickness(4, 0, 0, 0)
        };
        pickBtn.Click += async (_, _) =>
        {
            if (PickTexture is null) return;
            var picked = await PickTexture(selected.TexturePath);
            if (picked is null) return;
            PropertyChanging?.Invoke();
            selected.TexturePath = picked;
            PropertyCommitted?.Invoke();
        };
        row.Children.Add(pickBtn.At(column: 2));
        _items.Children.Add(row);
    }

    private void FlipRow(Models.SceneObject selected)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("82,*,*"), Height = 30 };
        row.Children.Add(Text("Flip", TextDim).At(column: 0));
        var fx = new CheckBox { Content = "X", Foreground = Brush(TextSecondary), IsChecked = selected.FlipX };
        fx.IsCheckedChanged += (_, _) => { PropertyChanging?.Invoke(); selected.FlipX = fx.IsChecked == true; PropertyCommitted?.Invoke(); };
        var fy = new CheckBox { Content = "Y", Foreground = Brush(TextSecondary), IsChecked = selected.FlipY };
        fy.IsCheckedChanged += (_, _) => { PropertyChanging?.Invoke(); selected.FlipY = fy.IsChecked == true; PropertyCommitted?.Invoke(); };
        row.Children.Add(fx.At(column: 1));
        row.Children.Add(fy.At(column: 2));
        _items.Children.Add(row);
    }

    private void ColorRow(string label, string value, Action<string> apply)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("82,*"),
            Height = 30
        };
        row.Children.Add(Text(label, TextDim).At(column: 0));
        var picker = new ColorPickerButton(value);
        picker.ColorChanged += newColor =>
        {
            PropertyChanging?.Invoke();
            apply(newColor);
            PropertyCommitted?.Invoke();
        };
        row.Children.Add(picker.At(column: 1));
        _items.Children.Add(row);
    }

    private void SectionHeader(string title)
    {
        _items.Children.Add(new Border
        {
            Background = Brush(MenuBackground),
            Padding = new Thickness(8, 4, 8, 4),
            Margin = new Thickness(-10, 8, -10, 4),
            Child = new TextBlock
            {
                Text = title.ToUpperInvariant(),
                Foreground = Brush(TextMuted),
                FontSize = 10,
                FontWeight = Avalonia.Media.FontWeight.SemiBold,
                FontFamily = Avalonia.Media.FontFamily.Parse(UiFont)
            }
        });
    }

    private void Row(string label, string value, Action<string> apply)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("82,*"),
            Height = 30
        };
        row.Children.Add(Text(label, TextDim).At(column: 0));
        var input = new TextBox
        {
            Text = value,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            BorderBrush = Brush(BorderColor),
            Padding = new Thickness(7, 3)
        };
        input.GotFocus += (_, _) => PropertyChanging?.Invoke();
        input.LostFocus += (_, _) => Commit(input.Text, apply);
        input.KeyUp += (_, args) =>
        {
            if (args.Key == Key.Enter)
            {
                Commit(input.Text, apply);
            }
        };
        row.Children.Add(input.At(column: 1));
        _items.Children.Add(row);
    }

    private void Commit(string? value, Action<string> apply)
    {
        apply(value ?? "");
        PropertyCommitted?.Invoke();
    }

    private static double ParseDouble(string value, double fallback)
    {
        return double.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ParseInt(string value, int fallback)
    {
        return int.TryParse(value, out var parsed) ? parsed : fallback;
    }
}
