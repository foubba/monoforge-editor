using System.Numerics;
using System.Text.Json;

namespace MonoForge.Editor.Services;

public sealed class RigJoint
{
    public string Name { get; set; } = "";
    public int Parent { get; set; } = -1;
    public Vector3 Position { get; set; }
}

public sealed class Rig
{
    public List<RigJoint> Joints { get; set; } = new();
}

/// <summary>
/// Defines a humanoid biped template skeleton (Mixamo-style) with 18 joints, and a
/// procedure to fit it to a set of user-provided landmark positions on a mesh.
/// </summary>
public static class AutoRig
{
    public static readonly string[] LandmarkNames =
    {
        "Head",
        "Neck",
        "Chest",
        "Pelvis",
        "Shoulder.L", "Elbow.L", "Hand.L",
        "Shoulder.R", "Elbow.R", "Hand.R",
        "Hip.L", "Knee.L", "Foot.L",
        "Hip.R", "Knee.R", "Foot.R"
    };

    public static Rig BuildBiped(Dictionary<string, Vector3> landmarks)
    {
        // Resolve each landmark, defaulting to the named-joint pos or pelvis if missing.
        Vector3 L(string k) => landmarks.TryGetValue(k, out var v) ? v : Vector3.Zero;

        var rig = new Rig();
        int Add(string name, int parent, Vector3 pos)
        {
            var idx = rig.Joints.Count;
            rig.Joints.Add(new RigJoint { Name = name, Parent = parent, Position = pos });
            return idx;
        }

        var hips = Add("Hips", -1, L("Pelvis"));
        var spine = Add("Spine", hips, Vector3.Lerp(L("Pelvis"), L("Chest"), 0.5f));
        var chest = Add("Chest", spine, L("Chest"));
        var neck = Add("Neck", chest, L("Neck"));
        var head = Add("Head", neck, L("Head"));

        var shoulderL = Add("Shoulder.L", chest, L("Shoulder.L"));
        var elbowL = Add("UpperArm.L", shoulderL, L("Elbow.L"));
        var handL = Add("Forearm.L", elbowL, L("Hand.L"));

        var shoulderR = Add("Shoulder.R", chest, L("Shoulder.R"));
        var elbowR = Add("UpperArm.R", shoulderR, L("Elbow.R"));
        var handR = Add("Forearm.R", elbowR, L("Hand.R"));

        var hipL = Add("Hip.L", hips, L("Hip.L"));
        var kneeL = Add("Knee.L", hipL, L("Knee.L"));
        var footL = Add("Foot.L", kneeL, L("Foot.L"));

        var hipR = Add("Hip.R", hips, L("Hip.R"));
        var kneeR = Add("Knee.R", hipR, L("Knee.R"));
        var footR = Add("Foot.R", kneeR, L("Foot.R"));

        return rig;
    }

    public static void Save(Rig rig, string path)
    {
        File.WriteAllText(path, JsonSerializer.Serialize(rig, new JsonSerializerOptions { WriteIndented = true }));
    }
}
