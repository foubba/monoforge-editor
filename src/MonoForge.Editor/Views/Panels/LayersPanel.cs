using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views.Panels;

public sealed class LayersPanel : UserControl
{
    private readonly StackPanel _list = new() { Margin = new Thickness(8, 4), Spacing = 2 };
    private readonly HashSet<int> _hiddenLayers = new();
    private readonly HashSet<int> _lockedLayers = new();
    private SceneDocument _scene = new();

    public event Action? VisibilityChanged;
    public event Action? LockChanged;
    public IReadOnlySet<int> LockedLayers => _lockedLayers;

    public bool IsLayerLocked(int layer) => _lockedLayers.Contains(layer);
    public bool IsLayerHidden(int layer) => _hiddenLayers.Contains(layer);

    public LayersPanel()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("34,*"),
            Background = Brush(PanelBackground)
        };
        grid.Children.Add(Text("Layers", TextMuted).WithMargin(12, 10, 10, 0).At(row: 0));
        grid.Children.Add(new ScrollViewer { Content = _list, Background = Brush(PanelBackground) }.At(row: 1));
        Content = BorderBox(grid, BorderColor, 0, 0, 0, 1);
    }

    public void Update(SceneDocument scene)
    {
        _scene = scene;
        _list.Children.Clear();
        var layers = scene.Flatten().Select(o => o.Layer).Distinct().OrderByDescending(l => l).ToList();
        if (layers.Count == 0)
        {
            _list.Children.Add(Text("(no layers yet)", TextDim));
            return;
        }

        foreach (var layer in layers)
        {
            _list.Children.Add(BuildRow(layer));
        }
    }

    private Control BuildRow(int layer)
    {
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"),
            Background = Brush(PanelBackgroundAlt),
            Margin = new Thickness(0, 0, 0, 1),
            Height = 24
        };

        var visBtn = new Button
        {
            Content = _hiddenLayers.Contains(layer) ? "○" : "●",
            Background = Avalonia.Media.Brushes.Transparent,
            BorderBrush = Avalonia.Media.Brushes.Transparent,
            Foreground = Brush(_hiddenLayers.Contains(layer) ? TextDim : TextSecondary),
            FontSize = 14,
            Padding = new Thickness(6, 0),
            Width = 28
        };
        visBtn.Click += (_, _) =>
        {
            if (!_hiddenLayers.Add(layer)) _hiddenLayers.Remove(layer);
            ApplyVisibility();
            Update(_scene);
            VisibilityChanged?.Invoke();
        };
        row.Children.Add(visBtn.At(column: 0));

        var lockBtn = new Button
        {
            Content = _lockedLayers.Contains(layer) ? "🔒" : "🔓",
            Background = Avalonia.Media.Brushes.Transparent,
            BorderBrush = Avalonia.Media.Brushes.Transparent,
            FontSize = 11,
            Padding = new Thickness(2, 0),
            Width = 26
        };
        lockBtn.Click += (_, _) =>
        {
            if (!_lockedLayers.Add(layer)) _lockedLayers.Remove(layer);
            foreach (var obj in _scene.Flatten())
            {
                if (obj.Layer == layer) obj.Locked = _lockedLayers.Contains(layer);
            }
            Update(_scene);
            LockChanged?.Invoke();
        };
        row.Children.Add(lockBtn.At(column: 1));

        var count = _scene.Flatten().Count(o => o.Layer == layer);
        row.Children.Add(new TextBlock
        {
            Text = $"Layer {layer}",
            Foreground = Brush(TextSecondary),
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0)
        }.At(column: 2));
        row.Children.Add(new TextBlock
        {
            Text = count.ToString(),
            Foreground = Brush(TextDim),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0)
        }.At(column: 3));

        return row;
    }

    private void ApplyVisibility()
    {
        foreach (var obj in _scene.Flatten())
        {
            if (_hiddenLayers.Contains(obj.Layer)) obj.Visible = false;
            else if (!obj.Visible && !_hiddenLayers.Contains(obj.Layer)) obj.Visible = true;
        }
    }
}
