using System;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Represents an integer pixel-space rectangle within a specific atlas page.
/// Intended as a stable key for per-tile build planning and override application.
/// </summary>
internal readonly record struct AtlasRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;

    public int Bottom => Y + Height;

    public bool IsEmpty => Width <= 0 || Height <= 0;

    public void Deconstruct(out int x, out int y, out int width, out int height)
    {
        x = X;
        y = Y;
        width = Width;
        height = Height;
    }
}
