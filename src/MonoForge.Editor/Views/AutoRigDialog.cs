using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class AutoRigDialog : Window
{
    private readonly Dictionary<string, Vector3> _landmarks = new();
    private readonly Dictionary<string, TextBox[]> _rows = new();
    private readonly TextBlock _status = new() { Foreground = Brush(TextMuted), FontSize = 12, Padding = new Thickness(10, 4) };

    public AutoRigDialog((float X, float Y, float Z) suggestedCenter)
    {
        Title = "AutoRig — Humanoid Template";
        Width = 540; Height = 640;
        Background = Brush(MenuBackground);
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var intro = new TextBlock
        {
            Text = "Enter approximate world-space positions for each landmark. The rig will be assembled " +
                   "into a Mixamo-style humanoid skeleton and exported as JSON. You can grab coordinates " +
                   "from the 3D viewer by hovering joints.",
            Foreground = Brush(TextDim),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(12, 12, 12, 4)
        };

        var list = new StackPanel { Margin = new Thickness(12, 0), Spacing = 4 };
        foreach (var name in AutoRig.LandmarkNames)
        {
            list.Children.Add(BuildRow(name, suggestedCenter));
        }

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(12),
            Spacing = 6
        };
        actions.Children.Add(MenuButton("Cancel", (_, _) => Close()));
        actions.Children.Add(PrimaryButton("Generate Rig", async (_, _) => await Generate()));

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto") };
        root.Children.Add(intro.At(row: 0));
        root.Children.Add(new ScrollViewer { Content = list }.At(row: 1));
        root.Children.Add(_status.At(row: 2));
        root.Children.Add(actions.At(row: 3));
        Content = root;
    }

    private Control BuildRow(string name, (float X, float Y, float Z) defaults)
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("130,*,*,*"),
            ColumnSpacing = 4,
            Height = 28
        };
        grid.Children.Add(new TextBlock { Text = name, Foreground = Brush(TextSecondary), FontSize = 12, VerticalAlignment = VerticalAlignment.Center }.At(column: 0));
        var x = MakeInput(defaults.X);
        var y = MakeInput(defaults.Y);
        var z = MakeInput(defaults.Z);
        grid.Children.Add(x.At(column: 1));
        grid.Children.Add(y.At(column: 2));
        grid.Children.Add(z.At(column: 3));
        _rows[name] = new[] { x, y, z };
        return grid;
    }

    private static TextBox MakeInput(float val) => new()
    {
        Text = val.ToString("0.##"),
        Background = Brush(InputBackground),
        Foreground = Brush(TextSecondary),
        BorderBrush = Brush(BorderColor),
        FontFamily = new FontFamily("Menlo"),
        FontSize = 11,
        Padding = new Thickness(6, 2)
    };

    private async Task Generate()
    {
        _landmarks.Clear();
        foreach (var (name, boxes) in _rows)
        {
            float ParseVal(TextBox b) => float.TryParse(b.Text, out var v) ? v : 0;
            _landmarks[name] = new Vector3(ParseVal(boxes[0]), ParseVal(boxes[1]), ParseVal(boxes[2]));
        }

        var rig = AutoRig.BuildBiped(_landmarks);

        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "rig.json",
            FileTypeChoices = [new FilePickerFileType("Rig JSON") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        AutoRig.Save(rig, file.Path.LocalPath);
        _status.Text = $"Saved {rig.Joints.Count} joints to {file.Name}";
    }
}
