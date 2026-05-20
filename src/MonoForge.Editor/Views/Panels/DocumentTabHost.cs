using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views.Panels;

public sealed class DocumentTabHost : UserControl
{
    private readonly StackPanel _tabsRow = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 0
    };

    private readonly ContentControl _body = new();
    private readonly List<Document> _documents = new();
    private Document? _active;
    private Control? _emptyContent;

    /// <summary>Fires whenever the active document changes (including null when none remain).</summary>
    public event Action<Control?>? ActiveChanged;

    /// <summary>Optional placeholder shown in the body whenever no documents are open.</summary>
    public Control? EmptyContent
    {
        get => _emptyContent;
        set
        {
            _emptyContent = value;
            if (_active is null) _body.Content = value;
        }
    }

    public DocumentTabHost()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("32,*"),
            Background = Brush(EditorBackground)
        };

        _tabsRow.Background = Brush(MenuBackground);
        var scroll = new ScrollViewer
        {
            Content = _tabsRow,
            Background = Brush(MenuBackground),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        grid.Children.Add(scroll.At(row: 0));
        grid.Children.Add(_body.At(row: 1));
        Content = grid;
    }

    public void OpenOrFocus(string key, string title, Func<Control> contentFactory)
    {
        var existing = _documents.FirstOrDefault(d => d.Key == key);
        if (existing is not null)
        {
            Activate(existing);
            return;
        }

        var doc = new Document(key, title, contentFactory());
        _documents.Add(doc);
        RebuildTabs();
        Activate(doc);
    }

    public void Replace(string key, Func<Control> contentFactory)
    {
        var doc = _documents.FirstOrDefault(d => d.Key == key);
        if (doc is null)
        {
            return;
        }

        doc.Content = contentFactory();
        if (_active == doc)
        {
            _body.Content = doc.Content;
        }
    }

    public void Close(string key)
    {
        var doc = _documents.FirstOrDefault(d => d.Key == key);
        if (doc is null) return;

        var index = _documents.IndexOf(doc);
        _documents.Remove(doc);
        RebuildTabs();

        if (_active == doc)
        {
            var fallback = _documents.ElementAtOrDefault(Math.Max(0, index - 1));
            if (fallback is not null)
            {
                Activate(fallback);
            }
            else
            {
                _active = null;
                _body.Content = _emptyContent;
                ActiveChanged?.Invoke(null);
            }
        }
    }

    public string? ActiveKey => _active?.Key;

    public Control? ActiveContent => _active?.Content;

    /// <summary>Enumerate every open tab's content control. Used by callers that need
    /// to do something across all tabs (e.g. save-all before build).</summary>
    public IEnumerable<Control> OpenContents => _documents.Select(d => d.Content);

    public void SetDirty(string key, bool dirty)
    {
        var doc = _documents.FirstOrDefault(d => d.Key == key);
        if (doc is null) return;
        if (doc.IsDirty == dirty) return;
        doc.IsDirty = dirty;
        RebuildTabs();
    }

    private void Activate(Document doc)
    {
        _active = doc;
        _body.Content = doc.Content;
        RebuildTabs();
        ActiveChanged?.Invoke(doc.Content);
    }

    private void RebuildTabs()
    {
        _tabsRow.Children.Clear();
        foreach (var doc in _documents)
        {
            _tabsRow.Children.Add(BuildTab(doc));
        }
    }

    private int TabIndexAt(double x)
    {
        var cursor = 0.0;
        for (var i = 0; i < _tabsRow.Children.Count; i++)
        {
            var child = _tabsRow.Children[i];
            var width = child.Bounds.Width;
            if (x < cursor + width / 2) return i;
            cursor += width;
        }
        return _tabsRow.Children.Count;
    }

    private Document? _dragDoc;
    private Point _tabDragStart;
    private bool _tabDragging;

    private Control BuildTab(Document doc)
    {
        var active = doc == _active;
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto"),
            Background = Brush(active ? EditorBackground : MenuBackground),
            Margin = new Thickness(0, 0, 1, 0),
            Tag = doc.Key
        };

        grid.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
        {
            _dragDoc = doc;
            _tabDragStart = e.GetPosition(_tabsRow);
            _tabDragging = false;
        }, RoutingStrategies.Tunnel);

        grid.AddHandler(InputElement.PointerMovedEvent, (s, e) =>
        {
            if (_dragDoc is null) return;
            if (!e.GetCurrentPoint(_tabsRow).Properties.IsLeftButtonPressed)
            {
                _dragDoc = null;
                _tabDragging = false;
                return;
            }

            var dx = e.GetPosition(_tabsRow).X - _tabDragStart.X;
            if (!_tabDragging && Math.Abs(dx) > 12) _tabDragging = true;
            if (!_tabDragging) return;

            var targetIndex = TabIndexAt(e.GetPosition(_tabsRow).X);
            var currentIndex = _documents.IndexOf(_dragDoc);
            if (targetIndex >= 0 && targetIndex != currentIndex)
            {
                _documents.Remove(_dragDoc);
                _documents.Insert(Math.Clamp(targetIndex, 0, _documents.Count), _dragDoc);
                RebuildTabs();
            }
        }, RoutingStrategies.Tunnel);

        grid.AddHandler(InputElement.PointerReleasedEvent, (s, e) =>
        {
            _dragDoc = null;
            _tabDragging = false;
        }, RoutingStrategies.Tunnel);

        var dot = doc.IsDirty ? " ●" : "";
        var label = new Button
        {
            Content = "  " + doc.Title + dot + "  ",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(active ? TextPrimary : TextDim),
            FontSize = 12,
            Padding = new Thickness(8, 6),
            Tag = doc.Key,
            VerticalAlignment = VerticalAlignment.Center
        }.OnClick((sender, _) =>
        {
            if (sender is Button { Tag: string key })
            {
                var found = _documents.FirstOrDefault(d => d.Key == key);
                if (found is not null) Activate(found);
            }
        });
        grid.Children.Add(label.At(column: 0));

        var close = new Button
        {
            Content = "×",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(TextDim),
            FontSize = 14,
            Padding = new Thickness(4, 2, 8, 2),
            Tag = doc.Key,
            VerticalAlignment = VerticalAlignment.Center
        }.OnClick((sender, _) =>
        {
            if (sender is Button { Tag: string key }) Close(key);
        });
        grid.Children.Add(close.At(column: 1));

        if (active)
        {
            return new Border
            {
                Child = grid,
                BorderBrush = Brush(Accent),
                BorderThickness = new Thickness(0, 0, 0, 2)
            };
        }

        return grid;
    }

    private sealed class Document
    {
        public Document(string key, string title, Control content)
        {
            Key = key;
            Title = title;
            Content = content;
        }

        public string Key { get; }
        public string Title { get; }
        public Control Content { get; set; }
        public bool IsDirty { get; set; }
    }
}
