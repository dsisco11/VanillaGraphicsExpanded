using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Selects and snapshots exact physical lighting pages without transferring their contents to the CPU.</summary>
internal static class SurfaceLightingPageSelection
{
    #region Tile observations
    /// <summary>Retains the selected page as an independent GPU snapshot for current or historical assertions.</summary>
    public static Assertions.GpuImageSnapshot Capture(Texture3D texture, in SurfaceLightingSnapshot snapshot, uint page)
        => new(texture, Region(snapshot, page));

    /// <summary>Maps one one-based physical page to its complete tile region and atlas layer.</summary>
    public static RHITextureRegion Region(in SurfaceLightingSnapshot snapshot, uint page)
    {
        int physical=checked((int)page)-1,local=physical%snapshot.TilesPerAtlas;
        return new(new(0,physical/snapshot.TilesPerAtlas),
            (local%snapshot.TilesPerAxis)*snapshot.TileSize, (local/snapshot.TilesPerAxis)*snapshot.TileSize,
            0, snapshot.TileSize, snapshot.TileSize);
    }
    #endregion
}
