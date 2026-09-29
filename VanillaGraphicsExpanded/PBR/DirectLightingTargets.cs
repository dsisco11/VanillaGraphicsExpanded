using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Owns the three linear radiance targets and their non-owning MRT framebuffer.</summary>
internal sealed class DirectLightingTargets : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    public DynamicTexture2D DirectDiffuse { get; }
    public DynamicTexture2D DirectSpecular { get; }
    public DynamicTexture2D Emissive { get; }
    public GpuFramebuffer? Framebuffer { get; }
    public bool IsValid => Framebuffer is { IsValid: true } && DirectDiffuse.IsValid
        && DirectSpecular.IsValid && Emissive.IsValid;

    #region Allocation
    /// <summary>Allocates screen-space radiance with linear sampling and reclaims partial allocations on failure.</summary>
    public DirectLightingTargets(int width, int height)
    {
        int previous = GpuFramebuffer.SaveBinding();
        try
        {
            DirectDiffuse = resources.Own(DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "DirectDiffuse"));
            DirectSpecular = resources.Own(DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "DirectSpecular"));
            Emissive = resources.Own(DynamicTexture2D.Create(width, height, PixelInternalFormat.Rgba16f, TextureFilterMode.Linear, debugName: "Emissive"));
            if (!DirectDiffuse.IsValid || !DirectSpecular.IsValid || !Emissive.IsValid) return;
            var framebuffer = GpuFramebuffer.CreateMRT([DirectDiffuse, DirectSpecular, Emissive],
                depthTexture: null, ownsTextures: false, debugName: "DirectLightingFBO");
            if (framebuffer is null) return;
            Framebuffer = resources.Own(framebuffer);
            // The factory logs incomplete targets; this owner also rejects them for publication.
            Framebuffer.Bind();
            if (GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) != FramebufferErrorCode.FramebufferComplete)
                resources.Dispose();
        }
        catch { resources.Dispose(); throw; }
        finally { GpuFramebuffer.RestoreBinding(previous); }
    }

    /// <summary>Resizes storage in place and validates MRT completeness while preserving the caller's framebuffer.</summary>
    public bool Resize(int width, int height)
    {
        int previous = GpuFramebuffer.SaveBinding();
        try
        {
            Framebuffer!.Resize(width, height);
            Framebuffer.Bind();
            return GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer) == FramebufferErrorCode.FramebufferComplete;
        }
        finally { GpuFramebuffer.RestoreBinding(previous); }
    }
    #endregion

    #region Lifetime
    /// <summary>Releases the framebuffer before its owned radiance textures.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
