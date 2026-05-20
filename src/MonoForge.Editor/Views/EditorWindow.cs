using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using MonoForge.Editor.Models;
using MonoForge.Editor.Services;
using MonoForge.Editor.Views.Panels;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class EditorWindow : Window
{
    private readonly SceneCanvas _sceneCanvas = new();
    private readonly PixelEditor _pixelEditor = new();
    private readonly AssetsPanel _assets = new();
    private readonly OutlinePanel _outline = new();
    private readonly InspectorPanel _inspector = new();
    private readonly Panels.LayersPanel _layers = new();
    private readonly Panels.StatisticsPanel _stats = new();
    private readonly ConsolePanel _console = new();
    private readonly DocumentTabHost _tabs = new();
    private readonly TextBlock _status = new();
    private readonly TextBlock _cursorReadout = new();
    private readonly UndoStack _history = new();
    private const string SceneTabKey = "__scene__";
    private Button? _runButton;
    private CancellationTokenSource? _runCts;
    private bool _isRunning;

    private string? _projectPath;
    private SceneDocument _scene = SeedScene();
    private string? _selectedId = "player";
    private string _activeColor = "#65a7ff";

    public EditorWindow()
    {
        var settings = Services.UserSettings.Current;
        _sceneCanvas.SnapSize = settings.DefaultSnap;
        _sceneCanvas.ShowGrid = settings.ShowGridDefault;
        _sceneCanvas.SnapToGrid = settings.SnapToGridDefault;

        Title = "MonoForge 0.2";
        Width = 1510;
        Height = 824;
        MinWidth = 1080;
        MinHeight = 720;
        Background = Brush(EditorBackground);
        Content = BuildShell();
        WireEvents();
        RegisterKeyBindings();
        RenderAll();
        RefreshToolButtons();
        Closing += (_, _) => SaveWorkspace();
        StartAutoSave();
        Services.TextureCache.TextureReloaded += () =>
        {
            _sceneCanvas.InvalidateVisual();
            _console.Log("Texture reloaded");
        };
        _console.BuildErrorClicked += (path, line) => OpenFileAtLine(path, line);
        _layers.VisibilityChanged += () => _sceneCanvas.InvalidateVisual();
        _layers.LockChanged += () => { RenderAll(); };
        _console.Log("Open a project folder to populate Assets.");
    }

    private Control BuildShell()
    {
        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*,24"),
            Background = Brush(EditorBackground)
        };

        root.Children.Add(BuildMenuBar().At(row: 0));
        root.Children.Add(BuildToolbar().At(row: 1));
        root.Children.Add(BuildWorkspace().At(row: 2));
        root.Children.Add(BuildStatusBar().At(row: 3));
        return root;
    }

    private Control BuildMenuBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Background = Brush(MenuBackground)
        };

        var left = RowStack(MenuBackground);
        left.Children.Add(Text("◆  MonoForge", TextPrimary, FontWeight.Bold));
        left.Children.Add(BuildFileMenu());
        left.Children.Add(BuildEditMenu());
        left.Children.Add(BuildViewMenu());
        left.Children.Add(BuildProjectMenu());
        left.Children.Add(BuildDebugMenu());
        left.Children.Add(BuildHelpMenu());

        var right = RowStack(MenuBackground);
        right.Children.Add(MenuButton("Undo", (_, _) => Undo()));
        right.Children.Add(MenuButton("Redo", (_, _) => Redo()));
        right.Children.Add(MenuButton("Save", async (_, _) => await SaveSceneAsync()));
        right.Children.Add(MenuButton("Load", async (_, _) => await LoadSceneAsync()));
        right.Children.Add(MenuButton("Atlas...", (_, _) => new AtlasWindow().Show()));
        right.Children.Add(MenuButton("Tilemap...", (_, _) => new TilemapEditor().Show()));
        right.Children.Add(MenuButton("Animation...", (_, _) => new AnimationEditor().Show()));
        right.Children.Add(MenuButton("Particles...", (_, _) => new ParticleEditor().Show()));
        right.Children.Add(MenuButton("Preferences...", (_, _) => new SettingsWindow().Show()));
        right.Children.Add(MenuButton("Scan TODOs", (_, _) => ScanTodos()));
        right.Children.Add(MenuButton("Sync Content", (_, _) => SyncContent()));
        right.Children.Add(MenuButton("Export", async (_, _) => await ExportSceneAsync()));
        right.Children.Add(MenuButton("Build", async (_, _) => await DotnetAsync("build")));
        _runButton = PrimaryButton("▶ Play", async (_, _) => await TogglePlay());
        right.Children.Add(_runButton);

        bar.Children.Add(left.At(column: 0));
        bar.Children.Add(right.At(column: 2));
        return bar;
    }

    private Control BuildFileMenu()
    {
        var menu = new Menu
        {
            Background = Avalonia.Media.Brushes.Transparent
        };
        var file = new MenuItem { Header = "File", Foreground = Brush(TextSecondary) };

        var newProject = new MenuItem { Header = "New Project..." };
        newProject.Click += async (_, _) =>
        {
            var dlg = new NewProjectDialog();
            await dlg.ShowDialog(this);
            if (dlg.CreatedAt is { } path)
            {
                await OpenProjectFromPath(path);
                _console.Log("Created project: " + path, "OK");
            }
        };
        file.Items.Add(newProject);

        var openProject = new MenuItem { Header = "Open Project..." };
        openProject.Click += async (_, _) => await OpenProjectAsync();
        file.Items.Add(openProject);

        var recentProjects = new MenuItem { Header = "Recent Projects" };
        RefillRecentProjects(recentProjects);
        file.Items.Add(recentProjects);

        var recentFiles = new MenuItem { Header = "Recent Files" };
        RefillRecentFiles(recentFiles);
        file.Items.Add(recentFiles);

        Services.UserSettings.Changed += () =>
        {
            RefillRecentProjects(recentProjects);
            RefillRecentFiles(recentFiles);
        };

        file.Items.Add(new Separator());

        var saveScene = new MenuItem { Header = "Save Scene...   ⌘S" };
        saveScene.Click += async (_, _) => await SaveSceneAsync();
        file.Items.Add(saveScene);

        var loadScene = new MenuItem { Header = "Load Scene..." };
        loadScene.Click += async (_, _) => await LoadSceneAsync();
        file.Items.Add(loadScene);

        file.Items.Add(new Separator());

        var quickOpen = new MenuItem { Header = "Quick Open File...   ⌘P" };
        quickOpen.Click += (_, _) => OpenQuickFile();
        file.Items.Add(quickOpen);

        menu.Items.Add(file);
        return menu;
    }

    private void RefillRecentProjects(MenuItem parent)
    {
        parent.Items.Clear();
        var list = Services.UserSettings.Current.RecentProjects;
        if (list.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        foreach (var p in list)
        {
            var item = new MenuItem { Header = Path.GetFileName(p) + "    " + p };
            var captured = p;
            item.Click += async (_, _) =>
            {
                if (!Directory.Exists(captured))
                {
                    _console.Log("Project gone: " + captured, "WARN");
                    return;
                }
                await OpenProjectFromPath(captured);
            };
            parent.Items.Add(item);
        }
    }

    private void RefillRecentFiles(MenuItem parent)
    {
        parent.Items.Clear();
        var list = Services.UserSettings.Current.RecentFiles;
        if (list.Count == 0)
        {
            parent.Items.Add(new MenuItem { Header = "(empty)", IsEnabled = false });
            return;
        }
        foreach (var p in list)
        {
            var item = new MenuItem { Header = Path.GetFileName(p) + "    " + p };
            var captured = p;
            item.Click += (_, _) => OpenFileAtLine(captured, 1);
            parent.Items.Add(item);
        }
    }

    private async Task OpenProjectFromPath(string path)
    {
        _projectPath = path;
        Title = $"{Path.GetFileName(path)} - MonoForge 0.2";
        _assets.LoadProject(path);
        _console.Log("Opened project: " + path);
        LoadWorkspace(path);
        Services.UserSettings.Current.PushRecentProject(path);
        UpdateStatus();
        await Task.CompletedTask;
    }

    private Control BuildEditMenu()
    {
        var menu = new Menu { Background = Avalonia.Media.Brushes.Transparent };
        var edit = new MenuItem { Header = "Edit", Foreground = Brush(TextSecondary) };
        var undo = new MenuItem { Header = "Undo   ⌘Z" };
        undo.Click += (_, _) => Undo();
        var redo = new MenuItem { Header = "Redo   ⌘⇧Z" };
        redo.Click += (_, _) => Redo();
        var dup = new MenuItem { Header = "Duplicate   ⌘D" };
        dup.Click += (_, _) => DuplicateSelected();
        var del = new MenuItem { Header = "Delete   ⌫" };
        del.Click += (_, _) => DeleteSelected();
        var group = new MenuItem { Header = "Group   ⌘G" };
        group.Click += (_, _) => GroupSelection();
        var ungroup = new MenuItem { Header = "Ungroup" };
        ungroup.Click += (_, _) => UngroupSelection();
        var bf = new MenuItem { Header = "Bring to Front   ⌘]" };
        bf.Click += (_, _) => BringToFront();
        var sb = new MenuItem { Header = "Send to Back   ⌘[" };
        sb.Click += (_, _) => SendToBack();
        edit.Items.Add(undo); edit.Items.Add(redo);
        edit.Items.Add(new Separator());
        edit.Items.Add(dup); edit.Items.Add(del);
        edit.Items.Add(new Separator());
        edit.Items.Add(group); edit.Items.Add(ungroup);
        edit.Items.Add(new Separator());
        edit.Items.Add(bf); edit.Items.Add(sb);
        menu.Items.Add(edit);
        return menu;
    }

    private Control BuildViewMenu()
    {
        var menu = new Menu { Background = Avalonia.Media.Brushes.Transparent };
        var view = new MenuItem { Header = "View", Foreground = Brush(TextSecondary) };
        var frame = new MenuItem { Header = "Frame Scene   F" };
        frame.Click += (_, _) => FrameScene();
        var grid = new MenuItem { Header = "Toggle Grid   G" };
        grid.Click += (_, _) => ToggleGrid();
        var snap = new MenuItem { Header = "Toggle Snap" };
        snap.Click += (_, _) => ToggleSnap();
        var pixel = new MenuItem { Header = "Toggle Pixel-Perfect" };
        pixel.Click += (_, _) =>
        {
            _sceneCanvas.PixelPerfect = !_sceneCanvas.PixelPerfect;
            _console.Log("Pixel-perfect: " + (_sceneCanvas.PixelPerfect ? "on" : "off"));
        };
        var snapToObj = new MenuItem { Header = "Toggle Snap to Objects" };
        snapToObj.Click += (_, _) =>
        {
            _sceneCanvas.SnapToObjects = !_sceneCanvas.SnapToObjects;
            _console.Log("Snap to objects: " + (_sceneCanvas.SnapToObjects ? "on" : "off"));
        };
        view.Items.Add(frame);
        view.Items.Add(new Separator());
        view.Items.Add(grid); view.Items.Add(snap); view.Items.Add(snapToObj); view.Items.Add(pixel);
        menu.Items.Add(view);
        return menu;
    }

    private Control BuildProjectMenu()
    {
        var menu = new Menu { Background = Avalonia.Media.Brushes.Transparent };
        var proj = new MenuItem { Header = "Project", Foreground = Brush(TextSecondary) };
        var atlas = new MenuItem { Header = "Atlas Packer..." };
        atlas.Click += (_, _) => new AtlasWindow().Show();
        var tile = new MenuItem { Header = "Tilemap Editor..." };
        tile.Click += (_, _) => new TilemapEditor().Show();
        var anim = new MenuItem { Header = "Animation Editor..." };
        anim.Click += (_, _) => new AnimationEditor().Show();
        var part = new MenuItem { Header = "Particle Editor..." };
        part.Click += (_, _) => new ParticleEditor().Show();
        var sync = new MenuItem { Header = "Sync to Content/" };
        sync.Click += (_, _) => SyncContent();
        var todo = new MenuItem { Header = "Scan TODOs" };
        todo.Click += (_, _) => ScanTodos();
        var export = new MenuItem { Header = "Export Scene as C#..." };
        export.Click += async (_, _) => await ExportSceneAsync();
        var exportModel = new MenuItem { Header = "Emit GLB Runtime Loader..." };
        exportModel.Click += async (_, _) => await ExportModelRuntimeAsync();
        var prefs = new MenuItem { Header = "Preferences..." };
        prefs.Click += (_, _) => new SettingsWindow().Show();
        proj.Items.Add(atlas); proj.Items.Add(tile); proj.Items.Add(anim); proj.Items.Add(part);
        proj.Items.Add(new Separator());
        proj.Items.Add(sync); proj.Items.Add(todo); proj.Items.Add(export); proj.Items.Add(exportModel);
        proj.Items.Add(new Separator());
        proj.Items.Add(prefs);
        menu.Items.Add(proj);
        return menu;
    }

    private Control BuildDebugMenu()
    {
        var menu = new Menu { Background = Avalonia.Media.Brushes.Transparent };
        var dbg = new MenuItem { Header = "Debug", Foreground = Brush(TextSecondary) };
        var build = new MenuItem { Header = "Build   ⌘B" };
        build.Click += async (_, _) => await DotnetAsync("build");
        var run = new MenuItem { Header = "Run with Live Reload   ⌘R" };
        run.Click += async (_, _) => await TogglePlay();
        var push = new MenuItem { Header = "Push Live Reload   ⌘E" };
        push.Click += (_, _) => PushLiveReload();
        var findFiles = new MenuItem { Header = "Find in Files...   ⌘⇧F" };
        findFiles.Click += (_, _) => OpenFindInFiles();
        var quick = new MenuItem { Header = "Quick Open...   ⌘P" };
        quick.Click += (_, _) => OpenQuickFile();
        var symbol = new MenuItem { Header = "Goto Symbol...   ⌘T" };
        symbol.Click += (_, _) => OpenGotoSymbol();
        dbg.Items.Add(build); dbg.Items.Add(run); dbg.Items.Add(push);
        dbg.Items.Add(new Separator());
        dbg.Items.Add(findFiles); dbg.Items.Add(quick); dbg.Items.Add(symbol);
        menu.Items.Add(dbg);
        return menu;
    }

    private Control BuildHelpMenu()
    {
        var menu = new Menu { Background = Avalonia.Media.Brushes.Transparent };
        var help = new MenuItem { Header = "Help", Foreground = Brush(TextSecondary) };
        var about = new MenuItem { Header = "About MonoForge" };
        about.Click += (_, _) =>
        {
            _console.Log("MonoForge — Defold-style editor for MonoGame", "OK");
            _console.Log("https://www.monogame.net  |  https://defold.com (inspiration)");
        };
        var shortcuts = new MenuItem { Header = "Show Keyboard Shortcuts" };
        shortcuts.Click += (_, _) => ShowShortcuts();
        help.Items.Add(about);
        help.Items.Add(shortcuts);
        menu.Items.Add(help);
        return menu;
    }

    private void ShowShortcuts()
    {
        _console.Log("=== Keyboard Shortcuts ===", "OK");
        _console.Log("Scene:  S select | M move | R rect | F frame | G grid | H flip-h | V flip-v | Del delete");
        _console.Log("Edit:   ⌘Z undo | ⌘⇧Z redo | ⌘D duplicate | ⌘G group | ⌘] front | ⌘[ back");
        _console.Log("Files:  ⌘S save | ⌘P quick-open | ⌘⇧F find-in-files | ⌘T goto-symbol");
        _console.Log("Code:   ⌘D select-next-occurrence | ⌘F find | ⌘F2 bookmark | F2 next-bookmark");
        _console.Log("Build:  ⌘B build | ⌘R run | ⌘E push-live-reload");
    }

    private Control BuildToolbar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"),
            Background = Brush(ToolbarBackground),
            Height = 32
        };
        var tools = RowStack(ToolbarBackground);
        tools.Children.Add(ToolButton("Select  (S)", "select"));
        tools.Children.Add(ToolButton("Move  (M)", "move"));
        tools.Children.Add(ToolButton("Rect  (R)", "rect"));
        tools.Children.Add(MenuButton("Frame  (F)", (_, _) => FrameScene()));
        tools.Children.Add(MenuButton("Grid  (G)", (_, _) => ToggleGrid()));
        tools.Children.Add(MenuButton("Snap", (_, _) => ToggleSnap()));
        var snapCombo = new ComboBox
        {
            ItemsSource = new[] { 1, 4, 8, 16, 32 },
            SelectedItem = _sceneCanvas.SnapSize,
            Width = 70,
            Background = Brush(InputBackground),
            Foreground = Brush(TextSecondary),
            FontSize = 12
        };
        snapCombo.SelectionChanged += (_, _) =>
        {
            if (snapCombo.SelectedItem is int sz)
            {
                _sceneCanvas.SnapSize = sz;
                _sceneCanvas.InvalidateVisual();
                _console.Log("Snap: " + sz + "px");
            }
        };
        tools.Children.Add(snapCombo);
        tools.Children.Add(MenuButton("Pixel", (_, _) =>
        {
            _sceneCanvas.PixelPerfect = !_sceneCanvas.PixelPerfect;
            _console.Log("Pixel-perfect: " + (_sceneCanvas.PixelPerfect ? "on" : "off"));
        }));
        tools.Children.Add(new TextBlock { Text = "│", Foreground = Brush(TextFaint), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Margin = new Thickness(8, 0) });
        tools.Children.Add(MenuButton("⊞ Group  (⌘G)", (_, _) => GroupSelection()));
        tools.Children.Add(MenuButton("⊟ Ungroup", (_, _) => UngroupSelection()));
        tools.Children.Add(MenuButton("⊨ L", (_, _) => AlignSelection("left")));
        tools.Children.Add(MenuButton("┃ C", (_, _) => AlignSelection("centerH")));
        tools.Children.Add(MenuButton("⊩ R", (_, _) => AlignSelection("right")));
        tools.Children.Add(MenuButton("⊤ T", (_, _) => AlignSelection("top")));
        tools.Children.Add(MenuButton("━ M", (_, _) => AlignSelection("centerV")));
        tools.Children.Add(MenuButton("⊥ B", (_, _) => AlignSelection("bottom")));
        tools.Children.Add(MenuButton("⇔ Dist X", (_, _) => DistributeSelection(horizontal: true)));
        tools.Children.Add(MenuButton("⇕ Dist Y", (_, _) => DistributeSelection(horizontal: false)));
        bar.Children.Add(tools.At(column: 0));
        bar.Children.Add(Text("Pan: MMB or Alt+drag    Zoom: wheel    Undo: ⌘Z", TextDim).At(column: 2));
        return bar;
    }

    private Control BuildWorkspace()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("266,4,*,4,282"),
            Background = Brush(EditorBackground)
        };

        grid.Children.Add(_assets.At(column: 0));
        grid.Children.Add(VSplitter().At(column: 1));
        grid.Children.Add(BuildCenter().At(column: 2));
        grid.Children.Add(VSplitter().At(column: 3));
        grid.Children.Add(BuildRightDock().At(column: 4));
        return grid;
    }

    private Control BuildCenter()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,4,272"),
            Background = Brush(EditorBackground)
        };
        _tabs.OpenOrFocus(SceneTabKey, _scene.Name, () => _sceneCanvas);
        grid.Children.Add(_tabs.At(row: 0));
        grid.Children.Add(HSplitter().At(row: 1));
        grid.Children.Add(_console.At(row: 2));
        return grid;
    }

    private Control BuildRightDock()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,4,*,4,140,Auto"),
            Background = Brush(PanelBackground)
        };
        grid.Children.Add(_outline.At(row: 0));
        grid.Children.Add(HSplitter().At(row: 1));
        grid.Children.Add(_inspector.At(row: 2));
        grid.Children.Add(HSplitter().At(row: 3));
        var bottomCol = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*") };
        bottomCol.Children.Add(_layers.At(column: 0));
        bottomCol.Children.Add(_stats.At(column: 1));
        grid.Children.Add(bottomCol.At(row: 4));
        grid.Children.Add(BuildSpritePane().At(row: 5));
        return BorderBox(grid, BorderSubtle, 1, 0, 0, 0);
    }

    private static GridSplitter VSplitter()
    {
        return new GridSplitter
        {
            Background = Brush(BorderSubtle),
            Width = 4,
            ResizeDirection = GridResizeDirection.Columns,
            ShowsPreview = false
        };
    }

    private static GridSplitter HSplitter()
    {
        return new GridSplitter
        {
            Background = Brush(BorderSubtle),
            Height = 4,
            ResizeDirection = GridResizeDirection.Rows,
            ShowsPreview = false
        };
    }

    private Control BuildSpritePane()
    {
        var stack = ColumnStack(PanelBackgroundAlt);
        stack.Children.Add(Text("Sprite Editor", TextPrimary, FontWeight.Bold).WithMargin(10, 8, 10, 4));
        stack.Children.Add(_pixelEditor.WithMargin(12, 0, 12, 8));
        var palette = RowStack();
        palette.Margin = new Thickness(12, 0, 12, 8);
        foreach (var color in new[] { "#65a7ff", "#7bd88f", "#ffd166", "#ff6b6b", "#d7dce5", "#15171b" })
        {
            palette.Children.Add(new Button
            {
                Width = 22, Height = 22,
                Background = Brush(color),
                BorderBrush = Brush(ObjectStroke),
                BorderThickness = new Thickness(color == _activeColor ? 2 : 1),
                Tag = color,
                Margin = new Thickness(0, 0, 5, 0)
            }.OnClick((sender, _) =>
            {
                if (sender is Button { Tag: string selected })
                {
                    SetActiveColor(selected);
                }
            }));
        }

        stack.Children.Add(palette);
        return BorderBox(stack, BorderColor, 1, 1, 0, 0);
    }

    private Control BuildStatusBar()
    {
        _status.Foreground = Brush(TextDim);
        _status.Padding = new Thickness(12, 4, 10, 0);
        _status.Text = "Ready";

        _cursorReadout.Foreground = Brush(TextDim);
        _cursorReadout.FontFamily = new FontFamily("Menlo");
        _cursorReadout.FontSize = 11;
        _cursorReadout.Padding = new Thickness(8, 4, 12, 0);
        _cursorReadout.Text = "x: -  y: -";

        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush(MenuBackground)
        };
        bar.Children.Add(_status.At(column: 0));
        bar.Children.Add(_cursorReadout.At(column: 1));
        return bar;
    }

    private void WireEvents()
    {
        _assets.OpenProjectRequested += async () => await OpenProjectAsync();
        _assets.NodeSelected += OnAssetSelected;
        _assets.Log += msg => _console.Log(msg);
        _assets.RenameRequested += node => _ = RenameAssetAsync(node);
        _assets.DeleteRequested += node => DeleteAsset(node);
        _assets.RevealRequested += node => RevealInFinder(node.FullPath);
        _assets.FindUsagesRequested += FindAssetUsages;

        _sceneCanvas.SelectionChanged += id =>
        {
            _selectedId = id;
            _outline.Update(_scene, _selectedId);
            _inspector.Update(SelectedObject());
            UpdateStatus();
        };
        _sceneCanvas.ObjectChanged += () =>
        {
            _inspector.Update(SelectedObject());
            UpdateStatus();
        };
        _sceneCanvas.AddSpriteRequested += point =>
        {
            CaptureHistory();
            AddSpriteAt(point);
        };
        _sceneCanvas.DragCompleted += CaptureHistory;
        _sceneCanvas.CursorWorldChanged += world => _cursorReadout.Text = $"x: {world.X:0}  y: {world.Y:0}";
        _sceneCanvas.ContextDuplicate += DuplicateSelected;
        _sceneCanvas.ContextDelete += DeleteSelected;
        _sceneCanvas.ContextFrame += FrameScene;
        _sceneCanvas.AssetDropped += (path, world) =>
        {
            CaptureHistory();
            AddSpriteFromTexture(path, world);
        };
        _sceneCanvas.AtlasRegionDropped += (path, world, rx, ry, rw, rh, name) =>
        {
            CaptureHistory();
            AddSpriteFromAtlasRegion(path, world, rx, ry, rw, rh, name);
        };

        _outline.SelectionRequested += id =>
        {
            _selectedId = id;
            _sceneCanvas.SelectedId = id;
            _outline.Update(_scene, _selectedId);
            _inspector.Update(SelectedObject());
            _sceneCanvas.InvalidateVisual();
        };
        _outline.DuplicateRequested += DuplicateSelected;
        _outline.DeleteRequested += DeleteSelected;
        _outline.ReparentRequested += (childId, newParentId) =>
        {
            if (childId == newParentId) return;
            CaptureHistory();
            if (Reparent(childId, newParentId))
            {
                _console.Log(newParentId is null ? "Moved to root" : "Reparented to " + newParentId);
                RenderAll();
            }
        };
        _outline.VisibilityToggled += id =>
        {
            if (_scene.Find(id) is { } obj)
            {
                CaptureHistory();
                obj.Visible = !obj.Visible;
                RenderAll();
            }
        };
        _outline.LockToggled += id =>
        {
            if (_scene.Find(id) is { } obj)
            {
                CaptureHistory();
                obj.Locked = !obj.Locked;
                if (obj.Locked) _sceneCanvas.SelectedIds.Remove(id);
                RenderAll();
            }
        };

        _inspector.PickTexture = async current =>
        {
            if (_projectPath is null) return null;
            var dlg = new SpritePickerDialog(_projectPath, current);
            await dlg.ShowDialog(this);
            return dlg.PickedPath;
        };
        _inspector.OpenNineSlice = async obj =>
        {
            var dlg = new NineSliceEditor(obj);
            await dlg.ShowDialog(this);
            return dlg.Applied;
        };
        _inspector.PropertyChanging += CaptureHistory;
        _inspector.PropertyCommitted += () =>
        {
            _outline.Update(_scene, _selectedId);
            _sceneCanvas.InvalidateVisual();
            UpdateStatus();
        };

        _pixelEditor.Painted += color =>
        {
            if (SelectedObject() is { } selected)
            {
                selected.Color = color;
                _inspector.Update(selected);
                _sceneCanvas.InvalidateVisual();
            }
        };
        _pixelEditor.Picked += SetActiveColor;
    }

    private void RegisterKeyBindings()
    {
        KeyDown += (_, e) =>
        {
            var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);

            if (meta && e.Key == Key.Z && e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { Redo(); e.Handled = true; return; }
            if (meta && e.Key == Key.Z) { Undo(); e.Handled = true; return; }
            if (meta && e.Key == Key.Y) { Redo(); e.Handled = true; return; }
            if (meta && e.Key == Key.S)
            {
                if (!TrySaveActiveDocument()) _ = SaveSceneAsync();
                e.Handled = true;
                return;
            }
            if (meta && e.Key == Key.D) { DuplicateSelected(); e.Handled = true; return; }
            if (meta && e.Key == Key.B) { _ = DotnetAsync("build"); e.Handled = true; return; }
            if (meta && e.Key == Key.P) { OpenQuickFile(); e.Handled = true; return; }
            if (meta && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.F)
            {
                OpenFindInFiles(); e.Handled = true; return;
            }
            if (meta && e.Key == Key.R) { _ = DotnetAsync("run"); e.Handled = true; return; }
            if (meta && e.Key == Key.E) { PushLiveReload(); e.Handled = true; return; }
            if (meta && e.Key == Key.T) { OpenGotoSymbol(); e.Handled = true; return; }
            if (meta && e.Key == Key.G) { GroupSelection(); e.Handled = true; return; }
            if (meta && e.Key == Key.OemCloseBrackets)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ZOrderForward();
                else BringToFront();
                e.Handled = true; return;
            }
            if (meta && e.Key == Key.OemOpenBrackets)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) ZOrderBackward();
                else SendToBack();
                e.Handled = true; return;
            }
            if (!meta && e.Key == Key.H && FocusManager?.GetFocusedElement() is not TextBox)
            {
                FlipSelected(horizontal: true); e.Handled = true; return;
            }
            if (!meta && e.Key == Key.V && FocusManager?.GetFocusedElement() is not TextBox && !e.KeyModifiers.HasFlag(KeyModifiers.Shift))
            {
                FlipSelected(horizontal: false); e.Handled = true; return;
            }

            if (FocusManager?.GetFocusedElement() is TextBox)
            {
                return;
            }

            switch (e.Key)
            {
                case Key.S: SetTool("select"); e.Handled = true; break;
                case Key.M: SetTool("move"); e.Handled = true; break;
                case Key.R: SetTool("rect"); e.Handled = true; break;
                case Key.F: FrameScene(); e.Handled = true; break;
                case Key.G: ToggleGrid(); e.Handled = true; break;
                case Key.Delete:
                case Key.Back:
                    DeleteSelected();
                    e.Handled = true;
                    break;
            }
        };
    }

    private void RenderAll()
    {
        _sceneCanvas.Scene = _scene;
        _sceneCanvas.SelectedId = _selectedId;
        _pixelEditor.ActiveColor = _activeColor;
        _outline.Update(_scene, _selectedId);
        _inspector.Update(SelectedObject());
        _layers.Update(_scene);
        _stats.Update(_scene);
        UpdateStatus();
        _sceneCanvas.InvalidateVisual();
    }

    private void CaptureHistory()
    {
        _history.Capture(_scene);
    }

    private void Undo()
    {
        if (_history.Undo(_scene) is { } restored)
        {
            _scene = restored;
            _selectedId = _scene.Objects.FirstOrDefault()?.Id;
            _console.Log("Undo", "INFO");
            RenderAll();
        }
    }

    private void Redo()
    {
        if (_history.Redo(_scene) is { } restored)
        {
            _scene = restored;
            _selectedId = _scene.Objects.FirstOrDefault()?.Id;
            _console.Log("Redo", "INFO");
            RenderAll();
        }
    }

    private void AddSpriteAt(Point point)
    {
        var id = "sprite_" + (_scene.Objects.Count + 1);
        _scene.Objects.Add(new SceneObject
        {
            Id = id,
            Name = "NewSprite",
            Type = "Sprite",
            X = Math.Round(point.X / 8) * 8,
            Y = Math.Round(point.Y / 8) * 8,
            Width = 64,
            Height = 64,
            Color = _activeColor,
            Layer = _scene.Objects.Count + 1
        });
        _selectedId = id;
        _console.Log("Added NewSprite", "OK");
        RenderAll();
    }

    private void AddSpriteFromAtlasRegion(string texturePath, Point world, int rx, int ry, int rw, int rh, string name)
    {
        var id = "sprite_" + (_scene.Objects.Count + 1);
        _scene.Objects.Add(new SceneObject
        {
            Id = id,
            Name = name,
            Type = "Sprite",
            X = Math.Round(world.X / 8) * 8,
            Y = Math.Round(world.Y / 8) * 8,
            Width = rw,
            Height = rh,
            Color = "#ffffff",
            TexturePath = texturePath,
            SourceX = rx,
            SourceY = ry,
            SourceW = rw,
            SourceH = rh,
            Layer = _scene.Objects.Count + 1
        });
        _selectedId = id;
        _console.Log($"Added atlas region '{name}'", "OK");
        RenderAll();
    }

    private void BringToFront()
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count == 0) return;
        CaptureHistory();
        var maxLayer = _scene.Flatten().Max(o => o.Layer);
        foreach (var o in objs) o.Layer = ++maxLayer;
        RenderAll();
    }

    private void SendToBack()
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count == 0) return;
        CaptureHistory();
        var minLayer = _scene.Flatten().Min(o => o.Layer);
        foreach (var o in objs) o.Layer = --minLayer;
        RenderAll();
    }

    private void ZOrderForward()
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count == 0) return;
        CaptureHistory();
        foreach (var o in objs) o.Layer += 1;
        RenderAll();
    }

    private void ZOrderBackward()
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count == 0) return;
        CaptureHistory();
        foreach (var o in objs) o.Layer -= 1;
        RenderAll();
    }

    private void FlipSelected(bool horizontal)
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count == 0) return;
        CaptureHistory();
        foreach (var o in objs)
        {
            if (horizontal) o.FlipX = !o.FlipX;
            else o.FlipY = !o.FlipY;
        }
        RenderAll();
    }

    private void GroupSelection()
    {
        var ids = _sceneCanvas.SelectedIds.ToList();
        if (ids.Count < 2) return;
        var objects = ids.Select(id => _scene.Find(id)).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objects.Count < 2) return;

        CaptureHistory();
        var minX = objects.Min(o => o.X);
        var minY = objects.Min(o => o.Y);
        var maxX = objects.Max(o => o.X + o.Width);
        var maxY = objects.Max(o => o.Y + o.Height);

        var group = new SceneObject
        {
            Id = "group_" + Guid.NewGuid().ToString("N").Substring(0, 6),
            Name = "Group",
            Type = "Group",
            X = minX,
            Y = minY,
            Width = maxX - minX,
            Height = maxY - minY,
            Color = "#888888",
            Layer = objects.Max(o => o.Layer) + 1
        };

        foreach (var o in objects)
        {
            _scene.Remove(o.Id);
            o.X -= minX;
            o.Y -= minY;
            o.Parent = group;
            group.Children.Add(o);
        }
        _scene.Objects.Add(group);
        _sceneCanvas.SelectedIds.Clear();
        _sceneCanvas.SelectedIds.Add(group.Id);
        _selectedId = group.Id;
        _console.Log($"Grouped {objects.Count} objects", "OK");
        RenderAll();
    }

    private void UngroupSelection()
    {
        var ids = _sceneCanvas.SelectedIds.ToList();
        CaptureHistory();
        var ungrouped = 0;
        foreach (var id in ids)
        {
            if (_scene.Find(id) is { Type: "Group" } g)
            {
                var gx = g.X; var gy = g.Y;
                _scene.Remove(g.Id);
                foreach (var child in g.Children)
                {
                    child.Parent = null;
                    child.X += gx;
                    child.Y += gy;
                    _scene.Objects.Add(child);
                }
                ungrouped++;
            }
        }
        if (ungrouped > 0)
        {
            _console.Log($"Ungrouped {ungrouped} group(s)", "OK");
            RenderAll();
        }
    }

    private void AlignSelection(string mode)
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count < 2) return;
        CaptureHistory();
        switch (mode)
        {
            case "left":    { var x = objs.Min(o => o.X); foreach (var o in objs) o.X = x; break; }
            case "right":   { var x = objs.Max(o => o.X + o.Width); foreach (var o in objs) o.X = x - o.Width; break; }
            case "centerH": { var cx = objs.Average(o => o.X + o.Width / 2); foreach (var o in objs) o.X = cx - o.Width / 2; break; }
            case "top":     { var y = objs.Min(o => o.Y); foreach (var o in objs) o.Y = y; break; }
            case "bottom":  { var y = objs.Max(o => o.Y + o.Height); foreach (var o in objs) o.Y = y - o.Height; break; }
            case "centerV": { var cy = objs.Average(o => o.Y + o.Height / 2); foreach (var o in objs) o.Y = cy - o.Height / 2; break; }
        }
        RenderAll();
    }

    private void DistributeSelection(bool horizontal)
    {
        var objs = _sceneCanvas.SelectedIds.Select(_scene.Find).Where(o => o is not null).Cast<SceneObject>().ToList();
        if (objs.Count < 3) return;
        CaptureHistory();
        objs.Sort((a, b) => horizontal ? a.X.CompareTo(b.X) : a.Y.CompareTo(b.Y));
        var first = objs[0];
        var last = objs[^1];
        if (horizontal)
        {
            var span = (last.X + last.Width) - first.X;
            var totalW = objs.Sum(o => o.Width);
            var gap = (span - totalW) / (objs.Count - 1);
            var cursor = first.X;
            foreach (var o in objs) { o.X = cursor; cursor += o.Width + gap; }
        }
        else
        {
            var span = (last.Y + last.Height) - first.Y;
            var totalH = objs.Sum(o => o.Height);
            var gap = (span - totalH) / (objs.Count - 1);
            var cursor = first.Y;
            foreach (var o in objs) { o.Y = cursor; cursor += o.Height + gap; }
        }
        RenderAll();
    }

    private bool Reparent(string childId, string? newParentId)
    {
        var child = _scene.Find(childId);
        if (child is null) return false;

        // Prevent cycles
        if (newParentId is not null)
        {
            var p = _scene.Find(newParentId);
            while (p is not null)
            {
                if (p.Id == childId) return false;
                p = p.Parent;
            }
        }

        // Remove from current location
        if (!_scene.Remove(childId)) return false;
        child.Parent = null;

        if (newParentId is null)
        {
            _scene.Objects.Add(child);
        }
        else if (_scene.Find(newParentId) is { } parent)
        {
            parent.Children.Add(child);
            child.Parent = parent;
        }
        else
        {
            _scene.Objects.Add(child);
        }
        return true;
    }

    private void AddSpriteFromTexture(string texturePath, Point world)
    {
        var ext = Path.GetExtension(texturePath).ToLowerInvariant();

        // Tilemap?
        if (ext == ".json" && IsTilemapJson(texturePath))
        {
            AddTilemap(texturePath, world);
            return;
        }

        // 3D Model?
        if (ext is ".glb" or ".gltf")
        {
            AddModel3D(texturePath, world);
            return;
        }

        if (!AssetIcon.IsImage(ext))
        {
            _console.Log("Drop rejected — not an image: " + Path.GetFileName(texturePath), "WARN");
            return;
        }

        var id = "sprite_" + (_scene.Objects.Count + 1);
        var (w, h) = TryGetImageSize(texturePath);
        _scene.Objects.Add(new SceneObject
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(texturePath),
            Type = "Sprite",
            X = Math.Round(world.X / 8) * 8,
            Y = Math.Round(world.Y / 8) * 8,
            Width = w,
            Height = h,
            Color = "#ffffff",
            TexturePath = texturePath,
            Layer = _scene.Objects.Count + 1
        });
        _selectedId = id;
        _console.Log("Added sprite from " + Path.GetFileName(texturePath), "OK");
        RenderAll();
    }

    private static bool IsTilemapJson(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            return json.Contains("\"Tiles\"", StringComparison.Ordinal) && json.Contains("\"TileSize\"", StringComparison.Ordinal);
        }
        catch { return false; }
    }

    private void AddModel3D(string modelPath, Point world)
    {
        var id = "model_" + (_scene.Objects.Count + 1);
        _scene.Objects.Add(new SceneObject
        {
            Id = id,
            Name = Path.GetFileNameWithoutExtension(modelPath),
            Type = "Model3D",
            X = Math.Round(world.X / 8) * 8,
            Y = Math.Round(world.Y / 8) * 8,
            Width = 96, Height = 96,
            Color = "#9b6cff",
            ModelPath = modelPath,
            Layer = _scene.Objects.Count + 1
        });
        _selectedId = id;
        _console.Log("Added 3D model " + Path.GetFileName(modelPath), "OK");
        RenderAll();
    }

    private void AddTilemap(string tilemapPath, Point world)
    {
        try
        {
            var json = File.ReadAllText(tilemapPath);
            var map = System.Text.Json.JsonSerializer.Deserialize<Models.Tilemap>(json);
            if (map is null) return;
            var id = "tilemap_" + (_scene.Objects.Count + 1);
            _scene.Objects.Add(new SceneObject
            {
                Id = id,
                Name = Path.GetFileNameWithoutExtension(tilemapPath),
                Type = "Tilemap",
                X = Math.Round(world.X / 8) * 8,
                Y = Math.Round(world.Y / 8) * 8,
                Width = map.Width * map.TileSize,
                Height = map.Height * map.TileSize,
                Color = "#ffffff",
                TilemapPath = tilemapPath,
                Layer = _scene.Objects.Count + 1
            });
            _selectedId = id;
            _console.Log("Added tilemap " + Path.GetFileName(tilemapPath), "OK");
            RenderAll();
        }
        catch (Exception ex)
        {
            _console.Log("Tilemap drop failed: " + ex.Message, "ERR");
        }
    }

    private static (double, double) TryGetImageSize(string path)
    {
        try
        {
            using var bmp = new Avalonia.Media.Imaging.Bitmap(path);
            return (bmp.PixelSize.Width, bmp.PixelSize.Height);
        }
        catch
        {
            return (64, 64);
        }
    }

    private void DuplicateSelected()
    {
        if (SelectedObject() is not { } selected) return;
        CaptureHistory();
        var copy = new SceneObject
        {
            Id = selected.Id + "_copy_" + (_scene.Objects.Count + 1),
            Name = selected.Name + " Copy",
            Type = selected.Type,
            X = selected.X + 24,
            Y = selected.Y + 24,
            Width = selected.Width,
            Height = selected.Height,
            Color = selected.Color,
            Layer = selected.Layer + 1,
            Visible = selected.Visible
        };
        _scene.Objects.Add(copy);
        _selectedId = copy.Id;
        _console.Log("Duplicated " + selected.Name, "OK");
        RenderAll();
    }

    private void DeleteSelected()
    {
        var ids = _sceneCanvas.SelectedIds.Count > 0
            ? _sceneCanvas.SelectedIds.ToList()
            : (_selectedId is null ? new List<string>() : new List<string> { _selectedId });
        if (ids.Count == 0) return;

        CaptureHistory();
        var removed = 0;
        foreach (var id in ids)
        {
            if (_scene.Remove(id)) removed++;
        }
        if (removed == 0) return;

        _sceneCanvas.SelectedIds.Clear();
        _selectedId = _scene.Objects.FirstOrDefault()?.Id;
        _console.Log($"Deleted {removed} object(s)", "WARN");
        RenderAll();
    }

    private async Task SaveSceneAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "main.scene.json",
            FileTypeChoices = [new FilePickerFileType("Scene JSON") { Patterns = ["*.json"] }]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await JsonSerializer.SerializeAsync(stream, _scene, new JsonSerializerOptions { WriteIndented = true });
        _lastScenePath = file.Path.LocalPath;
        PushLiveReload();
        _console.Log("Saved " + file.Name, "OK");
    }

    private void PushLiveReload()
    {
        if (_projectPath is null) return;
        try
        {
            Services.LiveReload.PushScene(_projectPath, _scene);
            _console.Log("Live reload: pushed scene + token");
        }
        catch (Exception ex)
        {
            _console.Log("Live reload failed: " + ex.Message, "WARN");
        }
    }

    private async Task LoadSceneAsync()
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("Scene JSON") { Patterns = ["*.json"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;

        await using var stream = await file.OpenReadAsync();
        var loaded = await JsonSerializer.DeserializeAsync<SceneDocument>(stream);
        if (loaded is null) return;

        _scene = loaded;
        _selectedId = _scene.Objects.FirstOrDefault()?.Id;
        _history.Clear();
        _lastScenePath = file.Path.LocalPath;
        _console.Log("Loaded " + file.Name, "OK");
        RenderAll();
    }

    private async Task OpenProjectAsync()
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Open MonoGame Project Folder",
            AllowMultiple = false
        });
        var folder = folders.FirstOrDefault();
        if (folder?.Path.LocalPath is not { Length: > 0 } path) return;

        _projectPath = path;
        Title = $"{Path.GetFileName(path)} - MonoForge 0.2";
        _assets.LoadProject(path);
        _console.Log("Opened project: " + path);
        LoadWorkspace(path);
        Services.UserSettings.Current.PushRecentProject(path);
        UpdateStatus();
    }

    private void LoadWorkspace(string projectRoot)
    {
        var ws = ProjectWorkspace.Load(projectRoot);
        _sceneCanvas.Zoom = ws.Zoom;
        _sceneCanvas.Camera = new Vector(ws.CameraX, ws.CameraY);
        _sceneCanvas.ShowGrid = ws.ShowGrid;
        _sceneCanvas.SnapToGrid = ws.SnapToGrid;
        _sceneCanvas.InvalidateVisual();

        if (ws.Scene is { } embedded && embedded.Objects.Count > 0)
        {
            _scene = embedded;
            _selectedId = _scene.Objects.FirstOrDefault()?.Id;
            _history.Clear();
            _console.Log("Restored scene from workspace", "OK");
            RenderAll();
            return;
        }

        if (!string.IsNullOrWhiteSpace(ws.LastScenePath) && File.Exists(ws.LastScenePath))
        {
            try
            {
                var json = File.ReadAllText(ws.LastScenePath);
                if (JsonSerializer.Deserialize<SceneDocument>(json) is { } loaded)
                {
                    _scene = loaded;
                    _selectedId = _scene.Objects.FirstOrDefault()?.Id;
                    _history.Clear();
                    _console.Log("Restored scene: " + ws.LastScenePath, "OK");
                    RenderAll();
                }
            }
            catch (Exception ex)
            {
                _console.Log("Could not restore scene: " + ex.Message, "WARN");
            }
        }
    }

    private Avalonia.Threading.DispatcherTimer? _autoSaveTimer;

    private void StartAutoSave()
    {
        _autoSaveTimer?.Stop();
        var s = Services.UserSettings.Current;
        if (!s.AutoSaveEnabled || s.AutoSaveSeconds <= 0) return;
        _autoSaveTimer = new Avalonia.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Max(5, s.AutoSaveSeconds))
        };
        _autoSaveTimer.Tick += (_, _) =>
        {
            if (_projectPath is null) return;
            SaveWorkspace();
        };
        _autoSaveTimer.Start();
    }

    private void SaveWorkspace()
    {
        if (_projectPath is null) return;
        new ProjectWorkspace
        {
            Zoom = _sceneCanvas.Zoom,
            CameraX = _sceneCanvas.Camera.X,
            CameraY = _sceneCanvas.Camera.Y,
            ShowGrid = _sceneCanvas.ShowGrid,
            SnapToGrid = _sceneCanvas.SnapToGrid,
            LastScenePath = _lastScenePath ?? "",
            Scene = _scene
        }.Save(_projectPath);
    }

    private string? _lastScenePath;

    private void ScanTodos()
    {
        if (_projectPath is null) { _console.Log("Open a project first.", "WARN"); return; }
        _console.RebuildTodos(_projectPath, OpenFileAtLine);
        _console.Log("TODOs scanned.", "OK");
    }

    private void SyncContent()
    {
        if (_projectPath is null)
        {
            _console.Log("Open a project folder first.", "WARN");
            return;
        }

        var r = Services.MgcbSync.Sync(_projectPath, _scene);
        _console.Log($"Sync → {r.MgcbPath}", "OK");
        foreach (var c in r.Copied) _console.Log("Copied " + c, "OK");
        foreach (var p in r.AlreadyPresent) _console.Log("Up-to-date " + p);
        foreach (var e in r.Errors) _console.Log(e, "ERR");
    }

    private async Task ExportModelRuntimeAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "ForgedModel.cs",
            FileTypeChoices = [new FilePickerFileType("C#") { Patterns = ["*.cs"] }]
        });
        if (file is null) return;
        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(Services.MonoGameModelExporter.Emit("ForgedModel"));
        _console.Log("Wrote runtime model loader to " + file.Name, "OK");
        _console.Log("Add to your game .csproj: <PackageReference Include=\"SharpGLTF.Toolkit\" Version=\"1.0.6\" />");
    }

    private async Task ExportSceneAsync()
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            SuggestedFileName = "MonoForgeScene.generated.cs",
            FileTypeChoices = [new FilePickerFileType("C#") { Patterns = ["*.cs"] }]
        });
        if (file is null) return;

        await using var stream = await file.OpenWriteAsync();
        await using var writer = new StreamWriter(stream);
        await writer.WriteAsync(MonoGameSceneExporter.Export(_scene));
        _console.Log("Generated MonoGame C# scene file", "OK");
    }

    private async Task TogglePlay()
    {
        if (_isRunning)
        {
            _runCts?.Cancel();
            _isRunning = false;
            if (_runButton is not null) _runButton.Content = "▶ Play";
            _console.Log("Stopped game.", "WARN");
            return;
        }

        if (_projectPath is null) { _console.Log("Open a project first.", "WARN"); return; }
        var csproj = Directory.EnumerateFiles(_projectPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        if (csproj is null) { _console.Log("No .csproj found.", "ERR"); return; }

        PushLiveReload();
        _isRunning = true;
        if (_runButton is not null) _runButton.Content = "◼ Stop";
        _runCts = new CancellationTokenSource();
        _console.Log("Launching game with live reload watcher...", "OK");
        _console.ResetBuild();
        _console.FocusBuildTab();

        var token = _runCts.Token;
        var exit = await ProcessRunner.RunAsync(
            "dotnet",
            $"run --project \"{csproj}\"",
            _projectPath,
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)),
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)),
            token);

        _isRunning = false;
        if (_runButton is not null) _runButton.Content = "▶ Play";
        _console.Log($"Game exited with {exit}", exit == 0 ? "OK" : "ERR");
    }

    private async Task DotnetAsync(string verb)
    {
        if (_projectPath is null)
        {
            _console.Log("Open a project folder first.", "WARN");
            return;
        }

        var csproj = Directory.EnumerateFiles(_projectPath, "*.csproj", SearchOption.AllDirectories)
            .FirstOrDefault();
        if (csproj is null)
        {
            _console.Log("No .csproj found in " + _projectPath, "ERR");
            return;
        }

        _console.Log($"dotnet {verb} {csproj}");
        _console.ResetBuild();
        _console.FocusBuildTab();
        var exit = await ProcessRunner.RunAsync(
            "dotnet",
            $"{verb} \"{csproj}\"",
            _projectPath,
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)),
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)));
        _console.LogBuild($"--- dotnet {verb} exited with {exit} ---");
        _console.Log($"dotnet {verb} exited with {exit}", exit == 0 ? "OK" : "ERR");
    }

    private void OnAssetSelected(ProjectTreeNode node)
    {
        _console.Log("Selected asset: " + node.FullPath);
        _status.Text = node.FullPath;
        if (node.IsDirectory)
        {
            return;
        }

        var key = node.FullPath;
        _tabs.OpenOrFocus(key, node.Name, () =>
        {
            var content = AssetViewers.For(node, _pixelEditor, SetActiveColor);
            if (content is CodeEditor codeEditor)
            {
                codeEditor.DirtyChanged += editor => _tabs.SetDirty(editor.FilePath, editor.IsDirty);
            }
            return content;
        });
        Services.UserSettings.Current.PushRecentFile(node.FullPath);
    }

    private void FindAssetUsages(Models.ProjectTreeNode node)
    {
        var path = node.FullPath;
        var matches = _scene.Flatten().Where(o =>
            string.Equals(o.TexturePath, path, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(o.TilemapPath, path, StringComparison.OrdinalIgnoreCase) ||
            o.Components.Any(c => string.Equals(c.Source, path, StringComparison.OrdinalIgnoreCase))).ToList();

        if (matches.Count == 0)
        {
            _console.Log($"No usages of {Path.GetFileName(path)} found in scene.", "WARN");
            return;
        }

        _console.Log($"{matches.Count} usage(s) of {Path.GetFileName(path)}:", "OK");
        foreach (var m in matches) _console.Log("  · " + m.Name + "  (" + m.Id + ")");
        _sceneCanvas.SelectedIds.Clear();
        foreach (var m in matches) _sceneCanvas.SelectedIds.Add(m.Id);
        _selectedId = matches[0].Id;
        RenderAll();
    }

    private async Task RenameAssetAsync(Models.ProjectTreeNode node)
    {
        var dlg = new RenameDialog(Path.GetFileName(node.FullPath));
        var result = await dlg.ShowDialog<string?>(this);
        if (string.IsNullOrWhiteSpace(result) || result == Path.GetFileName(node.FullPath)) return;
        var newPath = Path.Combine(Path.GetDirectoryName(node.FullPath)!, result);
        try
        {
            if (node.IsDirectory) Directory.Move(node.FullPath, newPath);
            else File.Move(node.FullPath, newPath);
            _console.Log($"Renamed → {result}", "OK");
            if (_projectPath is not null) _assets.LoadProject(_projectPath);
        }
        catch (Exception ex)
        {
            _console.Log("Rename failed: " + ex.Message, "ERR");
        }
    }

    private void DeleteAsset(Models.ProjectTreeNode node)
    {
        try
        {
            if (node.IsDirectory) Directory.Delete(node.FullPath, recursive: true);
            else File.Delete(node.FullPath);
            _console.Log("Deleted " + node.Name, "WARN");
            if (_projectPath is not null) _assets.LoadProject(_projectPath);
        }
        catch (Exception ex)
        {
            _console.Log("Delete failed: " + ex.Message, "ERR");
        }
    }

    private void RevealInFinder(string path)
    {
        try
        {
            string fileName, args;
            if (OperatingSystem.IsMacOS()) { fileName = "open"; args = "-R \"" + path + "\""; }
            else if (OperatingSystem.IsWindows()) { fileName = "explorer"; args = "/select,\"" + path + "\""; }
            else { fileName = "xdg-open"; args = "\"" + Path.GetDirectoryName(path) + "\""; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false
            });
        }
        catch (Exception ex)
        {
            _console.Log("Reveal failed: " + ex.Message, "ERR");
        }
    }

    private void OpenGotoSymbol()
    {
        if (_tabs.ActiveContent is not CodeEditor editor)
        {
            _console.Log("Open a code file first.", "WARN");
            return;
        }
        var symbols = GotoSymbolWindow.Extract(editor.Text, editor.Language);
        if (symbols.Count == 0)
        {
            _console.Log("No symbols found.", "WARN");
            return;
        }
        var window = new GotoSymbolWindow(symbols, line => editor.GoToLine(line));
        window.Show(this);
    }

    private void OpenFindInFiles()
    {
        if (_projectPath is null) { _console.Log("Open a project first.", "WARN"); return; }
        new FindInFilesWindow(_projectPath, OpenFileAtLine).Show(this);
    }

    private void OpenQuickFile()
    {
        if (_projectPath is null)
        {
            _console.Log("Open a project folder first.", "WARN");
            return;
        }
        var files = EnumerateProjectFiles(_projectPath).ToList();
        var window = new QuickOpenWindow(files, picked => OpenFileAtLine(picked, 1));
        window.Show(this);
    }

    private static IEnumerable<string> EnumerateProjectFiles(string root)
    {
        var ignored = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "bin", "obj", ".git", ".vs", ".idea", ".vscode", "node_modules", "packages"
        };
        return EnumerateRecursive(new DirectoryInfo(root), 0);

        IEnumerable<string> EnumerateRecursive(DirectoryInfo dir, int depth)
        {
            if (depth > 8) yield break;
            IEnumerable<FileInfo> files;
            IEnumerable<DirectoryInfo> subs;
            try
            {
                files = dir.EnumerateFiles();
                subs = dir.EnumerateDirectories();
            }
            catch { yield break; }

            foreach (var f in files) yield return f.FullName;
            foreach (var sub in subs)
            {
                if (sub.Name.StartsWith('.') || ignored.Contains(sub.Name)) continue;
                foreach (var inner in EnumerateRecursive(sub, depth + 1)) yield return inner;
            }
        }
    }

    private void OpenFileAtLine(string path, int line)
    {
        var fullPath = path;
        if (!Path.IsPathRooted(fullPath) && _projectPath is not null)
        {
            fullPath = Path.Combine(_projectPath, path);
        }
        if (!File.Exists(fullPath))
        {
            _console.Log("Not found: " + fullPath, "WARN");
            return;
        }

        _tabs.OpenOrFocus(fullPath, Path.GetFileName(fullPath), () =>
        {
            var content = AssetViewers.For(new Models.ProjectTreeNode { Name = Path.GetFileName(fullPath), FullPath = fullPath, IsDirectory = false }, _pixelEditor, SetActiveColor);
            if (content is CodeEditor ce) ce.DirtyChanged += editor => _tabs.SetDirty(editor.FilePath, editor.IsDirty);
            return content;
        });

        if (_tabs.ActiveContent is CodeEditor active && active.FilePath == fullPath)
        {
            active.GoToLine(line);
        }
    }

    private bool TrySaveActiveDocument()
    {
        if (_tabs.ActiveContent is CodeEditor editor)
        {
            if (editor.Save())
            {
                _console.Log("Saved " + editor.FilePath, "OK");
                return true;
            }

            _console.Log("Failed to save " + editor.FilePath, "ERR");
            return true;
        }

        return false;
    }

    private void FrameScene()
    {
        _sceneCanvas.Zoom = 1;
        _sceneCanvas.Camera = new Vector(90, 78);
        _sceneCanvas.InvalidateVisual();
    }

    private void ToggleGrid()
    {
        _sceneCanvas.ShowGrid = !_sceneCanvas.ShowGrid;
        _sceneCanvas.InvalidateVisual();
        _console.Log("Grid: " + (_sceneCanvas.ShowGrid ? "on" : "off"));
    }

    private void ToggleSnap()
    {
        _sceneCanvas.SnapToGrid = !_sceneCanvas.SnapToGrid;
        _sceneCanvas.InvalidateVisual();
        _console.Log("Snap: " + (_sceneCanvas.SnapToGrid ? "on" : "off"));
    }

    private void SetTool(string tool)
    {
        _sceneCanvas.Tool = tool;
        _console.Log("Tool: " + tool);
        UpdateStatus();
        RefreshToolButtons();
    }

    private void RefreshToolButtons()
    {
        foreach (var (button, tool) in _toolButtons)
        {
            var active = tool == _sceneCanvas.Tool;
            button.Background = Brush(active ? Accent : "#00000000");
            button.Foreground = Brush(active ? TextPrimary : TextSecondary);
        }
    }

    private readonly List<(Button Button, string Tool)> _toolButtons = new();

    private void SetActiveColor(string color)
    {
        _activeColor = color;
        _pixelEditor.ActiveColor = color;
        if (SelectedObject() is { } selected)
        {
            CaptureHistory();
            selected.Color = color;
            _inspector.Update(selected);
            _sceneCanvas.InvalidateVisual();
        }
    }

    private SceneObject? SelectedObject()
    {
        return _selectedId is null ? null : _scene.Find(_selectedId);
    }

    private void UpdateStatus()
    {
        var tool = _sceneCanvas.Tool;
        var project = _projectPath ?? "(no project)";
        var selected = SelectedObject();
        var info = selected is null ? "" : $"   {selected.Name}  ({selected.X:0},{selected.Y:0})";
        _status.Text = $"{project}    tool: {tool}{info}";
    }

    private Button ToolButton(string label, string tool)
    {
        var button = MenuButton(label, (_, _) => SetTool(tool));
        _toolButtons.Add((button, tool));
        return button;
    }

    private static SceneDocument SeedScene()
    {
        return new SceneDocument
        {
            Name = "main.collection",
            Objects =
            [
                new SceneObject { Id = "player", Name = "Player", Type = "Sprite", X = 120, Y = 96, Width = 64, Height = 64, Color = "#65a7ff", Layer = 2 },
                new SceneObject { Id = "crate", Name = "Crate", Type = "Sprite", X = 320, Y = 160, Width = 96, Height = 72, Color = "#c7a76c", Layer = 1 },
                new SceneObject { Id = "spawn", Name = "SpawnPoint", Type = "Marker", X = 96, Y = 260, Width = 32, Height = 32, Color = "#7bd88f", Layer = 3 }
            ]
        };
    }
}
