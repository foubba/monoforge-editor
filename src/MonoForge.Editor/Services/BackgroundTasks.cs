using Avalonia.Threading;

namespace MonoForge.Editor.Services;

/// <summary>
/// Lightweight registry of in-flight background operations. Any long-running async work
/// (project open scans, dotnet build/run, asset reloads…) acquires a <see cref="Token"/>
/// via <see cref="Begin"/>, updates its message / progress as it runs, then disposes the
/// token to signal completion. A status-bar widget subscribes to <see cref="Changed"/>
/// and shows the currently-running tasks with a spinner so the user never wonders why
/// the UI is quiet.
///
/// Threading: events always fire on the Avalonia UI thread, so subscribers can update
/// controls directly without marshaling.
/// </summary>
public static class BackgroundTasks
{
    private static readonly List<Token> _active = new();
    private static readonly object _lock = new();

    /// <summary>Fired whenever a task starts, updates, or completes. Always on UI thread.</summary>
    public static event Action? Changed;

    /// <summary>Snapshot of tasks currently in flight, oldest first.</summary>
    public static IReadOnlyList<Token> Active
    {
        get { lock (_lock) return _active.ToArray(); }
    }

    /// <summary>Start tracking a new task. Dispose the returned token (or call
    /// <see cref="Token.Complete"/>) when the work finishes — both remove it from the
    /// active set and fire <see cref="Changed"/>.</summary>
    public static Token Begin(string title)
    {
        var token = new Token(title);
        lock (_lock) _active.Add(token);
        RaiseChanged();
        return token;
    }

    internal static void Remove(Token token)
    {
        bool removed;
        lock (_lock) removed = _active.Remove(token);
        if (removed) RaiseChanged();
    }

    internal static void RaiseChanged()
    {
        if (Dispatcher.UIThread.CheckAccess()) Changed?.Invoke();
        else Dispatcher.UIThread.Post(() => Changed?.Invoke());
    }

    public sealed class Token : IDisposable
    {
        public string Title { get; private set; }
        /// <summary>Optional 0..1 progress. Null means indeterminate (just spin).</summary>
        public double? Progress { get; private set; }
        /// <summary>Optional detail line shown alongside the title (e.g. current file).</summary>
        public string? Detail { get; private set; }

        private bool _completed;

        internal Token(string title) { Title = title; }

        public void Update(string? detail = null, double? progress = null)
        {
            if (detail is not null) Detail = detail;
            if (progress is not null) Progress = Math.Clamp(progress.Value, 0, 1);
            RaiseChanged();
        }

        public void Rename(string title)
        {
            Title = title;
            RaiseChanged();
        }

        public void Complete()
        {
            if (_completed) return;
            _completed = true;
            Remove(this);
        }

        public void Dispose() => Complete();
    }
}
