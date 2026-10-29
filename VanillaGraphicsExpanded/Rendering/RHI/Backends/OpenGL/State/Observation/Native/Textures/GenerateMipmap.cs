using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Publishes equivalent native entrypoint effects after driver dispatch.</summary>
internal sealed unsafe partial class OpenGlStateCache
{
    #region Internal API
    /// <summary>Updates existing cache and resource authority without issuing the intercepted operation.</summary>
    [InterceptOpenGL("glGenerateMipmap")]
    [InterceptOpenGL("glGenerateMipmapEXT")]
    internal static void ObserveGenerateMipmap(int a0)
    {
        GenerateMipmapTarget @target = unchecked((GenerateMipmapTarget)a0);
        if (!OpenGLObservationContext.TryGet(out _, out _, false)) return;
        OpenGlStateCache.ObserveTextureMetadata(0, unchecked((int)@target), -1, false, false, OpenGlStateCache.TextureMetadataEffect.MipGeneration, 0);
    }
    #endregion
}
