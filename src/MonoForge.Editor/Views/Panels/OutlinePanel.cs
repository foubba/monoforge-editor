using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views.Panels;

public sealed class OutlinePanel : UserControl
{
    private readonly StackPanel _items = new();
    private readonly TextBox _filter = new();
    private SceneDocument _scene = new();
    private string _filterText = "";

    public string? SelectedId { get; set; }
    public event Action<string>? SelectionRequested;
    public event Action? DuplicateRequested;
    public event Action? DeleteRequested;
    public event Action<string>? VisibilityToggled;
    public event Action<string>? LockToggled;
    public event Action<string, string?>? ReparentRequested; // (childId, newParentId|null)

    private string? _dragId;
    private Point _dragStart;
    private bool _dragging;

    public OutlinePanel()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("34,28,*,42"),
            Background = Brush(PanelBackground)
        };

        var header = Text("Outline", TextMuted).WithMargin(12, 10, 10, 0);
        header.Tag = "__root__";

        _filter.Watermark = "Filter…";
        _filter.Background = Brush(InputBackground);
        _filter.Foreground = Brush(TextSecondary);
        _filter.BorderBrush = Brush(BorderColor);
        _filter.FontSize = 11;
        _filter.Padding = new Thickness(6, 2);
        _filter.Margin = new Thickness(8, 0, 8, 2);
        _filter.TextChanged += (_, _) => { _filterText = _filter.Text ?? ""; Update(_scene, SelectedId); };
        header.PointerReleased += (_, _) =>
        {
            if (_dragging && _dragId is not null)
            {
                ReparentRequested?.Invoke(_dragId, null);
            }
            _dragging = false;
            _dragId = null;
        };
        grid.Children.Add(header.At(row: 0));
        grid.Children.Add(_filter.At(row: 1));

        _items.Margin = new Thickness(8);
        _items.Spacing = 2;
        var scroll = new ScrollViewer { Content = _items, Background = Brush(PanelBackground) };
        grid.Children.Add(scroll.At(row: 2));

        var actions = RowStack(PanelBackgroundAlt);
        actions.Children.Add(MenuButton("Duplicate", (_, _) => DuplicateRequested?.Invoke()));
        actions.Children.Add(MenuButton("Delete", (_, _) => DeleteRequested?.Invoke()));
        grid.Children.Add(actions.At(row: 3));

        Content = BorderBox(grid, BorderColor, 0, 0, 0, 1);
    }

    public void Update(SceneDocument scene, string? selectedId)
    {
        _scene = scene;
        SelectedId = selectedId;
        _items.Children.Clear();
        foreach (var obj in scene.Objects)
        {
            AppendObject(obj, 0);
        }
    }

    private bool DescendantMatches(SceneObject obj)
    {
        if (obj.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase)) return true;
        return obj.Children.Any(DescendantMatches);
    }

    private void AppendObject(SceneObject obj, int depth)
    {
        var matches = string.IsNullOrEmpty(_filterText)
            || obj.Name.Contains(_filterText, StringComparison.OrdinalIgnoreCase);
        var anyDescendantMatches = obj.Children.Any(c => DescendantMatches(c));
        if (!matches && !anyDescendantMatches) return;

        var selected = obj.Id == SelectedId;
        var row = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"),
            Background = Brush(selected ? Selection : PanelBackgroundAlt),
            Margin = new Thickness(depth * 12, 0, 0, 0),
            Tag = obj.Id
        };
        var label = new Button
        {
            Content = (obj.Children.Count > 0 ? "▾ " : "  ") + obj.Name,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = Brushes.Transparent,
            Foreground = Brush(selected ? TextPrimary : TextDim),
            BorderBrush = Brushes.Transparent,
            Tag = obj.Id
        }.OnClick((sender, _) =>
        {
            if (sender is Button { Tag: string id })
            {
                SelectionRequested?.Invoke(id);
            }
        });
        row.Children.Add(label.At(column: 0));

        var toggle = new Button
        {
            Content = obj.Visible ? "show" : "hide",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(obj.Visible ? TextDim : TextFaint),
            FontSize = 11,
            Tag = obj.Id
        }.OnClick((sender, _) =>
        {
            if (sender is Button { Tag: string id })
            {
                VisibilityToggled?.Invoke(id);
            }
        });
        row.Children.Add(toggle.At(column: 1));

        var lockBtn = new Button
        {
            Content = obj.Locked ? "🔒" : "🔓",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            FontSize = 10,
            Padding = new Thickness(2, 0),
            Tag = obj.Id
        }.OnClick((sender, _) =>
        {
            if (sender is Button { Tag: string id }) LockToggled?.Invoke(id);
        });
        row.Children.Add(lockBtn.At(column: 2));

        // Drag & drop reparenting
        row.PointerPressed += (s, e) =>
        {
            _dragId = obj.Id;
            _dragStart = e.GetPosition(_items);
            _dragging = false;
        };
        row.PointerMoved += (s, e) =>
        {
            if (_dragId is null) return;
            if (!e.GetCurrentPoint(_items).Properties.IsLeftButtonPressed) return;
            var dp = e.GetPosition(_items) - _dragStart;
            if (!_dragging && (Math.Abs(dp.X) + Math.Abs(dp.Y) > 6)) _dragging = true;
        };
        row.PointerReleased += (s, e) =>
        {
            if (_dragging && _dragId is not null && _dragId != obj.Id)
            {
                ReparentRequested?.Invoke(_dragId, obj.Id);
            }
            _dragId = null;
            _dragging = false;
        };
        _items.Children.Add(row);

        foreach (var child in obj.Children)
        {
            child.Parent = obj;
            AppendObject(child, depth + 1);
        }
    }
}
