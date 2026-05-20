using System.Diagnostics;
using System.Text.Json;

namespace MonoForge.Editor.Services;

/// <summary>Per-turn token usage emitted by the CLI in its final `result` event.</summary>
public sealed record ClaudeUsage(int InputTokens, int OutputTokens, int CacheReadTokens, int CacheWriteTokens, double DurationSeconds);

/// <summary>
/// Thin wrapper around the Claude Code CLI (`claude`). Auth happens out-of-band — the
/// user runs `claude auth login` once and the CLI keeps the token in its own store; we
/// never see API keys.
/// </summary>
public static class ClaudeCodeService
{
    private static string? _cachedPath;
    private static bool _searched;

    /// <summary>Locate the `claude` binary. PATH first, then a few common install dirs.</summary>
    public static string? FindClaudeBinary()
    {
        if (_searched) return _cachedPath;
        _searched = true;

        var fromPath = TryWhich("claude");
        if (fromPath is not null) { _cachedPath = fromPath; return fromPath; }

        // Standard install locations on macOS / Linux. Windows users typically have it
        // on PATH already (npm global bin), so the above `which` covers them.
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new[]
        {
            "/opt/homebrew/bin/claude",
            "/usr/local/bin/claude",
            Path.Combine(home, ".claude/local/bin/claude"),
            Path.Combine(home, ".local/bin/claude"),
            Path.Combine(home, ".npm-global/bin/claude"),
        };
        foreach (var c in candidates)
        {
            if (File.Exists(c)) { _cachedPath = c; return c; }
        }
        return null;
    }

    /// <summary>True if `claude` is installed and reachable.</summary>
    public static bool IsAvailable => FindClaudeBinary() is not null;

    /// <summary>Forget the cached lookup so a fresh search runs (e.g. right after install).</summary>
    public static void ResetCache()
    {
        _cachedPath = null;
        _searched = false;
    }

    /// <summary>
    /// Send a non-interactive prompt to Claude Code. The prompt is fed via stdin so
    /// multi-line content and special characters need no shell-escaping. Output streams
    /// back one line at a time via <paramref name="onLine"/>; stderr goes through
    /// <paramref name="onError"/> when supplied (otherwise it merges with stdout).
    /// </summary>
    public static Task<int> RunPromptAsync(
        string prompt,
        string workingDir,
        Action<string> onLine,
        Action<string>? onError = null,
        CancellationToken ct = default)
        => RunPromptWithArgsAsync("--print --permission-mode acceptEdits", prompt, workingDir, onLine, onError, ct);

    /// <summary>
    /// Same as <see cref="RunPromptAsync"/> but the caller controls the full argument
    /// string. Use this when you want to pass --model, --session-id, --resume, etc.
    /// The prompt is still piped through stdin so it can contain any characters.
    /// </summary>
    /// <summary>
    /// Streaming variant — parses Claude Code's stream-json output and fires callbacks
    /// for partial text deltas (so the UI can grow the reply token-by-token), tool-use
    /// notifications (so the UI can show "→ Editing Game.cs"), and the final usage
    /// totals.
    /// </summary>
    public static async Task<int> RunStreamingPromptAsync(
        string baseArgs,
        string prompt,
        string workingDir,
        Action<string> onTextDelta,
        Action<string> onToolUse,
        Action<ClaudeUsage>? onUsage,
        Action<string>? onError,
        CancellationToken ct)
    {
        // stream-json + include-partial-messages = token-level streaming.
        var args = baseArgs + " --output-format stream-json --include-partial-messages --verbose";
        return await RunPromptWithArgsAsync(args, prompt, workingDir,
            line => ParseStreamLine(line, onTextDelta, onToolUse, onUsage),
            line => ParseStreamLine(line, onTextDelta, onToolUse, onUsage, isError: true, onError),
            ct);
    }

