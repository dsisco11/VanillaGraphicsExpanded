namespace VanillaGraphicsExpanded.LumOn.Scene;

internal static class LumonSceneOccupancyPacking
{
    // Packed R32UI per cell:
    // - blockLevel: 6 bits (0..32)
    // - sunLevel:   6 bits (0..32)
    // - lightId:    6 bits (0..63)
    // - materialPaletteIndex: 14 bits (0..16383)

    public const int BlockLevelBits = 6;
    public const int SunLevelBits = 6;
    public const int LightIdBits = 6;
    public const int MaterialPaletteIndexBits = 14;

    public const int BlockLevelShift = 0;
    public const int SunLevelShift = BlockLevelShift + BlockLevelBits;
    public const int LightIdShift = SunLevelShift + SunLevelBits;
    public const int MaterialPaletteIndexShift = LightIdShift + LightIdBits;

    public const uint BlockLevelMask = (1u << BlockLevelBits) - 1u;
    public const uint SunLevelMask = (1u << SunLevelBits) - 1u;
    public const uint LightIdMask = (1u << LightIdBits) - 1u;
    public const uint MaterialPaletteIndexMask = (1u << MaterialPaletteIndexBits) - 1u;

    public static uint Pack(uint blockLevel, uint sunLevel, uint lightId, uint materialPaletteIndex)
    {
        return
            ((blockLevel & BlockLevelMask) << BlockLevelShift) |
            ((sunLevel & SunLevelMask) << SunLevelShift) |
            ((lightId & LightIdMask) << LightIdShift) |
            ((materialPaletteIndex & MaterialPaletteIndexMask) << MaterialPaletteIndexShift);
    }

    public static uint PackClamped(int blockLevel, int sunLevel, int lightId, int materialPaletteIndex)
    {
        uint bl = (uint)Clamp(blockLevel, 0, 32);
        uint sl = (uint)Clamp(sunLevel, 0, 32);
        uint lid = (uint)Clamp(lightId, 0, (int)LightIdMask);
        uint mpi = (uint)Clamp(materialPaletteIndex, 0, (int)MaterialPaletteIndexMask);
        return Pack(bl, sl, lid, mpi);
    }

    public static uint UnpackBlockLevel(uint packed) => (packed >> BlockLevelShift) & BlockLevelMask;
    public static uint UnpackSunLevel(uint packed) => (packed >> SunLevelShift) & SunLevelMask;
    public static uint UnpackLightId(uint packed) => (packed >> LightIdShift) & LightIdMask;
    public static uint UnpackMaterialPaletteIndex(uint packed) => (packed >> MaterialPaletteIndexShift) & MaterialPaletteIndexMask;

    private static int Clamp(int v, int min, int max)
    {
        if (v < min) return min;
        if (v > max) return max;
        return v;
    }
}

