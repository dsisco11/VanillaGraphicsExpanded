using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Owns six square color texture faces with configurable format, filtering and mip storage.</summary>
/// <remarks>Mip allocation does not populate or generate mip contents. Renderers own content generation
/// and can attach individual faces through <see cref="GpuFramebufferAttachment.FromTexture(GpuTextureRef, int, int?, CubeFace?)"/>.</remarks>
public sealed class DynamicTextureCube : GpuTexture
{
    #region Public API
    /// <summary>Returns the number of allocated mip levels, independent of the active sampling range.</summary>
    public int MipLevels => StorageMipLevels;


    #endregion

    /// <summary>Declares an unpublished cube owner for typed creation.</summary>
    internal DynamicTextureCube() { }

    #region Private

    #endregion
}
