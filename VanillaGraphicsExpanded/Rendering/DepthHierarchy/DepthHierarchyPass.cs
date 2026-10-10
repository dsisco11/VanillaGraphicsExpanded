using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering.Pipeline;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns one conservative hardware-depth mip chain and persistent targets for its generation.</summary>
internal sealed class DepthHierarchyPass : IDisposable
{
    private GpuResourceCollection resources=new();
    private GpuFramebuffer[] targets=[];
    private DepthHierarchyCopyShaderProgram? copy;
    private DepthHierarchyDownsampleShaderProgram? reduce;
    private GraphicsPipeline? copyPipeline,reducePipeline;
    #region Public API
    /// <summary>Gets the owned hierarchy; consumers borrow this instance.</summary>
    internal DynamicTexture2D? Texture {get;private set;}
    /// <summary>Gets logical payload bytes across every allocated mip.</summary>
    internal long StorageBytes {get;private set;}
    /// <summary>Allocates once per extent and prepares executable contracts outside submission.</summary>
    internal GraphicsPipeline[] Prepare(PostprocessDraw draw,int width,int height,DepthHierarchyCopyShaderProgram copy,DepthHierarchyDownsampleShaderProgram reduce) {
        if(Texture is null || Texture.Width!=width || Texture.Height!=height) {
            Dispose();
            try {
                int levels=1;for(int extent=Math.Max(width,height);extent>1;extent>>=1) levels++;
                Texture=resources.Own(DynamicTexture2D.CreateMipmapped(width,height,PixelInternalFormat.R32f,levels,"DepthHierarchy"));
                targets=new GpuFramebuffer[levels];
                for(int level=0;level<levels;level++) {
                    var attachment=resources.Own(GpuFramebufferAttachment.FromTexture(Texture,mipLevel:level));
                    targets[level]=resources.Own(GpuFramebuffer.Create([attachment],debugName:$"DepthHierarchy.Mip{level}"));
                    StorageBytes+=4L*Math.Max(1,width>>level)*Math.Max(1,height>>level);
                }
            } catch {Dispose();throw;}
        }
        this.copy=copy;this.reduce=reduce;
        if(!copy.EnsureReady() || !reduce.EnsureReady()) throw new InvalidOperationException("Depth hierarchy shaders unavailable.");
        copyPipeline=draw.Prepare(copy,targets[0]);
        reducePipeline=draw.Prepare(reduce,targets[0]);
        return [copyPipeline,reducePipeline];
    }
    /// <summary>Generates all mips, excluding the attached destination from the sampled texture's accessible levels.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,GpuTexture depth) {
        var texture=Texture??throw new InvalidOperationException("Depth hierarchy is not prepared.");
        copy!.PrimaryDepth=depth;
        draw.Submit(commands,copyPipeline!,targets[0]);
        try {
            reduce!.HzbDepth=texture;reduce.SrcMip=0;
            for(int level=1;level<texture.MipLevels;level++) {
                // Texel-fetch LOD zero is relative to BASE_LEVEL. Only the completed source mip is accessible.
                texture.SetMipRange(level-1,level-1);
                draw.Submit(commands,reducePipeline!,targets[level]);
            }
        } finally {texture.SetMipRange(0,texture.MipLevels-1);}
    }
    /// <summary>Retires framebuffer borrowers before their shared storage.</summary>
    public void Dispose() {resources.Dispose();resources=new();targets=[];Texture=null;StorageBytes=0;copy=null;reduce=null;copyPipeline=reducePipeline=null;}
    #endregion
}
