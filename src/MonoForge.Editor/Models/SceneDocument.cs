namespace MonoForge.Editor.Models;

public sealed class SceneDocument
{
    public string Name { get; set; } = "main.collection";
    public List<SceneObject> Objects { get; set; } = [];

    public IEnumerable<SceneObject> Flatten()
    {
        foreach (var obj in Objects)
        {
            foreach (var item in FlattenObject(obj))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<SceneObject> FlattenObject(SceneObject obj)
    {
        yield return obj;
        foreach (var child in obj.Children)
        {
            child.Parent = obj;
            foreach (var sub in FlattenObject(child))
            {
                yield return sub;
            }
        }
    }

    public SceneObject? Find(string id)
    {
        return Flatten().FirstOrDefault(o => o.Id == id);
    }

    public bool Remove(string id)
    {
        if (Objects.RemoveAll(o => o.Id == id) > 0)
        {
            return true;
        }

        foreach (var obj in Flatten())
        {
            if (obj.Children.RemoveAll(c => c.Id == id) > 0)
            {
                return true;
            }
        }

        return false;
    }
}
