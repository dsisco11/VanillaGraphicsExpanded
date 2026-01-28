using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal static class LumOnWorldProbeLayout
{
    #region Versioning

    public const int WorldProbeLayoutVersion = 2;

    public const string WorldProbeLayoutTag = "vge.lumon.worldprobes.v2";

    #endregion

    #region Texture Counts

    public const int ShL1TextureCount = 3;

    public const int ShL2TextureCount = 7;

    #endregion

    #region Formats

    public static readonly PixelInternalFormat ShL1Format = PixelInternalFormat.Rgba16f;

    public static readonly PixelInternalFormat RadianceAtlasFormat = PixelInternalFormat.Rgba16f;

    public static readonly PixelInternalFormat VisibilityFormat = PixelInternalFormat.Rgba16f;

    public static readonly PixelInternalFormat DistanceFormat = PixelInternalFormat.Rg16f;

    public static readonly PixelInternalFormat MetaFormat = PixelInternalFormat.Rg32f;

    #endregion

    #region World-Probe Atlas Packing (Octahedral)

    /// <summary>
    /// Default world-probe octahedral tile size (S) used when no override is supplied.
    /// Must match the shader fallback in assets/vanillagraphicsexpanded/shaders/includes/lumon_worldprobe_atlas.glsl.
    /// </summary>
    public const int DefaultWorldProbeOctahedralTileSize = 16;

    /// <summary>
    /// Per-probe scalar atlas size (old scheme): one texel per probe.
    /// W = N*N, H = (N*levels)
    /// </summary>
    public static (int Width, int Height) GetPerProbeAtlasSize(int resolution, int levels)
    {
        int n = Math.Max(1, resolution);
        int l = Math.Max(1, levels);
        return (n * n, n * l);
    }

    /// <summary>
    /// Radiance atlas size (octahedral tiles):
    /// W = N*N*S, H = (N*levels)*S
    /// </summary>
    public static (int Width, int Height) GetRadianceAtlasSize(int resolution, int levels, int tileSize)
    {
        int n = Math.Max(1, resolution);
        int l = Math.Max(1, levels);
        int s = Math.Max(1, tileSize);
        return (n * n * s, n * l * s);
    }

    /// <summary>
    /// Computes the tile origin for a probe's octahedral atlas tile.
    /// tileU0 = (x + z*N) * S
    /// tileV0 = (y + L*N) * S
    /// </summary>
    public static (int TileU0, int TileV0) GetRadianceAtlasTileOrigin(
        int storageX,
        int storageY,
        int storageZ,
        int level,
        int resolution,
        int tileSize)
    {
        int n = Math.Max(1, resolution);
        int s = Math.Max(1, tileSize);
        int tileU0 = (storageX + storageZ * n) * s;
        int tileV0 = (storageY + level * n) * s;
        return (tileU0, tileV0);
    }

    #endregion
}
