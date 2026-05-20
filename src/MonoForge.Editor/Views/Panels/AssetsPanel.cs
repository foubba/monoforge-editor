using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;
using MonoForge.Editor.Models;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views.Panels;

public sealed class AssetsPanel : UserControl
{
    private static readonly HashSet<string> IgnoredDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", ".git", ".vs", ".idea", ".vscode", "node_modules", "packages"
    };

    private readonly TreeView _tree = new();
    private FileSystemWatcher? _watcher;
    private string? _rootPath;
    private DateTime _lastRefresh = DateTime.MinValue;
    private static Dictionary<string, string> _gitStatus = new();

    public event Action<ProjectTreeNode>? NodeSelected;
    public event Action? OpenProjectRequested;
    public event Action<string>? Log;

    public AssetsPanel()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("34,*"),
            Background = Brush(PanelBackground)
        };

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush(PanelBackground)
        };
        header.Children.Add(Text("Assets", TextMuted).WithMargin(12, 10, 10, 0).At(column: 0));
        header.Children.Add(MenuButton("Open Project", (_, _) => OpenProjectRequested?.Invoke()).At(column: 1));
        grid.Children.Add(header.At(row: 0));

        _tree.Background = Brush(PanelBackground);
        _tree.Foreground = Brush(TextMuted);
        _tree.FontFamily = FontFamily.Parse(UiFont);
        _tree.FontSize = 11;
        _tree.Padding = new Thickness(0, 2, 0, 0);
        _tree.ClipToBounds = true;
        ScrollViewer.SetHorizontalScrollBarVisibility(_tree, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        ScrollViewer.SetVerticalScrollBarVisibility(_tree, Avalonia.Controls.Primitives.ScrollBarVisibility.Auto);
        _tree.SelectionChanged += (_, _) =>
        {
            if (_tree.SelectedItem is TreeViewItem { Tag: ProjectTreeNode node })
            {
                NodeSelected?.Invoke(node);
            }
        };

        // Drag&drop: start dragging asset paths when user mouses down + drags.
        _tree.AddHandler(InputElement.PointerPressedEvent, OnTreePointerPressed, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tree.AddHandler(InputElement.PointerMovedEvent, OnTreePointerMoved, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _tree.AddHandler(InputElement.PointerReleasedEvent, OnTreePointerReleased, Avalonia.Interactivity.RoutingStrategies.Tunnel);

        var treeScroll = new ScrollViewer
        {
            Content = _tree,
            Background = Brush(PanelBackground),
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto
        };
        grid.Children.Add(treeScroll.At(row: 1));

        Content = BorderBox(grid, BorderColor, 0, 0, 1, 0);
        ShowEmpty();
    }

    private Point? _dragStart;
    private ProjectTreeNode? _dragNode;
    public const string AssetPathDataFormat = "monoforge.asset-path";

    private void OnTreePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.Source is Visual v && FindNode(v) is { } node && !node.IsDirectory)
        {
            _dragStart = e.GetPosition(_tree);
            _dragNode = node;
        }
        else
        {
            _dragStart = null;
            _dragNode = null;
        }
    }

    private async void OnTreePointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragStart is null || _dragNode is null) return;
        if (!e.GetCurrentPoint(_tree).Properties.IsLeftButtonPressed) return;

        var pos = e.GetPosition(_tree);
        if (Math.Abs(pos.X - _dragStart.Value.X) + Math.Abs(pos.Y - _dragStart.Value.Y) < 6) return;

        var node = _dragNode;
        _dragStart = null;
        _dragNode = null;
        try
        {
            var data = new DataObject();
            data.Set(AssetPathDataFormat, node.FullPath);
            data.Set(DataFormats.Text, node.FullPath);
            await DragDrop.DoDragDrop(e, data, DragDropEffects.Copy);
        }
        catch { /* ignored */ }
    }

    private void OnTreePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _dragStart = null;
        _dragNode = null;
    }

    private static ProjectTreeNode? FindNode(Visual? v)
    {
        while (v is not null)
        {
            if (v is TreeViewItem { Tag: ProjectTreeNode node }) return node;
            v = v.GetVisualParent() as Visual;
        }
        return null;
    }

    public void ShowEmpty()
    {
        _tree.ItemsSource = new[] { BuildItem(new ProjectTreeNode { Name = "No project open", IsDirectory = true }) };
    }

    public void LoadProject(string rootPath)
    {
        _rootPath = rootPath;
        _gitStatus = MonoForge.Editor.Services.GitStatus.Read(rootPath);
        var root = BuildNode(new DirectoryInfo(rootPath), 0);
        _tree.ItemsSource = new[] { BuildItem(root) };
        StartWatcher(rootPath);
    }

    private void StartWatcher(string rootPath)
    {
        _watcher?.Dispose();
        try
        {
            _watcher = new FileSystemWatcher(rootPath)
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, _) => ScheduleRefresh();
            _watcher.Deleted += (_, _) => ScheduleRefresh();
            _watcher.Renamed += (_, _) => ScheduleRefresh();
        }
        catch (Exception ex)
        {
            Log?.Invoke("Watcher disabled: " + ex.Message);
        }
    }

    private void ScheduleRefresh()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastRefresh).TotalMilliseconds < 350) return;
        _lastRefresh = now;
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_rootPath is not null) LoadProject(_rootPath);
        }, Avalonia.Threading.DispatcherPriority.Background);
    }

    private ProjectTreeNode BuildNode(DirectoryInfo directory, int depth)
    {
        var node = new ProjectTreeNode
        {
            Name = directory.Name,
            FullPath = directory.FullName,
            IsDirectory = true
        };

        if (depth >= 8)
        {
            return node;
        }

        try
        {
            foreach (var child in directory.EnumerateDirectories().Where(IncludeDir).OrderBy(d => d.Name))
            {
                node.Children.Add(BuildNode(child, depth + 1));
            }

            foreach (var file in directory.EnumerateFiles().Where(IncludeFile).OrderBy(f => f.Name))
            {
                node.Children.Add(new ProjectTreeNode
                {
                    Name = file.Name,
                    FullPath = file.FullName,
                    IsDirectory = false
                });
            }
        }
        catch (UnauthorizedAccessException)
        {
            Log?.Invoke("Skipped protected: " + directory.FullName);
        }
        catch (IOException ex)
        {
            Log?.Invoke("Could not read: " + ex.Message);
        }

        return node;
    }

    private static bool IncludeDir(DirectoryInfo d)
    {
        var hidden = d.Name.StartsWith('.');
        return !hidden && !IgnoredDirectories.Contains(d.Name);
    }

    private static bool IncludeFile(FileInfo f)
    {
        return !f.Name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)
            && !f.Name.EndsWith(".user", StringComparison.OrdinalIgnoreCase)
            && !f.Name.EndsWith(".suo", StringComparison.OrdinalIgnoreCase);
    }

    public event Action<ProjectTreeNode>? RenameRequested;
    public event Action<ProjectTreeNode>? DeleteRequested;
    public event Action<ProjectTreeNode>? RevealRequested;
    public event Action<ProjectTreeNode>? FindUsagesRequested;

    private TreeViewItem BuildItem(ProjectTreeNode node)
    {
        var item = new TreeViewItem
        {
            Header = Label(node),
            Tag = node,
            IsExpanded = node.IsDirectory,
            MinHeight = 18,
            Padding = new Thickness(0)
        };

        var menu = new ContextMenu();
        var open = new MenuItem { Header = "Open" };
        open.Click += (_, _) => NodeSelected?.Invoke(node);
        var rename = new MenuItem { Header = "Rename..." };
        rename.Click += (_, _) => RenameRequested?.Invoke(node);
        var del = new MenuItem { Header = "Delete" };
        del.Click += (_, _) => DeleteRequested?.Invoke(node);
        var reveal = new MenuItem { Header = "Reveal in Finder" };
        reveal.Click += (_, _) => RevealRequested?.Invoke(node);
        var findUsages = new MenuItem { Header = "Find usages" };
        findUsages.Click += (_, _) => FindUsagesRequested?.Invoke(node);
        menu.Items.Add(open);
        menu.Items.Add(reveal);
        menu.Items.Add(findUsages);
        menu.Items.Add(new Separator());
        menu.Items.Add(rename);
        menu.Items.Add(del);
        item.ContextMenu = menu;

        if (node.Children.Count > 0)
        {
            item.ItemsSource = node.Children.Select(BuildItem).ToList();
        }

        return item;
    }

    private static Control Label(ProjectTreeNode node)
    {
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Height = 18,
            VerticalAlignment = VerticalAlignment.Center
        };

        var ext = Path.GetExtension(node.Name).ToLowerInvariant();
        if (!node.IsDirectory && AssetIcon.IsImage(ext))
        {
            var bmp = MonoForge.Editor.Services.TextureCache.Get(node.FullPath);
            if (bmp is not null)
            {
                row.Children.Add(new Image
                {
                    Source = bmp,
                    Width = 16,
                    Height = 16,
                    Stretch = Stretch.Uniform,
                    VerticalAlignment = VerticalAlignment.Center
                });

                // Hover preview tooltip with larger thumbnail
                ToolTip.SetTip(row, new StackPanel
                {
                    Spacing = 4,
                    Children =
                    {
                        new Image { Source = bmp, Width = 192, Height = 192, Stretch = Stretch.Uniform },
                        new TextBlock { Text = $"{bmp.PixelSize.Width}×{bmp.PixelSize.Height}", Foreground = Brush(TextDim), FontSize = 11, HorizontalAlignment = HorizontalAlignment.Center }
                    }
                });
                ToolTip.SetShowDelay(row, 400);
                ToolTip.SetPlacement(row, PlacementMode.RightEdgeAlignedTop);
            }
            else
            {
                row.Children.Add(IconText(node));
            }
        }
        else
        {
            row.Children.Add(IconText(node));
        }

        row.Children.Add(new TextBlock
        {
            Text = node.Name,
            Foreground = Brush(TextDivider),
            FontFamily = FontFamily.Parse(UiFont),
            FontSize = 11,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 200
        });

        if (!node.IsDirectory && _gitStatus.TryGetValue(node.FullPath, out var status))
        {
            row.Children.Add(new TextBlock
            {
                Text = " " + status,
                Foreground = Brush(status switch
                {
                    "M" => "#ffd166",
                    "A" => "#7bd88f",
                    "??" => "#42b7ff",
                    "D" => "#ff6b6b",
                    _ => TextDim
                }),
                FontFamily = FontFamily.Parse("Menlo"),
                FontSize = 10,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        return row;
    }

    private static TextBlock IconText(ProjectTreeNode node) => new()
    {
        Text = AssetIcon.For(node),
        Foreground = Brush(AssetIcon.ColorFor(node)),
        FontFamily = FontFamily.Parse("Menlo"),
        FontSize = node.IsDirectory ? 12 : 13,
        Width = 16,
        VerticalAlignment = VerticalAlignment.Center,
        TextAlignment = TextAlignment.Center
    };
}
