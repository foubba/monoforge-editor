using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views.Panels;

public sealed class ConsolePanel : UserControl
{
    // Match MSBuild diagnostic lines like:
    //   Program.cs(15,9): error CS0103: The name 'foo' does not exist [/path/to/proj.csproj]
    //   /abs/Program.cs(15): warning CS0168: ...
    //       1>Program.cs(15,9): error CS0103: ...    (parallel-build node prefix)
    // Allows leading whitespace + optional `<digits>>` node id (terminal-logger pads them),
    // optional column, and an optional trailing project hint in square brackets.
    private static readonly Regex BuildMessageRegex = new(
        @"^\s*(?:\d+>)?(?<path>[^()]+\.[a-zA-Z]+)\((?<line>\d+)(?:,(?<col>\d+))?\):\s+(?<kind>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<msg>.+?)\s*(\[.*\])?$",
        RegexOptions.Compiled);

    public event Action<string, int>? BuildErrorClicked;
    public event Action<string, int>? TodoClicked;
    /// <summary>Fires for every recognized build diagnostic line. (path, line, severity, code, message).</summary>
    public event Action<string, int, string, string, string>? DiagnosticParsed;

    private readonly TextBlock _consoleOutput = new();
    private readonly StackPanel _buildList = new() { Spacing = 0, Margin = new Thickness(8, 0, 16, 0) };
    private readonly StackPanel _todoList = new() { Spacing = 0, Margin = new Thickness(8, 0, 16, 0) };
    private readonly List<(string Stamp, string Kind, string Msg)> _consoleLines = new();
    private string _consoleFilter = "";
    private string _consoleKindFilter = "ALL";
    private readonly ScrollViewer _consoleScroll;
    private readonly ScrollViewer _buildScroll;
    private readonly ScrollViewer _todoScroll;
    private readonly Grid _tabsRow;
    private readonly Grid _bodyHost;
    private string _activeTab = "console";
    private int _buildErrors;
    private int _buildWarnings;

    public ConsolePanel()
    {
        var grid = new Grid
        {
            RowDefinitions = new RowDefinitions("38,32,*"),
            Background = Brush(ConsoleBackground)
        };

        _tabsRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*,Auto"),
            Background = Brush(ConsoleBackground)
        };
        RebuildTabs();
        grid.Children.Add(_tabsRow.At(row: 0));

        grid.Children.Add(BuildFilterBar().At(row: 1));

        ConfigureOutput(_consoleOutput);

        _consoleScroll = new ScrollViewer
        {
            Content = _consoleOutput,
            Background = Brush(ConsoleSurface)
        };
        _buildScroll = new ScrollViewer
        {
            Content = _buildList,
            Background = Brush(ConsoleSurface)
        };
        _todoScroll = new ScrollViewer
        {
            Content = _todoList,
            Background = Brush(ConsoleSurface)
        };

        _bodyHost = new Grid { Background = Brush(ConsoleSurface) };
        _bodyHost.Children.Add(_consoleScroll);
        grid.Children.Add(_bodyHost.At(row: 2));

