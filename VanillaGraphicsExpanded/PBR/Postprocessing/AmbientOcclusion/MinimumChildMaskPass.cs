using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns optional R16UI membership for the four hierarchy levels searched by AO.</summary>
internal sealed class MinimumChildMaskPass : IDisposable
{
    private GpuResourceCollection resources = new();
    private StaticGpuFramebuffer[] targets = [];
    private MinimumChildMaskShaderProgram? shader;
    private GraphicsPipeline? pipeline;
    #region Public API
    /// <summary>Gets membership storage; mip zero corresponds to shared hierarchy level one.</summary>
    internal DynamicTexture2D? Texture { get; private set; }
    /// <summary>Gets allocated R16UI texel payload, excluding driver padding.</summary>
    internal long StorageBytes { get; private set; }
    /// <summary>Checks context limits and reuses storage and PSOs until extent or executable changes.</summary>
    internal GraphicsPipeline Prepare(PostprocessDraw draw,int width,int height,MinimumChildMaskShaderProgram shader)
    {
        if(width<1 || height<1 || width>GpuSupport.MaxTextureSize || height>GpuSupport.MaxTextureSize)
            throw new ArgumentOutOfRangeException(nameof(width),"AO mask extent exceeds context limits.");
        if(GpuSupport.MaxTextureImageUnits<4 || GpuSupport.MaxCombinedTextureImageUnits<4
            || GpuSupport.MaxColorAttachments<1 || GpuSupport.MaxDrawBuffers<1)
            throw new InvalidOperationException("Masked AO requires four sampler units and one integer color target.");
        int levels=Math.Max(1,Math.Min(4,(int)Math.Floor(Math.Log2(Math.Max(width,height)))));
        int mw=Math.Max(1,width>>1),mh=Math.Max(1,height>>1);
        if(Texture is null || Texture.Width!=mw || Texture.Height!=mh || targets.Length!=levels)
        {
            Dispose();
            try
            {
                Texture=resources.Own(DynamicTexture2D.CreateMipmapped(mw,mh,PixelInternalFormat.R16ui,Math.Max(1,levels),"AmbientOcclusion.MinimumChildren"));
                targets=new StaticGpuFramebuffer[Math.Max(1,levels)];
                for(int level=0;level<targets.Length;level++)
                {
                    var attachment=resources.Own(GpuFramebufferAttachment.FromTexture(Texture,mipLevel:level));
                    targets[level]=resources.Own(StaticGpuFramebuffer.Create([attachment],debugName:$"AmbientOcclusion.MinimumChildren.Mip{level}"));
                    StorageBytes+=2L*Math.Max(1,mw>>level)*Math.Max(1,mh>>level);
                }
            }
            catch { Dispose();throw; }
        }
        this.shader=shader;
        if(!shader.EnsureReady()) throw new InvalidOperationException("AO minimum-child shader is unavailable.");
        pipeline=draw.Prepare(shader,targets[0].PreservePass);
        return pipeline;
    }
    /// <summary>Builds all used levels before horizon sampling, preserving conservative odd-axis child membership.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,DynamicTexture2D hierarchy)
    {
        var program=shader??throw new InvalidOperationException("AO masks are not prepared.");
        program.Hierarchy=hierarchy;
        int levels=Math.Min(4,hierarchy.MipLevels-1);
        for(int level=1;level<=levels;level++)
        {
            program.Capture(level);
            draw.Submit(commands,pipeline!,targets[level-1].PreservePass);
        }
        // Completed integer color writes feed the subsequent horizon sampler in the same boundary.
        GL.MemoryBarrier(MemoryBarrierFlags.FramebufferBarrierBit|MemoryBarrierFlags.TextureFetchBarrierBit);
    }
    /// <summary>Retires borrowers before sidecar storage on disable, resize, reload or world teardown.</summary>
    public void Dispose()
    {
        resources.Dispose();resources=new();targets=[];Texture=null;StorageBytes=0;shader=null;pipeline=null;
    }
    #endregion
}