    private static void ParseStreamLine(
        string line,
        Action<string> onTextDelta,
        Action<string> onToolUse,
        Action<ClaudeUsage>? onUsage,
        bool isError = false,
        Action<string>? onError = null)
    {
        if (string.IsNullOrWhiteSpace(line)) return;
        try
        {
            using var doc = JsonDocument.Parse(line);
            var root = doc.RootElement;
            if (!root.TryGetProperty("type", out var typeEl)) return;
            var type = typeEl.GetString();

            switch (type)
            {
                case "stream_event":
                    if (!root.TryGetProperty("event", out var evt)) break;
                    if (!evt.TryGetProperty("type", out var evtTypeEl)) break;
                    var evtType = evtTypeEl.GetString();
                    // content_block_delta → text_delta carries the streamed token text.
                    if (evtType == "content_block_delta" && evt.TryGetProperty("delta", out var delta))
                    {
                        if (delta.TryGetProperty("type", out var dt) && dt.GetString() == "text_delta"
                            && delta.TryGetProperty("text", out var txt))
                            onTextDelta(txt.GetString() ?? "");
                    }
                    break;
                case "assistant":
                    // Full assistant message arrives at the end of each turn. Look for
                    // tool_use blocks so we can announce what Claude did.
                    if (root.TryGetProperty("message", out var msg) &&
                        msg.TryGetProperty("content", out var contentArr) &&
                        contentArr.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in contentArr.EnumerateArray())
                        {
                            if (item.TryGetProperty("type", out var itTy) && itTy.GetString() == "tool_use"
                                && item.TryGetProperty("name", out var nameEl))
                            {
                                onToolUse(nameEl.GetString() ?? "(tool)");
                            }
                        }
                    }
                    break;
                case "result":
                    if (onUsage is not null && root.TryGetProperty("usage", out var u))
                    {
                        var inTok = u.TryGetProperty("input_tokens", out var i) ? i.GetInt32() : 0;
                        var outTok = u.TryGetProperty("output_tokens", out var o) ? o.GetInt32() : 0;
                        var cacheR = u.TryGetProperty("cache_read_input_tokens", out var cr) ? cr.GetInt32() : 0;
                        var cacheW = u.TryGetProperty("cache_creation_input_tokens", out var cw) ? cw.GetInt32() : 0;
                        var dur = root.TryGetProperty("duration_ms", out var d) ? d.GetDouble() / 1000.0 : 0;
                        onUsage(new ClaudeUsage(inTok, outTok, cacheR, cacheW, dur));
                    }
                    break;
            }
        }
        catch
        {
            // Not JSON (rare — maybe an unhandled stderr line). Surface as an error.
            if (isError && onError is not null) onError(line);
        }
    }

    public static Task<int> RunPromptWithArgsAsync(
        string args,
        string prompt,
        string workingDir,
        Action<string> onLine,
        Action<string>? onError = null,
        CancellationToken ct = default)
    {
        var binary = FindClaudeBinary()
            ?? throw new InvalidOperationException("Claude CLI not found");

        var tcs = new TaskCompletionSource<int>();
        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = binary,
                Arguments = args,
                WorkingDirectory = workingDir,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += (_, e) => { if (e.Data is { } line) onLine(line); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is { } line) (onError ?? onLine)(line); };
        process.Exited += (_, _) => { tcs.TrySetResult(process.ExitCode); process.Dispose(); };

        ct.Register(() =>
        {
            try { if (!process.HasExited) process.Kill(true); } catch { /* ignored */ }
        });

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            process.StandardInput.Write(prompt);
            process.StandardInput.Close();
        }
        catch (Exception ex)
        {
            onLine("Failed to start claude: " + ex.Message);
            tcs.TrySetResult(-1);
        }

        return tcs.Task;
    }

    private static string? TryWhich(string name)
    {
        try
        {
            var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/usr/bin/which",
                    Arguments = name,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                }
            };
            p.Start();
            var output = p.StandardOutput.ReadToEnd().Trim();
            p.WaitForExit();
            if (p.ExitCode == 0 && !string.IsNullOrEmpty(output) && File.Exists(output)) return output;
        }
        catch { /* ignored */ }
        return null;
    }
}
