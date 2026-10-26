using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Reads renderbuffer texture associations without issuing unsupported texture image queries.</summary>
internal static class GpuRenderbufferTextureMetadataReader
{
    #region Internal API
    /// <summary>Captures the backing name; renderbuffer textures have no mip levels or texture parameters.</summary>
    internal static bool TryRead(int texture, out GpuTextureMetadata metadata) => RHI.RHICommandContext.Current.OpenGL.ReadExternalRenderbufferTexture(texture, out metadata);
    #endregion
}