        Content = BorderBox(grid, BorderSubtle, 1, 1, 1, 0);
    }

    private Control BuildFilterBar()
    {
        var grid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("90,*,40,40,60"),
            Background = Brush(PanelBackground),
            Margin = new Thickness(12, 4, 12, 4),
            ColumnSpacing = 6
        };

        var levelBtn = new Button
        {
            Content = "All ▼",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextMuted),
            FontSize = 12,
            Padding = new Thickness(8, 4)
        };
        var levelMenu = new ContextMenu();
        foreach (var lvl in new[] { "All", "INFO", "OK", "WARN", "ERR" })
        {
            var item = new MenuItem { Header = lvl };
            var captured = lvl;
            item.Click += (_, _) =>
            {
                _consoleKindFilter = captured.ToUpperInvariant();
                levelBtn.Content = captured + " ▼";
                RebuildConsoleOutput();
            };
            levelMenu.Items.Add(item);
        }
        levelBtn.ContextMenu = levelMenu;
        levelBtn.Click += (s, _) => { if (s is Button b) b.ContextMenu?.Open(b); };
        grid.Children.Add(levelBtn.At(column: 0));

        var search = new TextBox
        {
            Watermark = "Filter lines…",
            Background = Brush(InputBackgroundAlt),
            Foreground = Brush(TextMuted),
            BorderBrush = Brush(BorderColor),
            FontFamily = FontFamily.Parse("Menlo"),
            FontSize = 12,
            Padding = new Thickness(10, 4)
        };
        search.TextChanged += (_, _) => { _consoleFilter = search.Text ?? ""; RebuildConsoleOutput(); };
        grid.Children.Add(search.At(column: 1));

        var prev = new Button
        {
            Content = "‹",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextMuted),
            FontSize = 14,
            Padding = new Thickness(8, 2)
        };
        prev.Click += (_, _) => _consoleScroll.LineUp();
        grid.Children.Add(prev.At(column: 2));

        var next = new Button
        {
            Content = "›",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextMuted),
            FontSize = 14,
            Padding = new Thickness(8, 2)
        };
        next.Click += (_, _) => _consoleScroll.LineDown();
        grid.Children.Add(next.At(column: 3));

        var clear = new Button
        {
            Content = "Clear",
            Background = Brush(FilterBackground),
            BorderBrush = Brush(FilterBackground),
            Foreground = Brush(TextMuted),
            FontSize = 12,
            Padding = new Thickness(8, 4)
        };
        clear.Click += (_, _) => ClearActive();
        grid.Children.Add(clear.At(column: 4));

        return grid;
    }

    private void ClearActive()
    {
        if (_activeTab == "build") { _buildList.Children.Clear(); _buildErrors = 0; _buildWarnings = 0; RebuildTabs(); }
        else if (_activeTab == "todos") _todoList.Children.Clear();
        else { _consoleLines.Clear(); RebuildConsoleOutput(); }
    }

    private void RebuildConsoleOutput()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var (stamp, kind, msg) in _consoleLines)
        {
            if (_consoleKindFilter != "ALL" && _consoleKindFilter != kind) continue;
            if (!string.IsNullOrEmpty(_consoleFilter) && !msg.Contains(_consoleFilter, StringComparison.OrdinalIgnoreCase)) continue;
            var prefix = kind switch { "OK" => "✓", "WARN" => "!", "ERR" => "✗", _ => "·" };
            sb.AppendLine($"[{stamp}] {prefix} {msg}");
        }
        _consoleOutput.Text = sb.ToString();
    }

    private static void ConfigureOutput(TextBlock tb)
    {
        tb.FontFamily = FontFamily.Parse(MonoFont);
        tb.FontSize = 11;
        tb.Foreground = Brush(TextDivider);
        tb.Background = Brush(ConsoleSurface);
        tb.TextWrapping = TextWrapping.NoWrap;
        tb.Padding = new Thickness(8, 0, 0, 0);
        tb.IsHitTestVisible = false;
    }

    public void Log(string message, string kind = "INFO")
    {
        // Surface notable events as toasts so the user notices without having to open
        // the console panel. INFO lines stay console-only (too noisy as toasts).
        switch (kind)
        {
            case "OK":   ToastHost.Success(message); break;
            case "WARN": ToastHost.Warn(message); break;
            case "ERR":  ToastHost.Error(message); break;
        }
        var stamp = DateTime.Now.ToString("HH:mm:ss");
        _consoleLines.Add((stamp, kind, message));
        if (_consoleLines.Count > 5000) _consoleLines.RemoveRange(0, 1000);
        // Append without full rebuild if no filter is active (cheaper).
        if (_consoleKindFilter == "ALL" && string.IsNullOrEmpty(_consoleFilter))
        {
            var prefix = kind switch { "OK" => "✓", "WARN" => "!", "ERR" => "✗", _ => "·" };
            _consoleOutput.Text += $"[{stamp}] {prefix} {message}{Environment.NewLine}";
        }
        else
        {
            RebuildConsoleOutput();
        }
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _consoleScroll.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
    }

    public void LogBuild(string line)
    {
        var match = BuildMessageRegex.Match(line);
        if (match.Success)
        {
            var path = match.Groups["path"].Value.Trim();
            var lineNum = int.Parse(match.Groups["line"].Value);
            var kind = match.Groups["kind"].Value;
            var code = match.Groups["code"].Value;
            var msg = match.Groups["msg"].Value;
            if (kind == "error") _buildErrors++;
            else _buildWarnings++;
            DiagnosticParsed?.Invoke(path, lineNum, kind, code, msg);

            var color = kind == "error" ? "#ff6b6b" : "#ffd166";
            var btn = new Button
            {
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Padding = new Thickness(0, 1),
                Cursor = new Cursor(StandardCursorType.Hand),
                Content = new TextBlock
                {
                    FontFamily = FontFamily.Parse(MonoFont),
                    FontSize = 11,
                    Foreground = Brush(color),
                    Text = $"{System.IO.Path.GetFileName(path)}:{lineNum}  {code}  {msg}",
                    TextWrapping = TextWrapping.NoWrap
                }
            };
            btn.Click += (_, _) => BuildErrorClicked?.Invoke(path, lineNum);
            _buildList.Children.Add(btn);
        }
        else
        {
            _buildList.Children.Add(new TextBlock
            {
                Text = line,
                FontFamily = FontFamily.Parse(MonoFont),
                FontSize = 11,
                Foreground = Brush(TextDivider),
                TextWrapping = TextWrapping.NoWrap,
                Padding = new Thickness(0, 1)
            });
        }

        RebuildTabs();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _buildScroll.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
    }

    public void ResetBuild()
    {
        _buildList.Children.Clear();
        _buildErrors = 0;
        _buildWarnings = 0;
        RebuildTabs();
    }

    /// <summary>Current build-tab error / warning counts (after parsing diagnostics).</summary>
    public (int Errors, int Warnings) BuildCounts => (_buildErrors, _buildWarnings);

    /// <summary>
    /// Same as <see cref="LogBuild"/> but treats unparseable lines as red text so the user
    /// sees stderr output from `dotnet` (uncaught exceptions, native loader errors, etc.)
    /// even when the regex doesn't match a known diagnostic format.
    /// </summary>
    public void LogBuildError(string line)
    {
        var match = BuildMessageRegex.Match(line);
        if (match.Success)
        {
            // Recognized — let LogBuild render the clickable diagnostic button.
            LogBuild(line);
            return;
        }
        if (string.IsNullOrWhiteSpace(line)) return;
        _buildList.Children.Add(new TextBlock
        {
            Text = line,
            FontFamily = FontFamily.Parse(MonoFont),
            FontSize = 11,
            Foreground = Brush("#ff6b6b"),
            TextWrapping = TextWrapping.NoWrap,
            Padding = new Thickness(0, 1)
        });
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _buildScroll.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);
    }

    public void FocusBuildTab() => SwitchTo("build");

    private void SwitchTo(string id)
    {
        _activeTab = id;
        _bodyHost.Children.Clear();
        var body = id switch
        {
            "build" => (Control)_buildScroll,
            "todos" => (Control)_todoScroll,
            _ => (Control)_consoleScroll
        };
        _bodyHost.Children.Add(body);
        RebuildTabs();
    }

    private void RebuildTabs()
    {
        _tabsRow.Children.Clear();
        _tabsRow.Children.Add(TabBtn("Console", "console").At(column: 0));
        var buildLabel = _buildErrors == 0 && _buildWarnings == 0
            ? "Build Output"
            : $"Build Output ({_buildErrors} err, {_buildWarnings} warn)";
        _tabsRow.Children.Add(TabBtn(buildLabel, "build").At(column: 1));
        _tabsRow.Children.Add(TabBtn(_todoList.Children.Count > 0 ? $"TODOs ({_todoList.Children.Count})" : "TODOs", "todos").At(column: 2));
    }

    public void RebuildTodos(string projectRoot, Action<string, int> onClick)
    {
        _todoList.Children.Clear();
        if (string.IsNullOrEmpty(projectRoot) || !Directory.Exists(projectRoot))
        {
            RebuildTabs();
            return;
        }

        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", "node_modules", "packages" };
        var todoRegex = new System.Text.RegularExpressions.Regex(@"\b(TODO|FIXME|HACK|XXX|NOTE)\b[: ]?(.*)");

        IEnumerable<string> Walk(DirectoryInfo dir, int depth)
        {
            if (depth > 8) yield break;
            DirectoryInfo[] subs; FileInfo[] files;
            try { subs = dir.GetDirectories(); files = dir.GetFiles(); }
            catch { yield break; }
            foreach (var f in files)
            {
                var ext = f.Extension.ToLowerInvariant();
                if (ext is ".cs" or ".js" or ".ts" or ".py" or ".lua" or ".cpp" or ".c" or ".h" or ".rs" or ".go" or ".java")
                    yield return f.FullName;
            }
            foreach (var d in subs)
            {
                if (d.Name.StartsWith('.') || skip.Contains(d.Name)) continue;
                foreach (var x in Walk(d, depth + 1)) yield return x;
            }
        }

        var found = 0;
        foreach (var file in Walk(new DirectoryInfo(projectRoot), 0))
        {
            string[] lines;
            try { lines = File.ReadAllLines(file); } catch { continue; }
            for (var i = 0; i < lines.Length; i++)
            {
                var m = todoRegex.Match(lines[i]);
                if (!m.Success) continue;
                var kind = m.Groups[1].Value;
                var rest = m.Groups[2].Value.Trim();
                var color = kind switch
                {
                    "FIXME" or "XXX" => "#ff6b6b",
                    "HACK" => "#ffd166",
                    "NOTE" => "#42b7ff",
                    _ => "#7bd88f"
                };
                var localFile = file; var localLine = i + 1;
                var btn = new Button
                {
                    HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    Background = Brushes.Transparent,
                    BorderBrush = Brushes.Transparent,
                    Cursor = new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand),
                    Padding = new Thickness(8, 1),
                    Content = new TextBlock
                    {
                        Text = $"[{kind,-5}] {System.IO.Path.GetFileName(file)}:{localLine}   {rest}",
                        Foreground = Brush(color),
                        FontFamily = FontFamily.Parse(MonoFont),
                        FontSize = 11,
                        TextWrapping = TextWrapping.NoWrap
                    }
                };
                btn.Click += (_, _) => onClick(localFile, localLine);
                _todoList.Children.Add(btn);
                found++;
                if (found > 500) break;
            }
            if (found > 500) break;
        }
        RebuildTabs();
    }

    private Control TabBtn(string text, string id)
    {
        var active = _activeTab == id;
        var btn = new Button
        {
            Content = "  " + text + "  ",
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(active ? TextPrimary : TextDim),
            FontSize = 12,
            Padding = new Thickness(8, 9, 8, 0),
            Height = 38,
            Tag = id
        };
        btn.Click += (_, _) => SwitchTo(id);
        if (active)
        {
            return new Border
            {
                Child = btn,
                BorderBrush = Brush(Accent),
                BorderThickness = new Thickness(0, 0, 0, 2)
            };
        }
        return btn;
    }
}
