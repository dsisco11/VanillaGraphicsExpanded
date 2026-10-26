using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Acquires only explicitly changed external image facts through the backend.</summary>
internal static class GpuTextureImageReader
{
    #region Internal API
    /// <summary>Reads a selected external level/face without rediscovering unrelated storage.</summary>
    internal static bool TryRead(int texture, TextureTarget target, TextureTarget face, int level, out GpuTextureLevelMetadata image) => RHI.RHICommandContext.Current.OpenGL.ReadExternalTextureImage(texture, target, face, level, out image);
    #endregion
}
