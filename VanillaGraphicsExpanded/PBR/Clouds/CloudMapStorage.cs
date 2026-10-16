namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Identifies borrowed native storage; integer names alone do not identify an allocation lifetime.</summary>
internal readonly record struct CloudMapStorage(int Map, int Raw1, int Raw2, int Framebuffer, int Width, int TileSize, int Color)
{
    /// <summary>The inspected native shaders require fifty world units per column coordinate.</summary>
    internal bool IsSupported => Map > 0 && Raw1 > 0 && Raw2 > 0 && Color > 0 && Framebuffer > 0 && Width is >= 20 and <= 200 && TileSize == 50;
}

