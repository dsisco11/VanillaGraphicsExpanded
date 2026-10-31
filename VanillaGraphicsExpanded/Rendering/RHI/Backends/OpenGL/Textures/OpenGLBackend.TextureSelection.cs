using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Selects producer-delivered texture facts for bounded resource operations.</summary>
internal sealed partial class OpenGLBackend
{
    #region Private
    /// <summary>Uses the supplied snapshot once; original-association misuse is diagnosed only in development.</summary>
    private static GpuResource<GpuTextureMetadata> SelectTexture(GpuTextureRef texture)
    {
        var owner = texture.Resource ?? throw new InvalidOperationException("A texture is required.");
#if DEBUG
        _ = RHIResourceManager.RequireTextureReferenceMetadata(owner);
#endif
        if (owner.IsDisposed || owner.ResourceId == 0 || !owner.HasMetadataSnapshot)
            throw new InvalidOperationException("Texture storage preparation is unavailable.");
        return owner;
    }
    #endregion
}
