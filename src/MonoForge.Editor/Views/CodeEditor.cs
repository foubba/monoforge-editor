using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using AvaloniaEdit;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Search;
using AvaloniaEdit.TextMate;
using TextMateSharp.Grammars;
using static MonoForge.Editor.Views.Theme;

namespace MonoForge.Editor.Views;

public sealed class CodeEditor : UserControl
{
    private readonly TextEditor _editor = new();
    private readonly TextBlock _status = new();
    private readonly Minimap _minimap;
    private TextMate.Installation? _installation;
    private FoldingManager? _foldingManager;
    private object? _foldingStrategy;
    private static readonly RegistryOptions Registry = new(ThemeName.DarkPlus);
    private readonly string _filePath;
    private string _language = "plaintext";
    private string _diagnosticsLabel = "";
    private readonly DiagnosticsRenderer _diagRenderer = new();
    private bool _dirty;

    public string FilePath => _filePath;
    public bool IsDirty => _dirty;
    public event Action<CodeEditor>? DirtyChanged;

    public CodeEditor(string filePath, string content)
    {
        _filePath = filePath;

        _editor.Background = new SolidColorBrush(Color.Parse("#1e1e1e"));
        _editor.Foreground = new SolidColorBrush(Color.Parse("#d4d4d4"));
        _editor.ShowLineNumbers = true;
        _editor.FontFamily = new FontFamily("Menlo,Consolas,monospace");
        _editor.FontSize = 13;
        _editor.Options.ConvertTabsToSpaces = true;
        _editor.Options.IndentationSize = 4;
        _editor.Options.HighlightCurrentLine = true;
        _editor.Options.EnableHyperlinks = false;
        _editor.Options.EnableEmailHyperlinks = false;
        _editor.Options.ShowColumnRulers = false;
        _editor.Options.ShowEndOfLine = false;
        _editor.Options.ShowTabs = false;
        _editor.Options.ShowSpaces = false;
        _editor.Options.AllowScrollBelowDocument = false;
        _editor.WordWrap = false;
        _editor.TextArea.TextView.BackgroundRenderers.Add(new IndentGuidesRenderer());
        _editor.TextArea.TextView.BackgroundRenderers.Add(new BookmarksRenderer(_bookmarks));
        _editor.TextArea.TextView.BackgroundRenderers.Add(_diagRenderer);
        _editor.Document = new AvaloniaEdit.Document.TextDocument(content);
        _editor.TextArea.SelectionBrush = new SolidColorBrush(Color.Parse("#264f78"));
        // Alt+drag → rectangle (block) selection — edit multiple lines at the same time
        _editor.Options.EnableRectangularSelection = true;

        // ⌘D / Ctrl+D — select next occurrence of current selection (or word)
        // ⌘F2 / Ctrl+F2 — toggle bookmark
        // F2 / Shift+F2 — jump to next / prev bookmark
        _editor.TextArea.KeyDown += (sender, e) =>
        {
            var meta = e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control);
            if (meta && e.Key == Key.D) { SelectNextOccurrence(); e.Handled = true; return; }
            if (meta && e.Key == Key.F2) { ToggleBookmark(); e.Handled = true; return; }
            if (!meta && e.Key == Key.F2)
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) PrevBookmark();
                else NextBookmark();
                e.Handled = true;
            }
        };
        _editor.TextArea.TextView.CurrentLineBackground = new SolidColorBrush(Color.Parse("#2a2d2e"));
        _editor.TextArea.TextView.CurrentLineBorder = new Pen(new SolidColorBrush(Color.Parse("#2a2d2e")));

        try
        {
            _installation = _editor.InstallTextMate(Registry);
            ApplyLanguage(filePath);
            _diagnosticsLabel = "tm-ok";
        }
        catch (Exception ex)
        {
            _installation = null;
            _diagnosticsLabel = "tm-fail: " + ex.GetType().Name;
            System.Diagnostics.Debug.WriteLine("TextMate install failed: " + ex);
        }

        _editor.Background = new SolidColorBrush(Color.Parse("#1e1e1e"));
        _editor.Foreground = new SolidColorBrush(Color.Parse("#d4d4d4"));

        // Find/Replace panel (⌘F / Ctrl+F)
        SearchPanel.Install(_editor);

        // Code folding
        InstallFolding();

        // Auto-completion
        InstallAutoComplete();

        _editor.TextArea.Caret.PositionChanged += (_, _) => UpdateStatus();
        _editor.TextChanged += (_, _) =>
        {
            if (!_dirty)
            {
                _dirty = true;
                DirtyChanged?.Invoke(this);
            }
            UpdateFolding();
            UpdateStatus();
        };

        _minimap = new Minimap(_editor);

        _status.FontFamily = new FontFamily("Menlo");
        _status.FontSize = 11;
        _status.Foreground = Brush(TextMuted);
        _status.Padding = new Thickness(10, 4);
        _status.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

        var wrapBtn = new Button
        {
            Content = "Wrap: off",
            Background = Avalonia.Media.Brushes.Transparent,
            BorderBrush = Avalonia.Media.Brushes.Transparent,
            Foreground = Brush(TextDim),
            FontSize = 11,
            FontFamily = new FontFamily("Menlo"),
            Padding = new Thickness(8, 2)
        };
        wrapBtn.Click += (_, _) =>
        {
            _editor.WordWrap = !_editor.WordWrap;
            wrapBtn.Content = _editor.WordWrap ? "Wrap: on" : "Wrap: off";
            wrapBtn.Foreground = Brush(_editor.WordWrap ? TextPrimary : TextDim);
        };

        var statusBar = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Background = Brush(MenuBackground)
        };
        statusBar.Children.Add(_status.At(column: 0));
        statusBar.Children.Add(wrapBtn.At(column: 1));

        _symbolsList = new ListBox
        {
            Background = new SolidColorBrush(Color.Parse("#1a1a1a")),
            Foreground = new SolidColorBrush(Color.Parse("#aeb8c2")),
            FontFamily = new FontFamily("Menlo"),
            FontSize = 11,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(4, 4)
        };
        _symbolsList.DoubleTapped += (_, _) =>
        {
            if (_symbolsList.SelectedItem is SymbolEntry sym) GoToLine(sym.Line);
        };

        var topGrid = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("180,*,110"),
            Background = new SolidColorBrush(Color.Parse("#1e1e1e"))
        };
        topGrid.Children.Add(_symbolsList.At(column: 0));
        topGrid.Children.Add(_editor.At(column: 1));
        topGrid.Children.Add(_minimap.At(column: 2));
        RefreshSymbols();
        _editor.TextChanged += (_, _) => RefreshSymbols();

        var root = new Grid
        {
            RowDefinitions = new RowDefinitions("*,22"),
            Background = new SolidColorBrush(Color.Parse("#1e1e1e"))
        };
        root.Children.Add(topGrid.At(row: 0));
        root.Children.Add(statusBar.At(row: 1));

        Content = root;
        UpdateStatus();
    }

    public string Language => _language;
    public string Text => _editor.Document?.Text ?? "";

    /// <summary>
    /// Push a fresh set of compiler diagnostics into the renderer + minimap. Called by
    /// the host (EditorWindow) every time a build/run finishes, replacing whatever was
    /// shown before. Pass an empty enumerable to clear.
    /// </summary>
    public void SetDiagnostics(IEnumerable<DiagnosticInfo> diagnostics)
    {
        var list = diagnostics.ToList();
        _diagRenderer.Set(list);
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        _minimap.SetDiagnosticLines(list.Select(d => (d.Line, d.Severity == "error")));
    }

    public void ClearDiagnostics() => SetDiagnostics(Array.Empty<DiagnosticInfo>());

    /// <summary>Append a single diagnostic (live-streaming from a running build).</summary>
    public void AddDiagnostic(DiagnosticInfo diagnostic)
    {
        _diagRenderer.Add(diagnostic);
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        _minimap.SetDiagnosticLines(_diagRenderer.Diagnostics.Select(kv => (kv.Key, kv.Value.Severity == "error")));
    }

    private void SelectNextOccurrence()
    {
        if (_editor.Document is null) return;
        var sel = _editor.SelectedText;
        if (string.IsNullOrEmpty(sel))
        {
            // No selection → select the word under the caret
            var caret = _editor.CaretOffset;
            var start = caret;
            var end = caret;
            while (start > 0 && IsWordChar(_editor.Document.GetCharAt(start - 1))) start--;
            while (end < _editor.Document.TextLength && IsWordChar(_editor.Document.GetCharAt(end))) end++;
            if (end > start)
            {
                _editor.Select(start, end - start);
            }
            return;
        }

        var text = _editor.Document.Text;
        var fromOffset = _editor.SelectionStart + _editor.SelectionLength;
        var idx = text.IndexOf(sel, fromOffset, StringComparison.Ordinal);
        if (idx < 0) idx = text.IndexOf(sel, 0, StringComparison.Ordinal);
        if (idx >= 0)
        {
            _editor.Select(idx, sel.Length);
            _editor.ScrollTo(_editor.Document.GetLocation(idx).Line, 0);
        }
    }

    private readonly ListBox _symbolsList;
    private readonly HashSet<int> _bookmarks = new();

    public IReadOnlyCollection<int> Bookmarks => _bookmarks;

    private int CurrentLine() => _editor.TextArea.Caret.Line;

    private void ToggleBookmark()
    {
        var line = CurrentLine();
        if (!_bookmarks.Add(line)) _bookmarks.Remove(line);
        _editor.TextArea.TextView.InvalidateLayer(AvaloniaEdit.Rendering.KnownLayer.Selection);
        _editor.TextArea.TextView.InvalidateVisual();
    }

    private void NextBookmark()
    {
        if (_bookmarks.Count == 0) return;
        var line = CurrentLine();
        var next = _bookmarks.Where(b => b > line).OrderBy(b => b).FirstOrDefault();
        if (next == 0) next = _bookmarks.Min();
        GoToLine(next);
    }

    private void PrevBookmark()
    {
        if (_bookmarks.Count == 0) return;
        var line = CurrentLine();
        var prev = _bookmarks.Where(b => b < line).OrderByDescending(b => b).FirstOrDefault();
        if (prev == 0) prev = _bookmarks.Max();
        GoToLine(prev);
    }

    private sealed record SymbolEntry(string Display, int Line)
    {
        public override string ToString() => Display;
    }

    private void RefreshSymbols()
    {
        var syms = GotoSymbolWindow.Extract(_editor.Document?.Text ?? "", _language);
        _symbolsList.ItemsSource = syms.Select(s => new SymbolEntry($"{s.Kind,-6} {s.Name}", s.LineNumber)).ToList();
    }

    public void GoToLine(int line)
    {
        if (_editor.Document is null) return;
        var safe = Math.Clamp(line, 1, _editor.Document.LineCount);
        var lineObj = _editor.Document.GetLineByNumber(safe);
        _editor.CaretOffset = lineObj.Offset;
        _editor.ScrollToLine(safe);
        _editor.TextArea.Focus();
    }

    public bool Save()
    {
        try
        {
            File.WriteAllText(_filePath, _editor.Document?.Text ?? "");
            _dirty = false;
            DirtyChanged?.Invoke(this);
            UpdateStatus();
            return true;
        }
        catch (Exception ex)
        {
            _diagnosticsLabel = "save-fail: " + ex.Message;
            UpdateStatus();
            return false;
        }
    }

    private CompletionWindow? _completion;

    private void InstallAutoComplete()
    {
        _editor.TextArea.TextEntered += OnTextEntered;
    }

    private void OnTextEntered(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || !char.IsLetter(e.Text[0])) return;
        if (_completion is not null && _completion.IsVisible) return;

        var caret = _editor.CaretOffset;
        var prefix = GetWordBefore(caret);
        if (prefix.Length < 2) return;

        var suggestions = CollectSuggestions(prefix).Take(40).ToList();
        if (suggestions.Count == 0) return;

        _completion = new CompletionWindow(_editor.TextArea)
        {
            MinWidth = 160,
            CloseAutomatically = true,
            CloseWhenCaretAtBeginning = true
        };
        var data = _completion.CompletionList.CompletionData;
        foreach (var word in suggestions) data.Add(new SimpleCompletion(word));
        _completion.Closed += (_, _) => _completion = null;
        _completion.Show();
    }

    private string GetWordBefore(int offset)
    {
        if (_editor.Document is null) return "";
        var start = offset;
        while (start > 0 && IsWordChar(_editor.Document.GetCharAt(start - 1))) start--;
        return _editor.Document.GetText(start, offset - start);
    }

    private static bool IsWordChar(char c) => char.IsLetterOrDigit(c) || c == '_';

    private IEnumerable<string> CollectSuggestions(string prefix)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // Keywords by language
        foreach (var kw in KeywordsFor(_language))
        {
            if (kw.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) set.Add(kw);
        }

        // Identifiers in the file
        if (_editor.Document is not null)
        {
            var text = _editor.Document.Text;
            var token = new System.Text.StringBuilder();
            foreach (var c in text)
            {
                if (IsWordChar(c)) token.Append(c);
                else
                {
                    if (token.Length >= prefix.Length)
                    {
                        var w = token.ToString();
                        if (w.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && w != prefix)
                            set.Add(w);
                    }
                    token.Clear();
                }
            }
        }
        return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase);
    }

    private static readonly string[] CsKeywords = { "using", "namespace", "public", "private", "internal", "protected", "static", "readonly", "class", "struct", "interface", "enum", "record", "void", "int", "string", "bool", "double", "float", "decimal", "var", "return", "if", "else", "for", "foreach", "while", "switch", "case", "break", "continue", "new", "this", "base", "null", "true", "false", "async", "await", "throw", "try", "catch", "finally", "is", "as", "in", "out", "ref" };
    private static readonly string[] PyKeywords = { "def", "class", "import", "from", "return", "if", "elif", "else", "for", "while", "True", "False", "None", "and", "or", "not", "in", "is", "lambda", "try", "except", "finally", "raise", "with", "as", "pass", "yield", "global", "nonlocal" };
    private static readonly string[] JsKeywords = { "function", "let", "const", "var", "if", "else", "for", "while", "do", "switch", "case", "break", "continue", "return", "true", "false", "null", "undefined", "new", "this", "class", "extends", "import", "from", "export", "default", "async", "await", "try", "catch", "finally", "throw", "typeof", "instanceof" };

    private static IEnumerable<string> KeywordsFor(string lang) => lang switch
    {
        "csharp" or "cs" => CsKeywords,
        "python" or "py" => PyKeywords,
        "javascript" or "js" or "typescript" or "ts" => JsKeywords,
        _ => CsKeywords.Concat(PyKeywords).Concat(JsKeywords).Distinct()
    };

    private void InstallFolding()
    {
        var ext = Path.GetExtension(_filePath).ToLowerInvariant();
        _foldingManager = FoldingManager.Install(_editor.TextArea);

        if (IsBraceLanguage(ext))
        {
            _foldingStrategy = new BraceFoldingStrategy();
        }
        else if (IsIndentLanguage(ext))
        {
            // Python / YAML use indentation; emit nested folds based on leading whitespace.
            _foldingStrategy = new IndentFoldingStrategy();
        }

        UpdateFolding();
    }

    private void UpdateFolding()
    {
        if (_foldingManager is null || _foldingStrategy is null || _editor.Document is null)
        {
            return;
        }

        if (_foldingStrategy is BraceFoldingStrategy brace)
        {
            brace.UpdateFoldings(_foldingManager, _editor.Document);
        }
        else if (_foldingStrategy is IndentFoldingStrategy indent)
        {
            indent.UpdateFoldings(_foldingManager, _editor.Document);
        }
    }

    private static bool IsBraceLanguage(string ext) =>
        ext is ".cs" or ".js" or ".ts" or ".jsx" or ".tsx" or ".java" or ".cpp" or ".cc" or ".c"
            or ".h" or ".hpp" or ".rs" or ".go" or ".kt" or ".swift" or ".scss" or ".css"
            or ".php" or ".json" or ".jsonc" or ".glsl" or ".vert" or ".frag" or ".geom"
            or ".tesc" or ".tese" or ".comp" or ".hlsl" or ".fx" or ".fxh" or ".cginc";

    private static bool IsIndentLanguage(string ext) =>
        ext is ".py" or ".yml" or ".yaml" or ".md" or ".markdown";

    /// <summary>
    /// Fallback ext→TextMate language-id map for extensions the registry doesn't bind
    /// by default. The bundled grammars in TextMateSharp.Grammars 1.0.65 include json,
    /// markdown, xml, and hlsl/shaderlab; .csproj / .axaml / shader-stage extensions
    /// don't auto-resolve so we re-route them here. Returns null if no override applies
    /// and the registry's default lookup should be used.
    /// </summary>
    private static string? FallbackLanguageId(string ext) => ext switch
    {
        ".json" or ".jsonc" or ".mfmap" => "json",
        ".md" or ".markdown" or ".mdown" or ".mkd" => "markdown",
        ".xml" or ".csproj" or ".vbproj" or ".fsproj" or ".props" or ".targets"
            or ".axaml" or ".axml" or ".config" or ".nuspec" or ".resx" or ".plist"
            or ".manifest" or ".storyboard" or ".xib" or ".xaml" => "xml",
        ".glsl" or ".vert" or ".frag" or ".geom" or ".tesc" or ".tese" or ".comp" => "glsl",
        ".hlsl" or ".fx" or ".fxh" or ".cginc" or ".compute" or ".shader" => "hlsl",
        ".yml" or ".yaml" => "yaml",
        ".html" or ".htm" or ".xhtml" => "html",
        ".css" => "css",
        ".scss" or ".sass" => "scss",
        ".less" => "less",
        ".sh" or ".bash" or ".zsh" or ".fish" => "shellscript",
        ".toml" => "ini",
        ".ini" or ".conf" => "ini",
        ".sql" => "sql",
        ".lua" => "lua",
        ".rs" => "rust",
        ".kt" or ".kts" => "kotlin",
        ".swift" => "swift",
        ".rb" => "ruby",
        ".php" => "php",
        ".go" => "go",
        ".dart" => "dart",
        ".dockerfile" => "dockerfile",
        _ => null,
    };

    private void ApplyLanguage(string filePath)
    {
        if (_installation is null) return;

        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        // Try the override map first — covers shader stages, .csproj/.axaml,
        // and our own .mfmap (which is JSON). Fall back to the registry's
        // built-in extension table for anything else (.cs, .py, .ts, ...).
        var fallback = FallbackLanguageId(ext);
        string? resolvedId = null;
        if (fallback is not null)
        {
            try
            {
                var scope = Registry.GetScopeByLanguageId(fallback);
                if (!string.IsNullOrEmpty(scope))
                {
                    _installation.SetGrammar(scope);
                    resolvedId = fallback;
                }
            }
            catch { /* grammar missing in this bundle — fall through to default lookup */ }
        }

        if (resolvedId is null)
        {
            var def = Registry.GetLanguageByExtension(ext);
            if (def is not null)
            {
                try
                {
                    _installation.SetGrammar(Registry.GetScopeByLanguageId(def.Id));
                    resolvedId = def.Id;
                }
                catch (Exception ex) { _diagnosticsLabel = "grammar-fail: " + ex.GetType().Name; }
            }
        }

        if (resolvedId is null)
        {
            _language = "plaintext";
            try { _installation.SetGrammar(null); } catch { /* ignored */ }
        }
        else
        {
            _language = resolvedId;
        }
    }

    private void UpdateStatus()
    {
        var caret = _editor.TextArea.Caret;
        var len = _editor.Document?.TextLength ?? 0;
        var dirtyMark = _dirty ? " ●" : "";
        var diag = string.IsNullOrEmpty(_diagnosticsLabel) ? "" : "    " + _diagnosticsLabel;
        _status.Text = $"{PrettyLanguageName(_language)}    Ln {caret.Line}, Col {caret.Column}    {len} chars{dirtyMark}{diag}";
    }

    /// <summary>Capitalize and prettify TextMate language ids for the status bar.</summary>
    private static string PrettyLanguageName(string id) => id switch
    {
        "csharp" => "C#",
        "cpp" => "C++",
        "javascript" => "JavaScript",
        "typescript" => "TypeScript",
        "python" => "Python",
        "json" => "JSON",
        "yaml" => "YAML",
        "xml" => "XML",
        "html" => "HTML",
        "css" => "CSS",
        "scss" => "SCSS",
        "markdown" => "Markdown",
        "glsl" => "GLSL",
        "hlsl" => "HLSL",
        "shellscript" => "Shell",
        "sql" => "SQL",
        "ini" => "INI",
        "plaintext" or "" => "Plain Text",
        _ => char.ToUpper(id[0]) + id[1..],
    };
}

