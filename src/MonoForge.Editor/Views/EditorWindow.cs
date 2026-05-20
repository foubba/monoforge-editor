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
    private readonly ContentControl _rightDockHost = new();
    private readonly Panels.LayersPanel _layers = new();
    private Control? _sceneDock; // cached scene-specific right dock (Outline+Inspector+Layers+SpritePane)
    private Grid? _workspaceGrid;
    private Control? _toolbar; // scene-tool toolbar (Select/Move/Snap/Group/Align/...) — hidden for non-scene tabs
    private readonly Panels.AiAssistantPanel _aiPanel = new();
    // Sidebar is visible by default so the user sees the Install/Auth path without
    // having to discover the ⌘⇧A shortcut first.
    private bool _aiPanelVisible = true;
    private bool _dockVisible = true;
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
    // Resolved executable .csproj for the current project (e.g. "Game.DesktopGL.csproj").
    // Multi-project solutions usually have a shared library + one or more platform targets;
    // this is the one we hand to `dotnet run`. null until OpenProjectFromPath finds one.
    private string? _runnableCsproj;
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
        try { Icon = new WindowIcon(MonoForgeLogo.RenderToBitmap(64)); } catch { /* icon is decorative */ }
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
        // Stream each parsed compiler diagnostic into the matching open code tab so the
        // user sees red squiggles inline and red markers in the minimap. We accumulate
        // them while a build runs and replace the editor's set on each diagnostic so the
        // UI updates progressively rather than only after the whole build finishes.
        _console.DiagnosticParsed += (path, line, severity, code, msg) =>
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() => PushDiagnostic(path, line, severity, code, msg));
        };
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
        _toolbar = BuildToolbar();
        root.Children.Add(_toolbar.At(row: 1));
        root.Children.Add(BuildWorkspace().At(row: 2));
        root.Children.Add(BuildStatusBar().At(row: 3));
        return root;
    }

    private Control BuildMenuBar()
    {
        var bar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Background = Brush(MenuBackground)
        };

        var left = RowStack(MenuBackground);
        // Inline logo — sized to roughly match the wordmark cap-height, rendered with
        // the light palette so it reads against the dark menu strip.
        left.Children.Add(new MonoForgeLogo(scale: 0.32, light: true)
        {
            Margin = new Thickness(10, 0, 6, 0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        });
        left.Children.Add(Text("MonoForge", TextPrimary, FontWeight.Bold));

        // All top-level items share a single Menu so the user can hover between File →
        // Edit → View etc. and the dropdown follows the cursor (native Avalonia behavior
        // is only available within one Menu container).
        var menus = new Menu { Background = Avalonia.Media.Brushes.Transparent };
        menus.Items.Add(BuildFileMenu());
        menus.Items.Add(BuildEditMenu());
        menus.Items.Add(BuildViewMenu());
        menus.Items.Add(BuildProjectMenu());
        menus.Items.Add(BuildDebugMenu());
        menus.Items.Add(BuildHelpMenu());
        left.Children.Add(menus);

        // Play lives in the menu bar's wide center column so it sits in the dark strip
        // (rather than over the lighter workspace background). Disabled until a project
        // is loaded so the user can't trigger a build with nothing to build.
        _runButton = PrimaryButton("▶ Play", async (_, _) => await TogglePlay());
        _runButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
        _runButton.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
        _runButton.IsEnabled = false;

        bar.Children.Add(left.At(column: 0));
        bar.Children.Add(_runButton.At(column: 1));
        return bar;
    }

    private MenuItem BuildFileMenu()
    {
        // Pre-refactor each Build*Menu returned its own Menu control. To enable native
        // hover-to-switch behavior between top-level items (File → Edit when the user
        // drags across the menu bar), they now return a single MenuItem and BuildMenuBar
        // composes them into one Menu.
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

        var openSceneTab = new MenuItem { Header = "Open Scene Tab" };
        openSceneTab.Click += (_, _) => OpenSceneTab();
        file.Items.Add(openSceneTab);

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

        return file;
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
        // Try to identify the runnable .csproj so Play knows what to launch. The button
        // only enables if we actually found something with OutputType=Exe (or WinExe).
        _runnableCsproj = FindRunnableCsproj(path);
        _aiPanel.ProjectPath = path;
        if (_runButton is not null) _runButton.IsEnabled = _runnableCsproj is not null;
        if (_runnableCsproj is not null)
            _console.Log("Runnable project detected: " + Path.GetRelativePath(path, _runnableCsproj), "OK");
        else
            _console.Log("No runnable .csproj found (no <OutputType>Exe</OutputType>). Play stays disabled.", "WARN");
        _console.Log("Opened project: " + path);
        LoadWorkspace(path);
        Services.UserSettings.Current.PushRecentProject(path);
        UpdateStatus();
        await Task.CompletedTask;
    }

    private MenuItem BuildEditMenu()
    {
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
        return edit;
    }

    private MenuItem BuildViewMenu()
    {
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
        // Panel-visibility toggles. The header text reflects current state so the user
        // can tell what hitting it will do without having to hover/check the layout.
        var aiToggle = new MenuItem { Header = _aiPanelVisible ? "Hide AI Sidebar   ⌘⇧A" : "Show AI Sidebar   ⌘⇧A" };
        aiToggle.Click += (_, _) =>
        {
            ToggleAiPanel();
            aiToggle.Header = _aiPanelVisible ? "Hide AI Sidebar   ⌘⇧A" : "Show AI Sidebar   ⌘⇧A";
        };
        view.Items.Add(frame);
        view.Items.Add(new Separator());
        view.Items.Add(grid); view.Items.Add(snap); view.Items.Add(snapToObj); view.Items.Add(pixel);
        view.Items.Add(new Separator());
        view.Items.Add(aiToggle);
        return view;
    }

    private MenuItem BuildProjectMenu()
    {
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
        return proj;
    }

    private MenuItem BuildDebugMenu()
    {
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
        var palette = new MenuItem { Header = "Command Palette...   ⌘⇧P" };
        palette.Click += (_, _) => OpenCommandPalette();
        dbg.Items.Add(build); dbg.Items.Add(run); dbg.Items.Add(push);
        dbg.Items.Add(new Separator());
        dbg.Items.Add(palette); dbg.Items.Add(findFiles); dbg.Items.Add(quick); dbg.Items.Add(symbol);
        return dbg;
    }

    private MenuItem BuildHelpMenu()
    {
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
        return help;
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
        // Columns: assets | split | center | split | rightDock | split | AiSidebar.
        // Widths for the right dock and the AI sidebar are toggled at runtime; the AI
        // sidebar starts collapsed (0px), opens to ~360px when the user hits ⌘⇧A.
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions(ComposeWorkspaceColumns()),
            Background = Brush(EditorBackground)
        };

        grid.Children.Add(_assets.At(column: 0));
        grid.Children.Add(VSplitter().At(column: 1));
        grid.Children.Add(BuildCenter().At(column: 2));
        grid.Children.Add(VSplitter().At(column: 3));
        grid.Children.Add(BuildRightDock().At(column: 4));
        // Custom drag handle for the AI panel left edge. Avalonia's GridSplitter doesn't
        // reliably resize the last column when it sits between two Pixel widths plus a
        // Star center column; this handle pushes width changes directly into both
        // adjacent pixel columns so growing the AI panel always works.
        grid.Children.Add(BuildAiResizeHandle().At(column: 5));
        grid.Children.Add(_aiPanel.At(column: 6));
        _aiPanel.ProjectMutated += () =>
        {
            if (_projectPath is not null) _assets.LoadProject(_projectPath);
        };
        _workspaceGrid = grid;
        ApplyWorkspaceMinimums();
        return grid;
    }

    /// <summary>Apply minimum widths to each workspace column so a splitter drag can't
    /// collapse panels to zero and panels can always be grown back.</summary>
    private void ApplyWorkspaceMinimums()
    {
        if (_workspaceGrid is null || _workspaceGrid.ColumnDefinitions.Count < 7) return;
        _workspaceGrid.ColumnDefinitions[0].MinWidth = 180; // assets
        _workspaceGrid.ColumnDefinitions[2].MinWidth = 300; // center editor
        if (_dockVisible) _workspaceGrid.ColumnDefinitions[4].MinWidth = 220;
        if (_aiPanelVisible) _workspaceGrid.ColumnDefinitions[6].MinWidth = 240;
    }

    /// <summary>Compose the workspace ColumnDefinitions spec from the two visibility flags.</summary>
    private string ComposeWorkspaceColumns()
    {
        // 6px splitters match VSplitter().Width — narrower splitters were hard to grab.
        var dock = _dockVisible ? "6,282" : "0,0";
        var ai = _aiPanelVisible ? "6,360" : "0,0";
        return $"266,6,*,{dock},{ai}";
    }

    private void RefreshWorkspaceColumns()
    {
        if (_workspaceGrid is null) return;
        var spec = ComposeWorkspaceColumns();
        if (_workspaceGrid.ColumnDefinitions.ToString() != spec)
        {
            _workspaceGrid.ColumnDefinitions = new ColumnDefinitions(spec);
            ApplyWorkspaceMinimums();
        }
    }

    /// <summary>Open / close the right-edge Claude sidebar (Cursor-style).</summary>
    private void ToggleAiPanel()
    {
        _aiPanelVisible = !_aiPanelVisible;
        RefreshWorkspaceColumns();
        if (_aiPanelVisible) _aiPanel.Focus();
    }

    private Control BuildCenter()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("*,4,272"),
            Background = Brush(EditorBackground)
        };
        _tabs.EmptyContent = new StartPageView();
        _tabs.ActiveChanged += UpdateContextPanel;
        // Start with no document open; the user reopens the scene from the menu / palette.
        UpdateContextPanel(null);
        grid.Children.Add(_tabs.At(row: 0));
        grid.Children.Add(HSplitter().At(row: 1));
        grid.Children.Add(_console.At(row: 2));
        return grid;
    }

    /// <summary>
    /// Swap the right-dock based on the active document. Each document type gets the
    /// panels that actually relate to it — sprite scenes get the full sprite stack
    /// (Outline + Inspector + Layers + Stats + Sprite pane); GLBs get the 3D Tools
    /// palette; code (and anything else) collapses the dock entirely since there's
    /// nothing scene-specific to show.
    /// </summary>
    private void UpdateContextPanel(Control? active)
    {
        var isScene = active is SceneCanvas;
        if (_toolbar is not null) _toolbar.IsVisible = isScene; // scene-only tools row

        if (isScene)
        {
            _sceneDock ??= BuildSceneDock();
            _rightDockHost.Content = _sceneDock;
            SetWorkspaceDockVisible(true);
        }
        else if (active is Model3DViewer viewer)
        {
            _rightDockHost.Content = BorderBox(viewer.BuildContextPanel(), BorderSubtle, 1, 0, 0, 0);
            SetWorkspaceDockVisible(true);
        }
        else
        {
            // null (no tabs) or code editor / other → no contextual panel; collapse.
            _rightDockHost.Content = null;
            SetWorkspaceDockVisible(false);
        }
    }

    private void SetWorkspaceDockVisible(bool visible)
    {
        if (_dockVisible == visible) return;
        _dockVisible = visible;
        RefreshWorkspaceColumns();
    }

    /// <summary>Right dock layout used while a sprite scene tab is active.</summary>
    private Control BuildSceneDock()
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

    private Control BuildRightDock() => _rightDockHost;

    /// <summary>
    /// Bespoke vertical drag handle that resizes the AI panel column. Uses raw pointer
    /// capture instead of GridSplitter — the latter was silently dropping resize events
    /// for the last column when sat between Pixel and Star definitions. This implementation
    /// borrows from the right dock when growing and gives back when shrinking, respecting
    /// the configured MinWidths on both columns.
    /// </summary>
    private Control BuildAiResizeHandle()
    {
        var handle = new Border
        {
            Background = Brush(BorderSubtle),
            Cursor = new Cursor(StandardCursorType.SizeWestEast),
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
        };

        var dragging = false;
        double startX = 0;
        double startRightDock = 0;
        double startAi = 0;

        handle.PointerEntered += (_, _) =>
        {
            if (!dragging) handle.Background = Brush(Accent);
        };
        handle.PointerExited += (_, _) =>
        {
            if (!dragging) handle.Background = Brush(BorderSubtle);
        };

        handle.PointerPressed += (_, e) =>
        {
            if (_workspaceGrid is null) return;
            startX = e.GetPosition(_workspaceGrid).X;
            startRightDock = _workspaceGrid.ColumnDefinitions[4].ActualWidth;
            startAi = _workspaceGrid.ColumnDefinitions[6].ActualWidth;
            dragging = true;
            e.Pointer.Capture(handle);
            handle.Background = Brush(Accent);
        };

        handle.PointerMoved += (_, e) =>
        {
            if (!dragging || _workspaceGrid is null) return;
            // Positive delta = mouse moved left = AI panel grows.
            var delta = startX - e.GetPosition(_workspaceGrid).X;
            const double aiMin = 240;
            const double rightDockMin = 180;
            var newAi = Math.Max(aiMin, startAi + delta);
            // Distribute width changes: shrink right dock to feed AI (with floor), and
            // vice versa when growing right dock back.
            var aiDelta = newAi - startAi;
            var newRightDock = Math.Max(rightDockMin, startRightDock - aiDelta);
            // If right dock hit its floor, clamp AI growth so we don't overflow.
            var actualRightDockShrink = startRightDock - newRightDock;
            newAi = startAi + actualRightDockShrink;

            _workspaceGrid.ColumnDefinitions[4].Width = new GridLength(newRightDock);
            _workspaceGrid.ColumnDefinitions[6].Width = new GridLength(newAi);
        };

        handle.PointerReleased += (_, e) =>
        {
            dragging = false;
            e.Pointer.Capture(null);
            handle.Background = Brush(BorderSubtle);
        };

        return handle;
    }

    private static GridSplitter VSplitter()
    {
        return new GridSplitter
        {
            Background = Brush(BorderSubtle),
            Width = 6,
            ResizeDirection = GridResizeDirection.Columns,
            ResizeBehavior = GridResizeBehavior.PreviousAndNext,
            ShowsPreview = false,
            Cursor = new Cursor(StandardCursorType.SizeWestEast)
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
            if (meta && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.P)
            {
                OpenCommandPalette(); e.Handled = true; return;
            }
            if (meta && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.A)
            {
                ToggleAiPanel(); e.Handled = true; return;
            }
            if (meta && e.Key == Key.P) { OpenQuickFile(); e.Handled = true; return; }
            if (meta && e.KeyModifiers.HasFlag(KeyModifiers.Shift) && e.Key == Key.F)
            {
                OpenFindInFiles(); e.Handled = true; return;
            }
            if (meta && e.Key == Key.R) { _ = DotnetAsync("run"); e.Handled = true; return; }
            if (meta && (e.Key == Key.Enter || e.Key == Key.Return)) { _ = RunCurrentFileAsync(); e.Handled = true; return; }
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

        // Route through the unified entry point so runnable-csproj detection, Play-button
        // enabling, recent-projects, and workspace-load all run in one place.
        await OpenProjectFromPath(path);
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

    /// <summary>
    /// Find every open CodeEditor tab whose path matches and add a diagnostic to it,
    /// then push the updated set so the squiggle + minimap marker show up.
    /// </summary>
    private void PushDiagnostic(string filePath, int line, string severity, string code, string msg)
    {
        // The msbuild path may be absolute or workspace-relative; normalize both.
        var normalized = Path.GetFullPath(filePath, _projectPath ?? Environment.CurrentDirectory);
        foreach (var content in _tabs.OpenContents)
        {
            if (content is not CodeEditor ed) continue;
            if (!string.Equals(ed.FilePath, normalized, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(ed.FilePath, filePath, StringComparison.OrdinalIgnoreCase)) continue;
            ed.AddDiagnostic(new DiagnosticInfo(line, severity, code, msg));
            return;
        }
    }

    /// <summary>Clear diagnostics on every open code tab. Called before each build.</summary>
    private void ClearAllDiagnostics()
    {
        foreach (var content in _tabs.OpenContents)
        {
            if (content is CodeEditor ed) ed.ClearDiagnostics();
        }
    }

    /// <summary>
    /// Save every dirty CodeEditor tab to disk and return how many were written. Called
    /// before build/run so the compiler sees the user's latest changes — without this,
    /// edits in unsaved tabs are invisible to dotnet and "the build passed" lies.
    /// </summary>
    private int SaveAllDirtyEditors()
    {
        var count = 0;
        foreach (var content in _tabs.OpenContents)
        {
            if (content is CodeEditor ed && ed.IsDirty && ed.Save())
            {
                _tabs.SetDirty(ed.FilePath, false);
                count++;
            }
        }
        return count;
    }

    /// <summary>
    /// Walk the project tree looking for a .csproj that declares OutputType=Exe (or WinExe).
    /// In a typical multi-project MonoGame solution this is the platform target (e.g.
    /// Game.DesktopGL/Game.DesktopGL.csproj) — the shared library lacks OutputType and so
    /// is correctly skipped.
    /// </summary>
    private static string? FindRunnableCsproj(string projectRoot)
    {
        IEnumerable<string> all;
        try { all = Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.AllDirectories); }
        catch { return null; }

        string? fallback = null;
        foreach (var csproj in all)
        {
            fallback ??= csproj;
            string text;
            try { text = File.ReadAllText(csproj); }
            catch { continue; }
            // Quick string check is enough — XML parsing would be overkill for this hint.
            if (text.Contains("<OutputType>Exe</OutputType>", StringComparison.OrdinalIgnoreCase) ||
                text.Contains("<OutputType>WinExe</OutputType>", StringComparison.OrdinalIgnoreCase))
            {
                return csproj;
            }
        }
        // No executable found → return null. The caller decides what to do (we leave Play
        // disabled rather than guess a library project that would just fail at runtime).
        return null;
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
        var csproj = _runnableCsproj;
        if (csproj is null) { _console.Log("No runnable .csproj (OutputType=Exe) in this project.", "ERR"); return; }

        // Same as Build: save every dirty editor before launching so the compiler sees
        // the user's pending changes.
        var savedForRun = SaveAllDirtyEditors();
        if (savedForRun > 0) _console.Log($"Saved {savedForRun} editor{(savedForRun == 1 ? "" : "s")} before run.");
        ClearAllDiagnostics();

        // Play is intentionally a clean `dotnet run` — equivalent to running the game
        // from the command line. The live-reload push has its own ⌘E shortcut and menu
        // entry, so the user opts into it explicitly instead of paying the cost on every
        // Play (it can fail silently in projects without the runtime helper installed).
        _isRunning = true;
        if (_runButton is not null) _runButton.Content = "◼ Stop";
        _runCts = new CancellationTokenSource();
        _console.Log($"dotnet run --project {Path.GetFileName(csproj)}", "OK");
        _console.ResetBuild();
        _console.FocusBuildTab();

        var token = _runCts.Token;
        // Same flags as Build (see DotnetAsync) — disable terminal logger so the regex can
        // pick up compile-time errors emitted during the implicit build phase of `run`.
        var exit = await ProcessRunner.RunAsync(
            "dotnet",
            $"run --project \"{csproj}\" -tl:off -nologo -v minimal",
            _projectPath,
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)),
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuildError(line)),
            token);

        _isRunning = false;
        if (_runButton is not null) _runButton.Content = "▶ Play";
        var (rErrs, rWarns) = _console.BuildCounts;
        _console.Log($"Game exited with {exit}  ({rErrs} error{(rErrs == 1 ? "" : "s")}, {rWarns} warning{(rWarns == 1 ? "" : "s")})", exit == 0 && rErrs == 0 ? "OK" : "ERR");
    }

    /// <summary>
    /// Run the script in the currently-focused code tab. Today supports Python (.py via
    /// `python3`) and shell scripts (.sh / .bash). Output is piped live to the Build tab.
    /// For C# files inside a .csproj project, the user should use Build/Run (⌘B / ⌘R)
    /// instead — we surface a friendly hint in that case.
    /// </summary>
    private async Task RunCurrentFileAsync()
    {
        if (_tabs.ActiveContent is not CodeEditor ed)
        {
            _console.Log("Open a code file first.", "WARN");
            return;
        }
        var path = ed.FilePath;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        string exe; string args;
        switch (ext)
        {
            case ".py":
                exe = "python3"; args = $"\"{path}\""; break;
            case ".sh": case ".bash":
                exe = "bash"; args = $"\"{path}\""; break;
            case ".js":
                exe = "node"; args = $"\"{path}\""; break;
            case ".cs":
                _console.Log("⌘B / ⌘R compiles and runs the C# project. Run-current-file is for scripts (.py, .sh, .js).", "WARN");
                return;
            default:
                _console.Log($"No runner registered for '{ext}'. Use ⌘B for C# projects.", "WARN");
                return;
        }

        _console.Log($"{exe} {Path.GetFileName(path)}");
        _console.ResetBuild();
        _console.FocusBuildTab();
        var cwd = Path.GetDirectoryName(path) ?? _projectPath ?? Environment.CurrentDirectory;
        var exit = await ProcessRunner.RunAsync(
            exe, args, cwd,
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)),
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)));
        _console.LogBuild($"--- {exe} exited with {exit} ---");
        _console.Log($"{exe} {Path.GetFileName(path)} exited with {exit}", exit == 0 ? "OK" : "ERR");
    }

    private async Task DotnetAsync(string verb)
    {
        if (_projectPath is null)
        {
            _console.Log("Open a project folder first.", "WARN");
            return;
        }

        // Prefer the runnable csproj so `dotnet build` targets the same project as Play.
        // Falls back to the first .csproj if no Exe was detected (still useful for libs).
        var csproj = _runnableCsproj ?? Directory.EnumerateFiles(_projectPath, "*.csproj", SearchOption.AllDirectories).FirstOrDefault();
        if (csproj is null)
        {
            _console.Log("No .csproj found in " + _projectPath, "ERR");
            return;
        }

        // Persist every dirty code tab to disk before invoking dotnet — otherwise the
        // compiler reads stale files and "everything's fine!" while the editor shows
        // broken code. Tracks saved file count for the log.
        var savedCount = SaveAllDirtyEditors();
        if (savedCount > 0) _console.Log($"Saved {savedCount} editor{(savedCount == 1 ? "" : "s")} before build.");
        // Wipe stale squiggles from the previous build so old errors don't linger after
        // they've been fixed.
        ClearAllDiagnostics();
        _console.Log($"dotnet {verb} {Path.GetFileName(csproj)}");
        _console.ResetBuild();
        _console.FocusBuildTab();
        // Disable the modern Terminal Logger so MSBuild emits parseable per-line diagnostics
        // instead of the ANSI/progress-bar pretty output (which our error regex can't read).
        // -nologo trims the version banner, -v normal makes sure each diagnostic line is
        // printed, --no-restore is safe because Run does its own restore step.
        var exit = await ProcessRunner.RunAsync(
            "dotnet",
            $"{verb} \"{csproj}\" -tl:off -nologo -v minimal",
            _projectPath,
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuild(line)),
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => _console.LogBuildError(line)));
        var (errs, warns) = _console.BuildCounts;
        _console.LogBuild($"--- dotnet {verb} exited with code {exit}  ({errs} error{(errs == 1 ? "" : "s")}, {warns} warning{(warns == 1 ? "" : "s")}) ---");
        var status = exit == 0 && errs == 0 ? "OK" : "ERR";
        _console.Log($"dotnet {verb}: {errs} error(s), {warns} warning(s), exit {exit}", status);
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

    private void OpenCommandPalette()
    {
        var palette = new CommandPaletteWindow(BuildCommandList());
        palette.Show(this);
    }

    /// <summary>Bring the sprite scene back as a tab (used after closing it from the start page).</summary>
    private void OpenSceneTab()
    {
        _tabs.OpenOrFocus(SceneTabKey, _scene.Name, () => _sceneCanvas);
    }

    /// <summary>
    /// Single source of truth for every action the user can invoke via ⌘⇧P.
    /// Mirrors the menu structure but flattens it; categories double as section labels.
    /// </summary>
    private IEnumerable<EditorCommand> BuildCommandList()
    {
        // File
        yield return new EditorCommand("New Project…", "File", "", async () =>
        {
            var dlg = new NewProjectDialog();
            await dlg.ShowDialog(this);
            if (dlg.CreatedAt is { } path) { await OpenProjectFromPath(path); _console.Log("Created project: " + path, "OK"); }
        });
        yield return new EditorCommand("Open Project…", "File", "", async () => await OpenProjectAsync());
        yield return new EditorCommand("Open Scene Tab", "File", "", OpenSceneTab);
        yield return new EditorCommand("Save Scene", "File", "⌘S", async () => { if (!TrySaveActiveDocument()) await SaveSceneAsync(); });
        yield return new EditorCommand("Load Scene…", "File", "", async () => await LoadSceneAsync());
        yield return new EditorCommand("Quick Open File…", "File", "⌘P", OpenQuickFile);

        // Edit
        yield return new EditorCommand("Undo", "Edit", "⌘Z", Undo);
        yield return new EditorCommand("Redo", "Edit", "⌘⇧Z", Redo);
        yield return new EditorCommand("Duplicate Selected", "Edit", "⌘D", DuplicateSelected);
        yield return new EditorCommand("Delete Selected", "Edit", "⌫", DeleteSelected);
        yield return new EditorCommand("Group Selection", "Edit", "⌘G", GroupSelection);
        yield return new EditorCommand("Ungroup Selection", "Edit", "", UngroupSelection);
        yield return new EditorCommand("Bring to Front", "Edit", "⌘]", BringToFront);
        yield return new EditorCommand("Send to Back", "Edit", "⌘[", SendToBack);
        yield return new EditorCommand("Z-Order Forward", "Edit", "⌘⇧]", ZOrderForward);
        yield return new EditorCommand("Z-Order Backward", "Edit", "⌘⇧[", ZOrderBackward);
        yield return new EditorCommand("Flip Horizontal", "Edit", "H", () => FlipSelected(horizontal: true));
        yield return new EditorCommand("Flip Vertical", "Edit", "V", () => FlipSelected(horizontal: false));

        // View
        yield return new EditorCommand("Frame Scene", "View", "F", FrameScene);
        yield return new EditorCommand("Toggle Grid", "View", "G", ToggleGrid);
        yield return new EditorCommand("Toggle Snap", "View", "", ToggleSnap);
        yield return new EditorCommand("Toggle Pixel-Perfect", "View", "", () => { _sceneCanvas.PixelPerfect = !_sceneCanvas.PixelPerfect; _console.Log("Pixel-perfect: " + (_sceneCanvas.PixelPerfect ? "on" : "off")); });
        yield return new EditorCommand("Toggle Snap to Objects", "View", "", () => { _sceneCanvas.SnapToObjects = !_sceneCanvas.SnapToObjects; _console.Log("Snap to objects: " + (_sceneCanvas.SnapToObjects ? "on" : "off")); });

        // Tools
        yield return new EditorCommand("Tool: Select", "Tool", "S", () => SetTool("select"));
        yield return new EditorCommand("Tool: Move", "Tool", "M", () => SetTool("move"));
        yield return new EditorCommand("Tool: Rect", "Tool", "R", () => SetTool("rect"));

        // Project
        yield return new EditorCommand("Atlas Packer…", "Project", "", () => new AtlasWindow().Show());
        yield return new EditorCommand("Tilemap Editor…", "Project", "", () => new TilemapEditor().Show());
        yield return new EditorCommand("Animation Editor…", "Project", "", () => new AnimationEditor().Show());
        yield return new EditorCommand("Particle Editor…", "Project", "", () => new ParticleEditor().Show());
        yield return new EditorCommand("Sync to Content/", "Project", "", SyncContent);
        yield return new EditorCommand("Scan TODOs", "Project", "", ScanTodos);
        yield return new EditorCommand("Export Scene as C#…", "Project", "", async () => await ExportSceneAsync());
        yield return new EditorCommand("Emit GLB Runtime Loader…", "Project", "", async () => await ExportModelRuntimeAsync());
        yield return new EditorCommand("Preferences…", "Project", "", () => new SettingsWindow().Show());

        // Debug / build
        yield return new EditorCommand("Build", "Debug", "⌘B", async () => await DotnetAsync("build"));
        yield return new EditorCommand("Run with Live Reload", "Debug", "⌘R", async () => await TogglePlay());
        yield return new EditorCommand("Run Current File", "Debug", "⌘↩", async () => await RunCurrentFileAsync());
        yield return new EditorCommand("Push Live Reload", "Debug", "⌘E", PushLiveReload);

        // Code navigation
        yield return new EditorCommand("Find in Files…", "Code", "⌘⇧F", OpenFindInFiles);
        yield return new EditorCommand("Goto Symbol…", "Code", "⌘T", OpenGotoSymbol);
        yield return new EditorCommand("Toggle AI Assistant", "Code", "⌘⇧A", ToggleAiPanel);

        // Help
        yield return new EditorCommand("Show Keyboard Shortcuts", "Help", "", ShowShortcuts);
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
