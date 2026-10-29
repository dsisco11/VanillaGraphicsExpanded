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
        _ = texture.RequireMetadata();
        texture.Resource!.RequireOriginalContext();
        ulong handle;
        if (sampler.IsEmpty) handle = unchecked((ulong)GL.Arb.GetTextureHandle(texture.TextureId));
        else
        {
            var owner = sampler.RequireResource();
            _ = RHIResourceManager.RequireSamplerReferenceMetadata(owner);
            handle = unchecked((ulong)GL.Arb.GetTextureSamplerHandle(texture.TextureId, checked((int)owner.ResourceId)));
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
