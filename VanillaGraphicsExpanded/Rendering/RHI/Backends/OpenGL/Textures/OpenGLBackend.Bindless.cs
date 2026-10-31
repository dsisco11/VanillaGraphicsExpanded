using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
/// <summary>Owns optional bindless handle issuance and residency commands.</summary>
internal sealed partial class OpenGLBackend
{
    #region Public API
    /// <inheritdoc />
    public ulong CreateTextureHandle(GpuTextureRef texture, GpuSamplerRef sampler)
    {
        RequireContext();
        if (!Capabilities.BindlessTextures) throw new NotSupportedException("Bindless textures are unavailable.");
        var image = SelectTexture(texture);
        image.RequireOriginalContext();
        ulong handle;
        if (sampler.IsEmpty) handle = unchecked((ulong)GL.Arb.GetTextureHandle(checked((int)image.ResourceId)));
        else
        {
            var owner = sampler.RequireResource();
            owner.RequireOriginalContext();
#if DEBUG
            _ = RHIResourceManager.RequireSamplerReferenceMetadata(owner);
#endif
            if (!owner.HasMetadataSnapshot) throw new InvalidOperationException("Sampler preparation is unavailable.");
            handle = unchecked((ulong)GL.Arb.GetTextureSamplerHandle(checked((int)image.ResourceId), checked((int)owner.ResourceId)));
        }
        RequireContext();
        if (handle == 0) throw new InvalidOperationException("Bindless handle issuance returned zero.");
        return handle;
    }
    /// <inheritdoc />
    public void SetTextureHandleResidency(ulong handle, bool resident)
    {
        RequireContext();
        if (!Capabilities.BindlessTextures) throw new NotSupportedException("Bindless textures are unavailable.");
        if (resident) GL.Arb.MakeTextureHandleResident(handle);
        else GL.Arb.MakeTextureHandleNonResident(handle);
        RequireContext();
    }
    #endregion
}
