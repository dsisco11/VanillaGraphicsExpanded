using System;
using OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes optional sparse-image residency for storage configured by explicit native integration.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public void CommitTexturePages(GpuTextureRef texture, in RHITextureRegion region, bool commit)
    {
        RequireContext();
        if (!Capabilities.SparseTextures) throw new NotSupportedException("Sparse textures are unavailable.");
        var metadata = texture.RequireMetadata();
        texture.Resource!.RequireOriginalContext();
        ValidateTextureRegion(metadata, region);
        cache.RequireResourceOperationAvailable(GpuResourceKind.Texture, texture.Resource.ResourceId);
        // Sparse allocation/page shape is native integration policy, absent from canonical metadata.
        // Preserve its native rejection behavior rather than inventing facts or querying on every call.
        using var selection = cache.BindTextureScope(metadata.Target, cache.GetActiveTextureUnit(), (int)texture.Resource.ResourceId);
        GL.Arb.TexPageCommitment((All)metadata.Target, region.Subresource.MipLevel,
            region.X, region.Y, checked(region.Subresource.Layer + region.Z), region.Width, region.Height, region.Depth, commit);
        RequireContext();
    }
    #endregion
}
