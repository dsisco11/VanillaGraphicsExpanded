using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Collections;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Reads selected physical lighting tiles without copying an entire atlas.</summary>
internal static class SurfaceLightingPageReadback
{
    #region Tile observations
    /// <summary>Reads the selected tile into pooled storage that the caller must dispose.</summary>
    public static PooledArray<float> Read(Texture3D texture,in SurfaceLightingSnapshot snapshot,uint page)
    {
        int physical=checked((int)page)-1,local=physical%snapshot.TilesPerAtlas;
        return texture.ReadPixelsRegion((local%snapshot.TilesPerAxis)*snapshot.TileSize,
            (local/snapshot.TilesPerAxis)*snapshot.TileSize, snapshot.TileSize, snapshot.TileSize,
            physical/snapshot.TilesPerAtlas);
    }
    #endregion
}
