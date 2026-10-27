using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires only explicitly changed external image facts through the backend.</summary>
internal static class GpuTextureImageReader
{
    #region Internal API
    /// <summary>Reads a selected external level/face without rediscovering unrelated storage.</summary>
    internal static bool TryRead(int texture, TextureDimension target, CubeFace face, int level, out GpuTextureLevelMetadata image) => RHI.RHICommandContext.Current.ReadExternalTextureImage(texture, target, face, level, out image);
    #endregion
}
