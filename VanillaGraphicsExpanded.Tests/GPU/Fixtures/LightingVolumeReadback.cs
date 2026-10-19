using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Reuses a compact framebuffer/PBO pair for exact candidate node diagnostics.</summary>
internal sealed class LightingVolumeReadback : IDisposable
{
    private readonly DynamicGpuFramebuffer framebuffer;
    private readonly GpuPixelPackBuffer staging;
    #region Public API
    /// <summary>Acquires one reusable 16-byte readback path without raw GL test calls.</summary>
    internal LightingVolumeReadback()
    {
        framebuffer=DynamicGpuFramebuffer.CreateEmpty("Tests.Cloud.LightingVolume.Readback");
        GpuPixelPackBuffer? acquired=null;
        try {acquired=GpuPixelPackBuffer.Create();acquired.AllocateOrphan(16);staging=acquired;}
        catch {RenderingCleanup.Run(()=>acquired?.Dispose(),framebuffer.Dispose);throw;}
    }
    /// <summary>Reads one texel, preserving inherited framebuffer state and borrowing the layer attachment.</summary>
    internal Vector4 Node(GpuTexture texture,int x,int y,int z)
    {
        GpuComputePipeline.MemoryBarrier(MemoryBarrierFlags.FramebufferBarrierBit);
        using var draw=StateCache.Current.BindFramebufferScope(FramebufferTarget.DrawFramebuffer);
        using var read=StateCache.Current.BindFramebufferScope(FramebufferTarget.ReadFramebuffer);
        using var image=GpuFramebufferAttachment.FromTexture(texture,layer:z);
        framebuffer.SetAttachment(FramebufferAttachment.ColorAttachment0,image);framebuffer.Bind();
        staging.ReadPixels(x,y,1,1,PixelFormat.Rgba,PixelType.Float,0);
        using var pixels=staging.MapRange<float>(0,4,MapBufferAccessMask.MapReadBit);
        var result=new Vector4(pixels.Span[0],pixels.Span[1],pixels.Span[2],pixels.Span[3]);
        framebuffer.RemoveAttachment(FramebufferAttachment.ColorAttachment0);return result;
    }
    /// <summary>Releases owned readback objects while sampled volume textures remain with the caller.</summary>
    public void Dispose()=>RenderingCleanup.Run(framebuffer.Dispose,staging.Dispose);
    #endregion
}
