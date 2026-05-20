using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using MonoForge.Editor.Services;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views.Panels;

/// <summary>
/// Cursor-style Agent sidebar — compact and minimal. Header is just the title plus a
/// few icon buttons; the input is a rounded pill that contains the model selector and
/// extra controls inline; chat bubbles are subtle; no account chrome unless the user
/// asks for it via the ⋯ menu. The panel keeps the underlying CLI integration as
/// before (session id continuity, per-turn snapshots, revert).
/// </summary>
public sealed class AiAssistantPanel : UserControl
{
    private const string FriendlyFont = "Inter, SF Pro Text, -apple-system, Segoe UI, Helvetica Neue, Arial";
    private const string EmptyStateBg = "#0d0e10";
    private const string ChatBg = "#0d0e10";
    private const string InputPillBg = "#1a1c20";
    private const string PillBorder = "#262a30";

    private readonly StackPanel _history = new() { Spacing = 18, Margin = new Thickness(14, 14, 14, 4) };
    private readonly TextBox _input = new();
    private readonly TextBlock _emptyTitle = new();
    private readonly TextBlock _footer = new();
    private readonly TextBlock _status = new();
    private readonly Panel _bodyHost = new();
    private readonly ComboBox _modelDropdown = new();
    private readonly Button _sendBtn;
    private readonly Button _stopBtn;
    private readonly RowDefinition _inputRowDef = new() { Height = new GridLength(110) };
    private RowDefinition _inputAreaRow = null!; // assigned in ctor; resized by the splitter

    private ScrollViewer _scroll = null!;
    private CancellationTokenSource? _cts;
    // Markdown buffer + container for the currently-streaming Claude turn. The buffer
    // grows token-by-token; the container is re-rendered each chunk so the user sees
    // headings / code blocks / bold etc. live. Set to null between turns.
    private StackPanel? _activeMarkdownBlock;
    private System.Text.StringBuilder? _activeReplyBuf;
    private TextBlock? _activeToolStrip; // shows "→ Edit Game1.cs" etc. above the markdown
    private string _sessionId = Guid.NewGuid().ToString();
    // Tracks whether we've created the session on disk yet. The CLI takes --session-id
    // only for the first call (to create), and --resume <id> on subsequent calls to
    // continue. Passing --session-id again returns "Session is already in use".
    private bool _sessionStarted;
    private readonly List<TurnRecord> _turns = new();

    private string? _projectPath;
    public string? ProjectPath
    {
        get => _projectPath;
        set
        {
            _projectPath = value;
            // If the user hasn't started chatting yet, refresh the empty-state title so
            // they see the new project name as soon as it's loaded.
            if (_history.Children.Count == 0) ShowEmptyState();
        }
    }
    public event Action? ProjectMutated;

