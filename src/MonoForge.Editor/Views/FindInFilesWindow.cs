using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using static MonoForge.Editor.Views.Theme;
using static MonoForge.Editor.Views.UiFactory;

namespace MonoForge.Editor.Views;

public sealed class FindInFilesWindow : Window
{
    private readonly TextBox _query = new();
    private readonly CheckBox _caseSensitive = new() { Content = "Match case", Foreground = Brush(TextSecondary) };
    private readonly CheckBox _wholeWord = new() { Content = "Whole word", Foreground = Brush(TextSecondary) };
    private readonly TextBox _glob = new();
    private readonly StackPanel _results = new() { Margin = new Thickness(0, 0, 0, 8) };
    private readonly TextBlock _status = new() { Foreground = Brush(TextDim), FontSize = 12, Padding = new Thickness(12, 4) };
    private readonly string _rootPath;
    private readonly Action<string, int> _onOpen;
    private CancellationTokenSource? _cts;

    public FindInFilesWindow(string rootPath, Action<string, int> onOpen)
    {
        _rootPath = rootPath;
        _onOpen = onOpen;

        Title = "Find in Files";
        Width = 820; Height = 560;
        Background = Brush(EditorBackground);

        _query.Watermark = "Search…";
        _query.Background = Brush(InputBackground);
        _query.Foreground = Brush(TextSecondary);
        _query.BorderBrush = Brush(BorderColor);
        _query.FontFamily = new FontFamily(MonoFont);
        _query.FontSize = 13;
        _query.Padding = new Thickness(10, 6);

        _glob.Watermark = "*.cs *.json … (blank = all text)";
        _glob.Background = Brush(InputBackground);
        _glob.Foreground = Brush(TextSecondary);
        _glob.BorderBrush = Brush(BorderColor);
        _glob.FontSize = 12;
        _glob.Padding = new Thickness(10, 4);

        var topRow = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(12, 12, 12, 6)
        };
        topRow.Children.Add(_query.At(column: 0));
        var goBtn = PrimaryButton("Search", (_, _) => _ = SearchAsync());
        goBtn.Margin = new Thickness(8, 0, 0, 0);
        topRow.Children.Add(goBtn.At(column: 1));

