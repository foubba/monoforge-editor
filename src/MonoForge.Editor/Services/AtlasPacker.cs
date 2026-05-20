using System.Text.Json;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using SkiaSharp;

namespace MonoForge.Editor.Services;

public sealed class AtlasRegion
{
    public string Name { get; set; } = "";
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; }
    public int H { get; set; }
}

public sealed class AtlasResult
{
    public List<AtlasRegion> Regions { get; set; } = new();
    public int Width { get; set; }
    public int Height { get; set; }
}

public static class AtlasPacker
{
    /// <summary>
    /// Skyline-bottom-left bin packing of all the given images into a single PNG +
    /// JSON metadata describing each region.
    /// </summary>
    public static AtlasResult Pack(IEnumerable<string> imagePaths, string outputPngPath, string outputJsonPath, int padding = 2, int maxSize = 4096)
    {
        var inputs = imagePaths
            .Select(p => new { Path = p, Bitmap = TryLoadSkia(p) })
            .Where(x => x.Bitmap is not null)
            .OrderByDescending(x => Math.Max(x.Bitmap!.Width, x.Bitmap.Height))
            .ToList();

        var result = new AtlasResult();
        if (inputs.Count == 0) return result;

        // Start small and grow until everything fits.
        var size = 256;
        List<(string Path, SKBitmap Bitmap, int X, int Y)> placed = new();
        while (size <= maxSize)
        {
            placed = new List<(string, SKBitmap, int, int)>();
            var skyline = new List<(int X, int Y, int Width)> { (0, 0, size) };
            var fits = true;

            foreach (var input in inputs)
            {
                var bw = input.Bitmap!.Width + padding;
                var bh = input.Bitmap.Height + padding;
                if (!TryPlace(skyline, bw, bh, size, out var px, out var py))
                {
                    fits = false;
                    break;
                }
                placed.Add((input.Path, input.Bitmap, px, py));
            }

            if (fits) break;
            size *= 2;
        }

        using var output = new SKBitmap(size, size, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(output);
        canvas.Clear(SKColors.Transparent);
        foreach (var (path, bmp, x, y) in placed)
        {
            canvas.DrawBitmap(bmp, x, y);
            result.Regions.Add(new AtlasRegion
            {
                Name = Path.GetFileNameWithoutExtension(path),
                X = x,
                Y = y,
                W = bmp.Width,
                H = bmp.Height
            });
            bmp.Dispose();
        }
        result.Width = size;
        result.Height = size;

        using (var image = SKImage.FromBitmap(output))
        using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
        using (var stream = File.OpenWrite(outputPngPath))
        {
            data.SaveTo(stream);
        }

        File.WriteAllText(outputJsonPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        return result;
    }

    private static bool TryPlace(List<(int X, int Y, int Width)> skyline, int w, int h, int binSize, out int outX, out int outY)
    {
        outX = -1; outY = int.MaxValue;
        var bestIndex = -1;
        for (var i = 0; i < skyline.Count; i++)
        {
            var (sx, sy, sw) = skyline[i];
            if (sx + w > binSize) continue;
            var maxY = sy;
            var widthAccum = 0;
            var j = i;
            while (widthAccum < w && j < skyline.Count)
            {
                maxY = Math.Max(maxY, skyline[j].Y);
                widthAccum += skyline[j].Width;
                j++;
            }
            if (widthAccum < w) continue;
            if (maxY + h > binSize) continue;
            if (maxY < outY)
            {
                outY = maxY;
                outX = sx;
                bestIndex = i;
            }
        }

        if (bestIndex < 0) return false;

        // Update skyline: merge segments covered by the placement and add new segment on top.
        var newSegments = new List<(int X, int Y, int Width)> { (outX, outY + h, w) };
        var consumed = 0;
        var k = bestIndex;
        while (consumed < w && k < skyline.Count)
        {
            var seg = skyline[k];
            consumed += seg.Width;
            k++;
        }
        var leftover = consumed - w;
        if (leftover > 0)
        {
            var last = skyline[k - 1];
            newSegments.Add((outX + w, last.Y, leftover));
        }
        skyline.RemoveRange(bestIndex, k - bestIndex);
        skyline.InsertRange(bestIndex, newSegments);

        // Merge adjacent segments of equal Y
        for (var i = skyline.Count - 1; i > 0; i--)
        {
            if (skyline[i].Y == skyline[i - 1].Y)
            {
                skyline[i - 1] = (skyline[i - 1].X, skyline[i - 1].Y, skyline[i - 1].Width + skyline[i].Width);
                skyline.RemoveAt(i);
            }
        }

        return true;
    }

    private static SKBitmap? TryLoadSkia(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return SKBitmap.Decode(stream);
        }
        catch
        {
            return null;
        }
    }
}
