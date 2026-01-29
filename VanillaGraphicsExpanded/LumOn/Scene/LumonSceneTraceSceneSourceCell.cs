using System.Runtime.InteropServices;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Phase 23.3: Minimal per-worldCell source data for TraceScene occupancy payload packing.
/// Intended to be the element type of a <c>PooledChunkSnapshot&lt;T&gt;</c> produced by the async chunk snapshot source.
/// </summary>
/// <remarks>
/// This struct is CPU-side only. The actual GPU clipmap payload is packed via <see cref="LumonSceneOccupancyPacking"/>.
/// </remarks>
[StructLayout(LayoutKind.Sequential)]
internal readonly record struct LumonSceneTraceSceneSourceCell
{
    /// <summary>
    /// 0 = empty, non-zero = solid/occupied.
    /// </summary>
    public readonly byte IsSolid;

    /// <summary>
    /// Block light intensity in [0..32].
    /// </summary>
    public readonly byte BlockLevel;

    /// <summary>
    /// Sun light intensity in [0..32].
    /// </summary>
    public readonly byte SunLevel;

    /// <summary>
    /// Light color index into the LUT (v1: 0..63).
    /// </summary>
    public readonly byte LightId;

    /// <summary>
    /// Material palette index in [0..16383].
    /// </summary>
    public readonly ushort MaterialPaletteIndex;

    /// <summary>
    /// Optional padding/reserved for future flags (keeps the struct 8 bytes).
    /// </summary>
    public readonly ushort Reserved0;

    public LumonSceneTraceSceneSourceCell(
        byte isSolid,
        byte blockLevel,
        byte sunLevel,
        byte lightId,
        ushort materialPaletteIndex,
        ushort reserved0 = 0)
    {
        IsSolid = isSolid;
        BlockLevel = blockLevel;
        SunLevel = sunLevel;
        LightId = lightId;
        MaterialPaletteIndex = materialPaletteIndex;
        Reserved0 = reserved0;
    }
}

