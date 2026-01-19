using System;

using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.LumOn.WorldProbes;

internal static class LumOnWorldProbeLayout
{
    #region Versioning

    public const int WorldProbeLayoutVersion = 1;

    public const string WorldProbeLayoutTag = "vge.lumon.worldprobes.v1";

    #endregion

    #region Texture Counts

    public const int ShL1TextureCount = 3;

    public const int ShL2TextureCount = 7;

    #endregion

    #region Formats

    public static readonly PixelInternalFormat ShL1Format = PixelInternalFormat.Rgba16f;

    public static readonly PixelInternalFormat VisibilityFormat = PixelInternalFormat.Rgba16f;

    public static readonly PixelInternalFormat DistanceFormat = PixelInternalFormat.Rg16f;

    public static readonly PixelInternalFormat MetaFormat = PixelInternalFormat.Rg32f;

    #endregion
}