/// <summary>
/// Minimal brace-based folding for C-family languages. Folds anything between { and }.
/// </summary>
internal sealed class BraceFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, AvaloniaEdit.Document.TextDocument document)
    {
        var foldings = CreateNewFoldings(document);
        manager.UpdateFoldings(foldings, -1);
    }

    private IEnumerable<NewFolding> CreateNewFoldings(AvaloniaEdit.Document.TextDocument document)
    {
        var newFoldings = new List<NewFolding>();
        var startOffsets = new Stack<int>();
        var lastNewLineOffset = 0;
        var openBrace = '{';
        var closeBrace = '}';

        for (var i = 0; i < document.TextLength; i++)
        {
            var c = document.GetCharAt(i);
            if (c == openBrace)
            {
                startOffsets.Push(i);
            }
            else if (c == closeBrace && startOffsets.Count > 0)
            {
                var startOffset = startOffsets.Pop();
                if (startOffset < lastNewLineOffset)
                {
                    newFoldings.Add(new NewFolding(startOffset, i + 1));
                }
            }
            else if (c == '\n' || c == '\r')
            {
                lastNewLineOffset = i + 1;
            }
        }

        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        return newFoldings;
    }
}

/// <summary>
/// Indent-based folding for Python / YAML. Each block of consecutive lines whose indent
/// is strictly deeper than the line above becomes a fold. Blank lines are skipped when
/// determining block boundaries so trailing blank lines don't accidentally close a block.
/// </summary>
internal sealed class IndentFoldingStrategy
{
    public void UpdateFoldings(FoldingManager manager, AvaloniaEdit.Document.TextDocument document)
    {
        var newFoldings = new List<NewFolding>();
        // Each entry: (the line that opened this block, its indent, its end offset).
        var open = new Stack<(int Line, int Indent, int StartOffset)>();

        for (var lineNum = 1; lineNum <= document.LineCount; lineNum++)
        {
            var line = document.GetLineByNumber(lineNum);
            var text = document.GetText(line);
            if (string.IsNullOrWhiteSpace(text)) continue;

            var indent = 0;
            while (indent < text.Length && (text[indent] == ' ' || text[indent] == '\t')) indent++;

            // Any open block whose start-line has indent >= current ends just before this line.
            while (open.Count > 0 && open.Peek().Indent >= indent)
            {
                var top = open.Pop();
                var endLineNum = FindPrevNonBlank(document, lineNum - 1, top.Line + 1);
                if (endLineNum > top.Line)
                {
                    var endLine = document.GetLineByNumber(endLineNum);
                    if (endLine.EndOffset > top.StartOffset)
                        newFoldings.Add(new NewFolding(top.StartOffset, endLine.EndOffset));
                }
            }

            open.Push((lineNum, indent, line.EndOffset));
        }

        // Close anything still on the stack at end-of-document.
        while (open.Count > 0)
        {
            var top = open.Pop();
            var endLineNum = FindPrevNonBlank(document, document.LineCount, top.Line + 1);
            if (endLineNum > top.Line)
            {
                var endLine = document.GetLineByNumber(endLineNum);
                if (endLine.EndOffset > top.StartOffset)
                    newFoldings.Add(new NewFolding(top.StartOffset, endLine.EndOffset));
            }
        }

        newFoldings.Sort((a, b) => a.StartOffset.CompareTo(b.StartOffset));
        manager.UpdateFoldings(newFoldings, -1);
    }

    private static int FindPrevNonBlank(AvaloniaEdit.Document.TextDocument doc, int from, int min)
    {
        for (var k = from; k >= min; k--)
        {
            var l = doc.GetLineByNumber(k);
            if (!string.IsNullOrWhiteSpace(doc.GetText(l))) return k;
        }
        return -1;
    }
}
