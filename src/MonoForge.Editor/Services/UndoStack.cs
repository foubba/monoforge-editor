using System.Text.Json;
using MonoForge.Editor.Models;

namespace MonoForge.Editor.Services;

public sealed class UndoStack
{
    private readonly Stack<string> _undo = new();
    private readonly Stack<string> _redo = new();
    private const int MaxDepth = 80;

    public bool CanUndo => _undo.Count > 0;
    public bool CanRedo => _redo.Count > 0;

    public void Capture(SceneDocument scene)
    {
        var snapshot = JsonSerializer.Serialize(scene);
        if (_undo.TryPeek(out var top) && top == snapshot)
        {
            return;
        }

        _undo.Push(snapshot);
        _redo.Clear();
        while (_undo.Count > MaxDepth)
        {
            var tail = _undo.ToArray();
            _undo.Clear();
            for (var i = tail.Length - 2; i >= 0; i--)
            {
                _undo.Push(tail[i]);
            }
        }
    }

    public SceneDocument? Undo(SceneDocument current)
    {
        if (_undo.Count == 0)
        {
            return null;
        }

        _redo.Push(JsonSerializer.Serialize(current));
        var snapshot = _undo.Pop();
        return JsonSerializer.Deserialize<SceneDocument>(snapshot);
    }

    public SceneDocument? Redo(SceneDocument current)
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        _undo.Push(JsonSerializer.Serialize(current));
        var snapshot = _redo.Pop();
        return JsonSerializer.Deserialize<SceneDocument>(snapshot);
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
