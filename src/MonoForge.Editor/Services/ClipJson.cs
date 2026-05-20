using System.Numerics;
using System.Text.Json;
using MonoForge.Editor.Views;

namespace MonoForge.Editor.Services;

/// <summary>
/// Serialize / deserialize an animation clip to a minimal MonoForge-specific JSON.
/// Channels reference joints by name (so clips travel with the rig, not just one mesh).
/// </summary>
internal static class ClipJson
{
    public sealed class DTO
    {
        public string Name { get; set; } = "";
        public float Duration { get; set; }
        public List<ChannelDTO> Channels { get; set; } = new();
    }

    public sealed class ChannelDTO
    {
        public string Joint { get; set; } = "";
        public string Property { get; set; } = ""; // translation | rotation | scale
        public float[] Times { get; set; } = Array.Empty<float>();
        public float[] Values { get; set; } = Array.Empty<float>();  // 3 or 4 per key
    }

    public static string Serialize(AnimClip clip, ModelData model)
    {
        var dto = new DTO { Name = clip.Name, Duration = clip.Duration };
        foreach (var ch in clip.Channels)
        {
            var name = ch.JointIndex >= 0 && ch.JointIndex < model.JointNames.Count
                ? model.JointNames[ch.JointIndex]
                : $"#{ch.JointIndex}";
            var cd = new ChannelDTO { Joint = name, Property = ch.Property, Times = (float[])ch.Times.Clone() };
            if (ch.Property == "rotation")
            {
                var f = new float[ch.QuatValues.Length * 4];
                for (var i = 0; i < ch.QuatValues.Length; i++)
                {
                    var q = ch.QuatValues[i];
                    f[i * 4] = q.X; f[i * 4 + 1] = q.Y; f[i * 4 + 2] = q.Z; f[i * 4 + 3] = q.W;
                }
                cd.Values = f;
            }
            else
            {
                var f = new float[ch.Vec3Values.Length * 3];
                for (var i = 0; i < ch.Vec3Values.Length; i++)
                {
                    var v = ch.Vec3Values[i];
                    f[i * 3] = v.X; f[i * 3 + 1] = v.Y; f[i * 3 + 2] = v.Z;
                }
                cd.Values = f;
            }
            dto.Channels.Add(cd);
        }
        return JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
    }

    public static AnimClip Deserialize(string json, ModelData model)
    {
        var dto = JsonSerializer.Deserialize<DTO>(json) ?? throw new InvalidDataException("Empty clip JSON");
        var clip = new AnimClip { Name = dto.Name, Duration = dto.Duration };
        var nameToIdx = new Dictionary<string, int>();
        for (var i = 0; i < model.JointNames.Count; i++) nameToIdx[model.JointNames[i]] = i;
        foreach (var c in dto.Channels)
        {
            if (!nameToIdx.TryGetValue(c.Joint, out var ji)) continue;
            var ch = new AnimChannel { JointIndex = ji, Property = c.Property, Times = c.Times };
            if (c.Property == "rotation")
            {
                var q = new Quaternion[c.Times.Length];
                for (var i = 0; i < q.Length; i++) q[i] = new Quaternion(c.Values[i * 4], c.Values[i * 4 + 1], c.Values[i * 4 + 2], c.Values[i * 4 + 3]);
                ch.QuatValues = q;
            }
            else
            {
                var v = new Vector3[c.Times.Length];
                for (var i = 0; i < v.Length; i++) v[i] = new Vector3(c.Values[i * 3], c.Values[i * 3 + 1], c.Values[i * 3 + 2]);
                ch.Vec3Values = v;
            }
            clip.Channels.Add(ch);
        }
        return clip;
    }
}
