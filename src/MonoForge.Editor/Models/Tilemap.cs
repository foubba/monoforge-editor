namespace MonoForge.Editor.Models;

public sealed class Tilemap
{
    public string TilesetPath { get; set; } = "";
    public int TileSize { get; set; } = 16;
    public int Width { get; set; } = 32;
    public int Height { get; set; } = 24;
    public int[] Tiles { get; set; } = Array.Empty<int>(); // -1 = empty, else tile index in tileset

    public static Tilemap Empty(int w, int h, int tile)
    {
        var arr = new int[w * h];
        Array.Fill(arr, -1);
        return new Tilemap { Width = w, Height = h, TileSize = tile, Tiles = arr };
    }
}