        var filterRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(12, 0, 12, 6) };
        filterRow.Children.Add(_caseSensitive);
        filterRow.Children.Add(_wholeWord);
        filterRow.Children.Add(new TextBlock { Text = "Filter:", Foreground = Brush(TextDim), VerticalAlignment = VerticalAlignment.Center });
        _glob.Width = 280;
        filterRow.Children.Add(_glob);

        var scroll = new ScrollViewer { Content = _results, Background = Brush(MenuBackground), Margin = new Thickness(12, 0, 12, 0) };

        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto") };
        root.Children.Add(topRow.At(row: 0));
        root.Children.Add(filterRow.At(row: 1));
        root.Children.Add(scroll.At(row: 2));
        root.Children.Add(_status.At(row: 3));
        Content = root;

        _query.KeyDown += (_, e) => { if (e.Key == Key.Enter) _ = SearchAsync(); };
        Opened += (_, _) => _query.Focus();
    }

    private async Task SearchAsync()
    {
        var q = _query.Text ?? "";
        if (string.IsNullOrEmpty(q)) return;
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        _results.Children.Clear();
        _status.Text = "Searching…";

        var caseSensitive = _caseSensitive.IsChecked == true;
        var wholeWord = _wholeWord.IsChecked == true;
        var patterns = ParseGlobs(_glob.Text);

        var hits = 0;
        var filesHit = 0;
        await Task.Run(() =>
        {
            foreach (var file in EnumerateFiles(_rootPath))
            {
                if (token.IsCancellationRequested) return;
                if (patterns.Count > 0 && !patterns.Any(p => MatchesGlob(file, p))) continue;
                if (!LooksTextual(file)) continue;

                string content;
                try { content = File.ReadAllText(file); }
                catch { continue; }

                var lines = content.Split('\n');
                var fileMatches = new List<(int LineNo, string LineText, int Col)>();
                for (var i = 0; i < lines.Length; i++)
                {
                    var line = lines[i].TrimEnd('\r');
                    var idx = FindMatch(line, q, caseSensitive, wholeWord);
                    if (idx >= 0)
                    {
                        fileMatches.Add((i + 1, line, idx));
                        hits++;
                    }
                }

                if (fileMatches.Count > 0)
                {
                    filesHit++;
                    var localFile = file;
                    var localMatches = fileMatches;
                    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    {
                        AppendFileResults(localFile, localMatches);
                        _status.Text = $"{hits} matches in {filesHit} files";
                    });
                }
            }
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                if (hits == 0) _status.Text = "No matches.";
                else _status.Text = $"Done — {hits} matches in {filesHit} files";
            });
        }, token);
    }

    private void AppendFileResults(string file, List<(int LineNo, string LineText, int Col)> matches)
    {
        var header = new TextBlock
        {
            Text = Path.GetRelativePath(_rootPath, file),
            Foreground = Brush(TextDivider),
            FontSize = 12,
            FontWeight = FontWeight.SemiBold,
            FontFamily = new FontFamily(MonoFont),
            Padding = new Thickness(8, 6, 8, 2),
            Background = Brush(PanelBackgroundAlt)
        };
        _results.Children.Add(header);
        foreach (var (lineNo, lineText, col) in matches)
        {
            var btn = new Button
            {
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Background = Avalonia.Media.Brushes.Transparent,
                BorderBrush = Avalonia.Media.Brushes.Transparent,
                Cursor = new Cursor(StandardCursorType.Hand),
                Padding = new Thickness(8, 1)
            };
            var inner = new TextBlock
            {
                Text = $"{lineNo,4}: {lineText.TrimStart()}",
                Foreground = Brush(TextSecondary),
                FontSize = 11,
                FontFamily = new FontFamily(MonoFont),
                TextWrapping = TextWrapping.NoWrap
            };
            btn.Content = inner;
            btn.Click += (_, _) => { _onOpen(file, lineNo); };
            _results.Children.Add(btn);
        }
    }

    private static int FindMatch(string line, string query, bool caseSensitive, bool wholeWord)
    {
        var cmp = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var idx = line.IndexOf(query, cmp);
        if (idx < 0 || !wholeWord) return idx;
        bool leftOk = idx == 0 || !IsWordChar(line[idx - 1]);
        bool rightOk = idx + query.Length >= line.Length || !IsWordChar(line[idx + query.Length]);
        return (leftOk && rightOk) ? idx : -1;
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private static List<string> ParseGlobs(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return new();
        return text.Split(new[] { ' ', ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static bool MatchesGlob(string path, string glob)
    {
        var name = Path.GetFileName(path);
        var pattern = "^" + System.Text.RegularExpressions.Regex.Escape(glob).Replace("\\*", ".*").Replace("\\?", ".") + "$";
        return System.Text.RegularExpressions.Regex.IsMatch(name, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> EnumerateFiles(string root)
    {
        var skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "bin", "obj", ".git", ".vs", ".idea", ".vscode", "node_modules", "packages" };
        return Walk(new DirectoryInfo(root), 0);

        IEnumerable<string> Walk(DirectoryInfo dir, int depth)
        {
            if (depth > 8) yield break;
            DirectoryInfo[] subs; FileInfo[] files;
            try { subs = dir.GetDirectories(); files = dir.GetFiles(); }
            catch { yield break; }
            foreach (var f in files) yield return f.FullName;
            foreach (var d in subs)
            {
                if (d.Name.StartsWith('.') || skip.Contains(d.Name)) continue;
                foreach (var x in Walk(d, depth + 1)) yield return x;
            }
        }
    }

    private static bool LooksTextual(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext is ".cs" or ".js" or ".ts" or ".jsx" or ".tsx" or ".py" or ".lua" or ".rs"
            or ".go" or ".java" or ".kt" or ".swift" or ".cpp" or ".cc" or ".c" or ".h" or ".hpp"
            or ".rb" or ".php" or ".html" or ".css" or ".scss" or ".xml" or ".json" or ".yaml"
            or ".yml" or ".md" or ".txt" or ".mgcb" or ".fx" or ".vp" or ".fp" or ".script"
            or ".collection" or ".atlas" or ".gui" or ".material" or ".tilemap";
    }
}
