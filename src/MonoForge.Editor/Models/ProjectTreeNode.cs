namespace MonoForge.Editor.Models;

public sealed class ProjectTreeNode
{
    public string Name { get; init; } = "";
    public string FullPath { get; init; } = "";
    public bool IsDirectory { get; init; }
    public List<ProjectTreeNode> Children { get; } = [];
}
