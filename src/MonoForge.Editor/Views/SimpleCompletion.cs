using Avalonia.Media;
using AvaloniaEdit.CodeCompletion;
using AvaloniaEdit.Document;
using AvaloniaEdit.Editing;

namespace MonoForge.Editor.Views;

internal sealed class SimpleCompletion : ICompletionData
{
    public SimpleCompletion(string text) { Text = text; }
    public IImage? Image => null;
    public string Text { get; }
    public object Content => Text;
    public object Description => Text;
    public double Priority => 0;
    public void Complete(TextArea textArea, ISegment completionSegment, EventArgs insertionRequestEventArgs)
    {
        textArea.Document.Replace(completionSegment, Text);
    }
}