    public AiAssistantPanel()
    {
        Background = Brush(ChatBg);

        // ----- header -----
        var titleLabel = new TextBlock
        {
            Text = "Agent",
            Foreground = Brush(TextSecondary),
            FontFamily = new FontFamily(FriendlyFont),
            FontWeight = FontWeight.SemiBold,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(14, 0)
        };
        var newChatBtn = MakeHeaderIcon("+", "New chat", NewChat);
        var historyBtn = MakeHeaderIcon("↻", "Conversation history", ShowHistoryMenu);
        var moreBtn = MakeHeaderIcon("⋯", "More options", ShowMoreMenu);
        var headerIcons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0)
        };
        headerIcons.Children.Add(newChatBtn);
        headerIcons.Children.Add(historyBtn);
        headerIcons.Children.Add(moreBtn);

        var header = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush(ChatBg),
            Height = 38
        };
        header.Children.Add(titleLabel.At(column: 0));
        header.Children.Add(headerIcons.At(column: 1));

        // ----- empty state title (project name) -----
        _emptyTitle.Foreground = Brush(TextPrimary);
        _emptyTitle.FontFamily = new FontFamily(FriendlyFont);
        _emptyTitle.FontWeight = FontWeight.SemiBold;
        _emptyTitle.FontSize = 18;
        _emptyTitle.Margin = new Thickness(18, 0, 18, 10);

        _scroll = new ScrollViewer
        {
            Content = _history,
            Background = Brush(ChatBg),
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };

        // ----- input pill -----
        _input.Watermark = "Ask anything, @ to mention, / for actions";
        _input.Background = Brushes.Transparent;
        _input.Foreground = Brush(TextPrimary);
        _input.BorderBrush = Brushes.Transparent;
        _input.BorderThickness = new Thickness(0);
        _input.FontFamily = new FontFamily(FriendlyFont);
        _input.FontSize = 13;
        _input.Padding = new Thickness(0);
        // AcceptsReturn=false so the TextBox doesn't swallow Enter as "insert newline".
        // We re-implement newline ourselves on Shift+Enter to keep both behaviors:
        // plain Enter → send, Shift+Enter → newline.
        _input.AcceptsReturn = false;
        _input.TextWrapping = TextWrapping.Wrap;
        _input.MinHeight = 28;
        _input.KeyDown += OnInputKey;
        // The Avalonia Fluent theme overrides TextBox background on :focus and
        // :pointerover through DynamicResource lookups. The cleanest, theme-stable way
        // to disable them is to redefine those resources on this TextBox so the lookup
        // resolves to Transparent in every state.
        var transparent = (IBrush)Brushes.Transparent;
        _input.Resources["TextControlBackground"] = transparent;
        _input.Resources["TextControlBackgroundPointerOver"] = transparent;
        _input.Resources["TextControlBackgroundFocused"] = transparent;
        _input.Resources["TextControlBackgroundDisabled"] = transparent;
        _input.Resources["TextControlBorderBrush"] = transparent;
        _input.Resources["TextControlBorderBrushPointerOver"] = transparent;
        _input.Resources["TextControlBorderBrushFocused"] = transparent;
        _input.Resources["TextControlBorderBrushDisabled"] = transparent;

        BuildModelDropdown();
        var attachBtn = MakePillIcon("+", "Add file context — insert @path references for Claude");
        attachBtn.Click += async (_, _) => await PickFilesForContextAsync();

        // Send button — accent-colored circle, only visible while the user has text.
        _sendBtn = new Button
        {
            Content = "↑",
            Background = Brush(Accent),
            BorderBrush = Brush(AccentBorder),
            Foreground = Brush(TextPrimary),
            FontSize = 14,
            FontWeight = FontWeight.SemiBold,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        ToolTip.SetTip(_sendBtn, "Send  (↩)");
        _sendBtn.Click += (_, _) => _ = SendAsync();

        _stopBtn = new Button
        {
            Content = "■",
            Background = Brush("#3a2d2d"),
            BorderBrush = Brushes.Transparent,
            Foreground = Brush("#ff8a8a"),
            FontSize = 12,
            Width = 28,
            Height = 28,
            Padding = new Thickness(0),
            CornerRadius = new CornerRadius(14),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            IsVisible = false,
        };
        ToolTip.SetTip(_stopBtn, "Stop");
        _stopBtn.Click += (_, _) => _cts?.Cancel();

        // Show/hide the Send button based on whether there's a prompt to send.
        _input.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name == "Text")
            {
                var hasText = !string.IsNullOrWhiteSpace(_input.Text);
                _sendBtn.IsVisible = hasText && _cts is null;
            }
        };

        // Bottom row inside the pill: + | model | spacer | send / stop
        var pillBottom = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto,Auto"),
            ColumnSpacing = 4,
            Margin = new Thickness(0, 8, 0, 0)
        };
        pillBottom.Children.Add(attachBtn.At(column: 0));
        pillBottom.Children.Add(_modelDropdown.At(column: 1));
        pillBottom.Children.Add(_stopBtn.At(column: 3));
        pillBottom.Children.Add(_sendBtn.At(column: 4));

        var pillBody = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto")
        };
        pillBody.Children.Add(_input.At(row: 0));
        pillBody.Children.Add(pillBottom.At(row: 1));

        var inputPill = new Border
        {
            Background = Brush(InputPillBg),
            BorderBrush = Brush(PillBorder),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(14, 12),
            Margin = new Thickness(14, 6, 14, 4),
            Child = pillBody
        };

        // Status (small italic above pill) — used during install/auth heartbeats.
        _status.Foreground = Brush(TextDim);
        _status.FontFamily = new FontFamily(FriendlyFont);
        _status.FontSize = 11;
        _status.FontStyle = FontStyle.Italic;
        _status.Padding = new Thickness(18, 2, 18, 0);
        _status.IsVisible = false;

        _footer.Text = "Claude may make mistakes. Double-check all generated code.";
        _footer.Foreground = Brush("#3e4148");
        _footer.FontFamily = new FontFamily(FriendlyFont);
        _footer.FontSize = 10.5;
        _footer.HorizontalAlignment = HorizontalAlignment.Center;
        _footer.Margin = new Thickness(14, 0, 14, 10);

        // The chat / empty area sits in this Panel; we swap its content depending on
        // whether the user has sent any messages yet.
        _bodyHost.Background = Brush(ChatBg);

        // Resizable input via splitter between body and pill.
        var splitter = new GridSplitter
        {
            ResizeDirection = GridResizeDirection.Rows,
            Background = Brushes.Transparent,
            Height = 6,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ShowsPreview = false,
            Cursor = new Cursor(StandardCursorType.SizeNorthSouth)
        };
        ToolTip.SetTip(splitter, "Drag to resize the input");

        // Group the title / status / pill together — they live in the resizable bottom
        // section so dragging the splitter grows the whole input area, not just the pill.
        // GridSplitter needs Star + Pixel adjacent rows to resize properly; Auto rows
        // can't be dragged.
        var inputArea = new Grid
        {
            RowDefinitions = new RowDefinitions("Auto,Auto,*"),
            Background = Brush(ChatBg)
        };
        inputArea.Children.Add(_emptyTitle.At(row: 0));
        inputArea.Children.Add(_status.At(row: 1));
        inputArea.Children.Add(inputPill.At(row: 2));

        _inputAreaRow = new RowDefinition(new GridLength(150));
        _inputAreaRow.MinHeight = 80;

        var root = new Grid { Background = Brush(ChatBg) };
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // header
        root.RowDefinitions.Add(new RowDefinition(GridLength.Star));   // body
        root.RowDefinitions.Add(new RowDefinition(new GridLength(6))); // splitter
        root.RowDefinitions.Add(_inputAreaRow);                        // resizable input area
        root.RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // footer

        root.Children.Add(header.At(row: 0));
        root.Children.Add(_bodyHost.At(row: 1));
        root.Children.Add(splitter.At(row: 2));
        root.Children.Add(inputArea.At(row: 3));
        root.Children.Add(_footer.At(row: 4));

        Content = root;
        ShowEmptyState();
    }

    private void BuildModelDropdown()
    {
        _modelDropdown.Items.Clear();
        _modelDropdown.Items.Add("Default model");
        _modelDropdown.Items.Add("Sonnet");
        _modelDropdown.Items.Add("Opus");
        _modelDropdown.Items.Add("Haiku");
        _modelDropdown.SelectedIndex = 0;
        _modelDropdown.FontFamily = new FontFamily(FriendlyFont);
        _modelDropdown.FontSize = 11;
        _modelDropdown.Background = Brushes.Transparent;
        _modelDropdown.Foreground = Brush(TextDim);
        _modelDropdown.BorderBrush = Brushes.Transparent;
        _modelDropdown.Padding = new Thickness(8, 2, 4, 2);
        _modelDropdown.MinHeight = 24;
        _modelDropdown.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private string? SelectedModelArg() => _modelDropdown.SelectedIndex switch
    {
        1 => "sonnet",
        2 => "opus",
        3 => "haiku",
        _ => null,
    };

    private void ShowEmptyState()
    {
        _bodyHost.Children.Clear();
        var spacer = new Panel { Background = Brush(ChatBg) };
        _bodyHost.Children.Add(spacer);
        _emptyTitle.Text = string.IsNullOrEmpty(ProjectPath) ? "MonoForge" : Path.GetFileName(ProjectPath);
        _emptyTitle.IsVisible = true;
    }

    private void ShowChatState()
    {
        if (_bodyHost.Children.Count == 0 || _bodyHost.Children[0] != _scroll)
        {
            _bodyHost.Children.Clear();
            _bodyHost.Children.Add(_scroll);
        }
        _emptyTitle.IsVisible = false;
    }

    // ----- header icons & menus -----

    private static Button MakeHeaderIcon(string glyph, string tooltip, Func<Task> onClick)
    {
        var btn = new Button
        {
            Content = glyph,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(TextDim),
            FontSize = 15,
            Padding = new Thickness(0),
            Width = 28,
            Height = 28,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6)
        };
        ToolTip.SetTip(btn, tooltip);
        btn.Click += async (_, _) => await onClick();
        return btn;
    }

    private static Button MakePillIcon(string glyph, string tooltip)
    {
        var btn = new Button
        {
            Content = glyph,
            Background = Brushes.Transparent,
            BorderBrush = Brushes.Transparent,
            Foreground = Brush(TextDim),
            FontSize = 14,
            Width = 26,
            Height = 26,
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6)
        };
        ToolTip.SetTip(btn, tooltip);
        return btn;
    }

    private async Task NewChat()
    {
        _history.Children.Clear();
        _turns.Clear();
        _activeReplyBuf = null; _activeMarkdownBlock = null;
        _sessionId = Guid.NewGuid().ToString();
        _sessionStarted = false; // fresh id, no on-disk session yet
        ShowEmptyState();
        await Task.CompletedTask;
    }

    private async Task ShowHistoryMenu()
    {
        // Lightweight placeholder — clear chat acts as "start fresh"; future versions
        // can list previous session ids saved to disk.
        await NewChat();
    }

    private async Task ShowMoreMenu()
    {
        var menu = new MenuFlyout();
        var account = await TryReadAccountAsync();
        if (!ClaudeCodeService.IsAvailable)
        {
            menu.Items.Add(MenuItemFor("Install Claude CLI", InstallClaudeAsync));
            menu.Items.Add(MenuItemFor("Authenticate", AuthenticateAsync));
        }
        else if (account is null)
        {
            menu.Items.Add(MenuItemFor("Sign in", AuthenticateAsync));
        }
        else
        {
            // Account block — read-only entries that show who you're logged in as and
            // your plan tier. Token limits aren't exposed by the CLI, so we link to the
            // web dashboard for usage.
            menu.Items.Add(new MenuItem
            {
                Header = "Signed in as",
                IsEnabled = false,
                Foreground = Brush(TextMuted),
            });
            menu.Items.Add(new MenuItem
            {
                Header = "  " + account.Email,
                IsEnabled = false,
                Foreground = Brush(TextSecondary),
            });
            menu.Items.Add(new MenuItem
            {
                Header = $"  {account.OrgName}  ·  {PrettyPlan(account.Plan)}",
                IsEnabled = false,
                Foreground = Brush(TextDim),
            });
            menu.Items.Add(new Separator());
            menu.Items.Add(MenuItemFor("View usage & limits in browser", OpenUsageDashboardAsync));
            menu.Items.Add(MenuItemFor("Re-authenticate", AuthenticateAsync));
            menu.Items.Add(MenuItemFor("Sign out", LogoutAsync));
        }
        menu.Items.Add(new Separator());
        menu.Items.Add(MenuItemFor("New chat", NewChat));
        menu.ShowAt(this, true);
    }

    /// <summary>Map subscription codes to display names ("team" → "Team plan").</summary>
    private static string PrettyPlan(string code) => code switch
    {
        "team" => "Team plan",
        "pro" => "Pro plan",
        "max" => "Max plan",
        "enterprise" => "Enterprise",
        "free" => "Free tier",
        _ => string.IsNullOrEmpty(code) ? "Unknown" : code,
    };

    /// <summary>
    /// Token counts and rate limits aren't exposed by the Claude Code CLI (the JSON in
    /// `auth status` stops at plan tier). Open the Claude.ai settings page in the user's
    /// browser so they can see the actual usage there.
    /// </summary>
    private Task OpenUsageDashboardAsync()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = OperatingSystem.IsMacOS() ? "open"
                         : OperatingSystem.IsWindows() ? "cmd" : "xdg-open",
                Arguments = OperatingSystem.IsWindows()
                    ? "/c start https://claude.ai/settings/usage"
                    : "https://claude.ai/settings/usage",
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }
        catch { /* ignored */ }
        return Task.CompletedTask;
    }

    private MenuItem MenuItemFor(string label, Func<Task> action)
    {
        var mi = new MenuItem { Header = label };
        mi.Click += async (_, _) => await action();
        return mi;
    }

    // ----- account info -----

    private async Task<AccountInfo?> TryReadAccountAsync()
    {
        var binary = ClaudeCodeService.FindClaudeBinary();
        if (binary is null) return null;
        try
        {
            var (exit, output) = await CaptureAsync(binary, "auth status --json");
            if (exit != 0 || string.IsNullOrWhiteSpace(output)) return null;
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            if (!root.TryGetProperty("loggedIn", out var loggedIn) || !loggedIn.GetBoolean()) return null;
            var email = root.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";
            var org = root.TryGetProperty("orgName", out var o) ? o.GetString() ?? "" : "";
            var plan = root.TryGetProperty("subscriptionType", out var p) ? p.GetString() ?? "" : "";
            return new AccountInfo(email, org, plan);
        }
        catch { return null; }
    }

    private sealed record AccountInfo(string Email, string OrgName, string Plan);

    private static Task<(int Exit, string Output)> CaptureAsync(string exe, string args)
    {
        var tcs = new TaskCompletionSource<(int, string)>();
        var sb = new System.Text.StringBuilder();
        try
        {
            var p = new System.Diagnostics.Process
            {
                StartInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = args,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                },
                EnableRaisingEvents = true,
            };
            p.OutputDataReceived += (_, e) => { if (e.Data is { } d) sb.AppendLine(d); };
            p.ErrorDataReceived += (_, e) => { if (e.Data is { } d) sb.AppendLine(d); };
            p.Exited += (_, _) => { tcs.TrySetResult((p.ExitCode, sb.ToString())); p.Dispose(); };
            p.Start();
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
        }
        catch (Exception ex) { tcs.TrySetResult((-1, ex.Message)); }
        return tcs.Task;
    }

    // ----- install / auth -----

    private async Task InstallClaudeAsync()
    {
        ShowChatState();
        _status.IsVisible = true;
        AppendSystem("Installing Claude Code (~/.local/bin/claude). About 1-3 minutes; the timer below tracks progress.");
        BeginAssistant("Installer");
        AppendToReply("Downloading…\n");
        var exit = await RunWithHeartbeatAsync(
            "/bin/bash", "-lc \"curl -fsSL https://claude.ai/install.sh | bash\"",
            "Installing");
        if (exit != 0)
        {
            AppendToReply("\nFalling back to npm…");
            exit = await RunWithHeartbeatAsync("npm", "install -g @anthropic-ai/claude-code", "npm installing");
        }
        _activeReplyBuf = null; _activeMarkdownBlock = null;
        ClaudeCodeService.ResetCache();
        AppendSystem(exit == 0 && ClaudeCodeService.IsAvailable
            ? $"Installed at {ClaudeCodeService.FindClaudeBinary()}. Open the ⋯ menu to sign in."
            : "Install failed. Try a terminal:  curl -fsSL https://claude.ai/install.sh | bash");
        _status.IsVisible = false;
    }

    private async Task AuthenticateAsync()
    {
        if (!ClaudeCodeService.IsAvailable)
        {
            ShowChatState();
            AppendSystem("Install the CLI first (⋯ menu → Install Claude CLI).");
            return;
        }
        ShowChatState();
        _status.IsVisible = true;
        AppendSystem("Opening browser. Complete login there — the panel resumes when you return.");
        BeginAssistant("Auth");
        AppendToReply("Waiting for browser…\n");
        var binary = ClaudeCodeService.FindClaudeBinary()!;
        var exit = await RunWithHeartbeatAsync(binary, "auth login", "Waiting for browser");
        _activeReplyBuf = null; _activeMarkdownBlock = null;
        AppendSystem(exit == 0 ? "Signed in. Type a prompt to start." : $"Sign-in didn't complete (exit {exit}).");
        _status.IsVisible = false;
    }

    private async Task LogoutAsync()
    {
        var binary = ClaudeCodeService.FindClaudeBinary();
        if (binary is null) return;
        await CaptureAsync(binary, "auth logout");
        ShowChatState();
        AppendSystem("Signed out.");
    }

    private async Task<int> RunWithHeartbeatAsync(string exe, string args, string label)
    {
        var start = DateTime.UtcNow;
        var done = false;
        var heartbeat = Task.Run(async () =>
        {
            while (!done)
            {
                await Task.Delay(1000);
                if (done) break;
                var secs = (int)(DateTime.UtcNow - start).TotalSeconds;
                Avalonia.Threading.Dispatcher.UIThread.Post(() => _status.Text = $"{label}…  {secs / 60}:{secs % 60:D2}");
            }
        });
        var exit = await ProcessRunner.RunAsync(
            exe, args, Environment.CurrentDirectory,
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendToReply(line)),
            line => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendToReply(line)));
        done = true;
        try { await heartbeat; } catch { }
        var total = (int)(DateTime.UtcNow - start).TotalSeconds;
        Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendToReply($"\n— Finished in {total / 60}:{total % 60:D2}, exit {exit} —"));
        return exit;
    }

    /// <summary>
    /// Open a file picker and insert "@path/to/file" tokens at the caret for each
    /// chosen file. Claude Code recognizes the @-syntax and pulls those files into
    /// context for the next prompt.
    /// </summary>
    private async Task PickFilesForContextAsync()
    {
        var win = TopLevel.GetTopLevel(this) as Window;
        if (win is null) return;
        var files = await win.StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Add files as context",
            AllowMultiple = true
        });
        if (files is null || files.Count == 0) return;

        var refs = string.Join(" ", files.Select(f =>
        {
            var path = f.Path.LocalPath;
            // Prefer relative path so it reads naturally inside the project context.
            if (_projectPath is not null)
            {
                try { return "@" + Path.GetRelativePath(_projectPath, path); }
                catch { return "@" + path; }
            }
            return "@" + path;
        }));

        var pos = _input.CaretIndex;
        var current = _input.Text ?? "";
        var insert = (pos > 0 && pos <= current.Length && current[pos - 1] != ' ' ? " " : "") + refs + " ";
        _input.Text = current.Insert(pos, insert);
        _input.CaretIndex = pos + insert.Length;
        _input.Focus();
    }

    private void OnInputKey(object? sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Enter or Key.Return)) return;
        if (e.KeyModifiers.HasFlag(KeyModifiers.Shift))
        {
            // AcceptsReturn is off so we insert the newline ourselves at the caret.
            var pos = _input.CaretIndex;
            var text = _input.Text ?? "";
            _input.Text = text.Insert(pos, "\n");
            _input.CaretIndex = pos + 1;
            e.Handled = true;
            return;
        }
        _ = SendAsync();
        e.Handled = true;
    }

    private async Task SendAsync()
    {
        if (_cts is not null) return;
        var prompt = _input.Text?.Trim();
        if (string.IsNullOrEmpty(prompt)) return;
        if (!ClaudeCodeService.IsAvailable)
        {
            ShowChatState();
            AppendSystem("Open ⋯ → Install Claude CLI first.");
            return;
        }
        // Block sending if not authenticated.
        var account = await TryReadAccountAsync();
        if (account is null)
        {
            ShowChatState();
            AppendSystem("Open ⋯ → Sign in to start chatting.");
            return;
        }
        ShowChatState();
        var cwd = ProjectPath ?? Environment.CurrentDirectory;
        var turnId = Guid.NewGuid().ToString("N")[..8];
        var snapshotDir = TrySnapshot(cwd, turnId);

        var userBubble = AppendUser(prompt);
        _input.Text = "";
        // Placeholder for the turn we're about to record; populated after BeginAssistant.
        TurnRecord? pendingTurn = null;
        var (block, revertBtn) = BeginAssistant("Claude", snapshotDir, cwd, onRevert: () => pendingTurn);
        pendingTurn = new TurnRecord(prompt, snapshotDir, userBubble, block, revertBtn);
        _turns.Add(pendingTurn);
        ScrollToEnd();

        _cts = new CancellationTokenSource();
        _sendBtn.IsVisible = false;
        _stopBtn.IsVisible = true;
        _status.IsVisible = true;
        _status.Text = "Thinking…";

        var modelArg = SelectedModelArg();
        var sessionFlag = _sessionStarted ? $"--resume {_sessionId}" : $"--session-id {_sessionId}";
        var args = $"--print --permission-mode acceptEdits {sessionFlag}";
        if (modelArg is not null) args += $" --model {modelArg}";

        try
        {
            await ClaudeCodeService.RunStreamingPromptAsync(
                args, prompt, cwd,
                onTextDelta: chunk => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendTextChunk(chunk)),
                onToolUse: tool => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendToolUse(tool)),
                onUsage: usage => Avalonia.Threading.Dispatcher.UIThread.Post(() => ShowUsage(usage)),
                onError: line => Avalonia.Threading.Dispatcher.UIThread.Post(() => AppendSystem("⚠ " + line)),
                ct: _cts.Token);
            _sessionStarted = true;
        }
        catch (Exception ex) { AppendSystem("Error: " + ex.Message); }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            _stopBtn.IsVisible = false;
            _status.IsVisible = false;
            _activeMarkdownBlock = null;
            _activeReplyBuf = null;
            _activeToolStrip = null;
            ProjectMutated?.Invoke();
        }
    }

    /// <summary>Append a streamed token chunk to the current reply and re-render markdown.</summary>
    private void AppendTextChunk(string chunk)
    {
        if (_activeReplyBuf is null || _activeMarkdownBlock is null) return;
        _activeReplyBuf.Append(chunk);
        MarkdownRenderer.Render(_activeMarkdownBlock, _activeReplyBuf.ToString());
        ScrollToEnd();
    }

    /// <summary>Show "→ Tool" indicator above the markdown block.</summary>
    private void AppendToolUse(string tool)
    {
        if (_activeToolStrip is null) return;
        var existing = _activeToolStrip.Text ?? "";
        _activeToolStrip.Text = string.IsNullOrEmpty(existing) ? $"→ {tool}" : existing + $"   →  {tool}";
        ScrollToEnd();
    }

    /// <summary>Display the per-turn token usage in the status bar after a reply completes.</summary>
    private void ShowUsage(ClaudeUsage u)
    {
        var totalIn = u.InputTokens + u.CacheReadTokens + u.CacheWriteTokens;
        _status.IsVisible = true;
        _status.Text = $"Used  {totalIn} in  ·  {u.OutputTokens} out  ·  {u.DurationSeconds:0.0}s";
    }

    // ----- chat bubbles -----

    private Border AppendUser(string text)
    {
        var bubble = new Border
        {
            Background = Brush("#1f2530"),
            CornerRadius = new CornerRadius(14, 14, 4, 14),
            Padding = new Thickness(12, 8),
            MaxWidth = 280,
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock
            {
                Text = text,
                Foreground = Brush(TextPrimary),
                FontFamily = new FontFamily(FriendlyFont),
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap
            }
        };
        _history.Children.Add(bubble);
        ScrollToEnd();
        return bubble;
    }

    private (StackPanel Block, Button? Revert) BeginAssistant(string label, string? snapshotDir = null, string? projectRoot = null, Func<TurnRecord?>? onRevert = null)
    {
        var headerRow = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        headerRow.Children.Add(new TextBlock
        {
            Text = "✦  " + label,
            Foreground = Brush(TextDim),
            FontFamily = new FontFamily(FriendlyFont),
            FontSize = 11,
            FontWeight = FontWeight.SemiBold,
            Margin = new Thickness(0, 0, 0, 4)
        }.At(column: 0));

        Button? revertBtn = null;
        if (snapshotDir is not null && projectRoot is not null)
        {
            revertBtn = new Button
            {
                Content = "↶",
                Background = Brushes.Transparent,
                BorderBrush = Brushes.Transparent,
                Foreground = Brush(TextDim),
                FontFamily = new FontFamily(FriendlyFont),
                FontSize = 13,
                Width = 24,
                Height = 22,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment = VerticalAlignment.Center
            };
            ToolTip.SetTip(revertBtn, "Revert this turn — restore files, rewind chat to this prompt.");
            var capturedSnapshot = snapshotDir;
            var capturedRoot = projectRoot;
            revertBtn.Click += async (_, _) =>
            {
                revertBtn.IsEnabled = false;
                await Task.Run(() => RevertSnapshot(capturedSnapshot, capturedRoot));
                ProjectMutated?.Invoke();
                // Rewind the conversation to right before this turn: drop the user
                // bubble, this assistant block, plus anything after them, and put the
                // user's original prompt back in the input so they can edit and resend.
                if (onRevert?.Invoke() is { } turn) RewindTo(turn);
            };
            headerRow.Children.Add(revertBtn.At(column: 1));
        }

        // Tool-use strip — populated by AppendToolUse as Claude calls Edit/Bash/etc.
        _activeToolStrip = new TextBlock
        {
            Text = "",
            Foreground = Brush(TextMuted),
            FontFamily = new FontFamily(FriendlyFont),
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 4)
        };

        // Markdown is rendered into this StackPanel — children get cleared and rebuilt on
        // each streamed chunk so headings, code blocks, lists, bold all show progressively.
        _activeMarkdownBlock = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        _activeReplyBuf = new System.Text.StringBuilder();

        var stack = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(headerRow);
        stack.Children.Add(_activeToolStrip);
        stack.Children.Add(_activeMarkdownBlock);
        _history.Children.Add(stack);
        return (stack, revertBtn);
    }

    /// <summary>
    /// Drop the chat history starting at the given turn's user bubble, restore that
    /// turn's prompt into the input box for editing, and forget this and every later
    /// turn. Used when the user clicks Revert on a Claude response.
    /// </summary>
    private void RewindTo(TurnRecord turn)
    {
        var startIdx = _history.Children.IndexOf(turn.UserBubble);
        if (startIdx < 0) return;
        // Remove every child from startIdx onwards. We trim from the end backwards so
        // indexes stay valid.
        while (_history.Children.Count > startIdx)
            _history.Children.RemoveAt(_history.Children.Count - 1);
        // Drop this turn and every later one from our state list.
        var turnIdx = _turns.IndexOf(turn);
        if (turnIdx >= 0) _turns.RemoveRange(turnIdx, _turns.Count - turnIdx);
        // Put the original prompt back in the input for the user to edit and resend.
        _input.Text = turn.Prompt;
        _input.CaretIndex = turn.Prompt.Length;
        _input.Focus();
        // If we revert all the way to the beginning, also reset the on-disk session so
        // the next prompt starts fresh.
        if (_turns.Count == 0)
        {
            _sessionId = Guid.NewGuid().ToString();
            _sessionStarted = false;
            ShowEmptyState();
        }
    }

    private void AppendSystem(string text)
    {
        _history.Children.Add(new TextBlock
        {
            Text = text,
            Foreground = Brush(TextDim),
            FontFamily = new FontFamily(FriendlyFont),
            FontSize = 11,
            FontStyle = FontStyle.Italic,
            TextWrapping = TextWrapping.Wrap,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(20, 4)
        });
        ScrollToEnd();
    }

    /// <summary>
    /// Plain-text fallback used by install / auth heartbeats (those run plain processes,
    /// not stream-json). Falls back to appending to the markdown buffer if that's active,
    /// otherwise just adds a system bubble line.
    /// </summary>
    private void AppendToReply(string line)
    {
        if (_activeMarkdownBlock is null || _activeReplyBuf is null) return;
        if (_activeReplyBuf.Length > 0) _activeReplyBuf.Append('\n');
        _activeReplyBuf.Append(line);
        MarkdownRenderer.Render(_activeMarkdownBlock, _activeReplyBuf.ToString());
        ScrollToEnd();
    }

    private void ScrollToEnd() =>
        Avalonia.Threading.Dispatcher.UIThread.Post(() => _scroll.ScrollToEnd(), Avalonia.Threading.DispatcherPriority.Background);

    // ----- snapshot / revert -----

    private static string? TrySnapshot(string projectRoot, string turnId)
    {
        try
        {
            var dir = Path.Combine(projectRoot, ".monoforge", "ai-snapshots", turnId);
            Directory.CreateDirectory(dir);
            foreach (var f in EnumerateSnapshotFiles(projectRoot))
            {
                var rel = Path.GetRelativePath(projectRoot, f);
                var dest = Path.Combine(dir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
            }
            return dir;
        }
        catch { return null; }
    }

    private static int RevertSnapshot(string snapshotDir, string projectRoot)
    {
        if (!Directory.Exists(snapshotDir)) return 0;
        var count = 0;
        var snapshotFiles = Directory.EnumerateFiles(snapshotDir, "*", SearchOption.AllDirectories).ToList();
        foreach (var f in snapshotFiles)
        {
            var rel = Path.GetRelativePath(snapshotDir, f);
            var dest = Path.Combine(projectRoot, rel);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(f, dest, overwrite: true);
                count++;
            }
            catch { }
        }
        var snapshotRelSet = snapshotFiles.Select(f => Path.GetRelativePath(snapshotDir, f)).ToHashSet();
        foreach (var f in EnumerateSnapshotFiles(projectRoot))
        {
            var rel = Path.GetRelativePath(projectRoot, f);
            if (!snapshotRelSet.Contains(rel))
                try { File.Delete(f); } catch { }
        }
        return count;
    }

    private static IEnumerable<string> EnumerateSnapshotFiles(string root)
    {
        var skipDirs = new[] { "bin", "obj", ".git", "node_modules", ".vs", ".idea", "packages", ".monoforge" };
        return Walk(new DirectoryInfo(root));
        IEnumerable<string> Walk(DirectoryInfo dir)
        {
            IEnumerable<FileInfo> files;
            IEnumerable<DirectoryInfo> subs;
            try { files = dir.EnumerateFiles(); subs = dir.EnumerateDirectories(); }
            catch { yield break; }
            foreach (var f in files)
            {
                if (!IsSnapshotExt(f.Extension)) continue;
                if (f.Length > 200_000) continue;
                yield return f.FullName;
            }
            foreach (var sub in subs)
            {
                if (skipDirs.Contains(sub.Name, StringComparer.OrdinalIgnoreCase)) continue;
                if (sub.Name.StartsWith('.') && sub.Name != ".github") continue;
                foreach (var inner in Walk(sub)) yield return inner;
            }
        }
    }

    private static bool IsSnapshotExt(string ext) => ext.ToLowerInvariant() is
        ".cs" or ".csproj" or ".sln" or ".props" or ".targets"
        or ".json" or ".xml" or ".yml" or ".yaml" or ".toml" or ".ini"
        or ".md" or ".txt" or ".config" or ".editorconfig"
        or ".py" or ".js" or ".ts" or ".sh" or ".bash"
        or ".fx" or ".fxc" or ".mgcb" or ".glsl" or ".hlsl";

    /// <summary>One past prompt/reply pair. UI controls are captured so Revert can
    /// rewind the chat to before this turn fired.</summary>
    private sealed record TurnRecord(string Prompt, string? SnapshotDir, Control UserBubble, StackPanel AssistantBlock, Button? RevertBtn);
}
