using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Borrows the shared depth hierarchy and owns horizon visibility and separate spatial reconstruction targets.</summary>
internal sealed class AmbientOcclusionPass : IDisposable
{
    private GpuResourceCollection resources=new();
    private DynamicTexture2D? raw,filtered,output;
    private GpuFramebuffer? rawTarget,filteredTarget,outputTarget;
    private AmbientOcclusionQuality quality;
    private PostSsaoShaderProgram? horizon;
    private AmbientOcclusionFilterShaderProgram? filter;
    private GraphicsPipeline? horizonPipeline,filterPipeline;
    #region Public API
    /// <summary>Publishes visibility and its receiver depth only after the owner completes submission.</summary>
    internal GpuTexture? Texture=>output;
    /// <summary>Reports allocated texel bytes without including driver metadata.</summary>
    internal long StorageBytes { get; private set; }
    /// <summary>Reuses stable resources and prepares all executable contracts outside the command boundary.</summary>
    internal GraphicsPipeline[] Prepare(PostprocessDraw draw,int width,int height,int nativeQuality,
        PostSsaoShaderProgram horizon,AmbientOcclusionFilterShaderProgram filter)
    {
        quality=AmbientOcclusionQuality.FromNative(nativeQuality);
        if(output is null || output.Width!=width || output.Height!=height) {
            Dispose();
            try {
                int rw=Math.Max(1,(width+quality.Divisor-1)/quality.Divisor),rh=Math.Max(1,(height+quality.Divisor-1)/quality.Divisor);
                rawTarget=Create(rw,rh,PixelInternalFormat.Rgba16f,"Raw",out raw);
                filteredTarget=Create(rw,rh,PixelInternalFormat.Rgba16f,"Filtered",out filtered);
                outputTarget=Create(width,height,PixelInternalFormat.Rgba16f,"Visibility",out output);
            } catch { Dispose();throw; }
        }
        this.horizon=horizon;this.filter=filter;
        if(!horizon.EnsureReady() || !filter.EnsureReady())
            throw new InvalidOperationException("Ambient occlusion shaders are unavailable.");
        horizonPipeline=draw.Prepare(horizon,rawTarget!);
        filterPipeline=draw.Prepare(filter,filteredTarget!);
        return [horizonPipeline,filterPipeline];
    }
    /// <summary>Samples the shared hierarchy, integrates horizons and reconstructs visibility without feedback or history.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,GpuTexture depth,GpuTexture surface,DynamicTexture2D hierarchy,VgeFrameUniformBuffer camera)
    {
        var frame=new System.Numerics.Vector4(0,0,quality.Divisor,0);
        // Release one quarter of retained cosine-space occlusion per weaker radial observation; this is not a world-space thickness.
        var sampling=new System.Numerics.Vector4(1.25f,0.25f,quality.Directions,quality.Steps);
        var distance=new System.Numerics.Vector4(64,96,0,0);
        horizon!.DepthImage=depth;horizon.SurfaceImage=surface;
        horizon.DepthHierarchy=hierarchy;
        horizon.Capture(camera,frame,sampling,distance);
        draw.Submit(commands,horizonPipeline!,rawTarget!);
        filter!.DepthImage=depth;filter.SurfaceImage=surface;filter.SourceImage=raw;
        filter.Capture(camera,frame,sampling,distance);
        draw.Submit(commands,filterPipeline!,filteredTarget!);
        filter.SourceImage=filtered;frame.W=1;
        filter.Capture(camera,frame,sampling,distance);
        draw.Submit(commands,filterPipeline!,outputTarget!);
    }
    /// <summary>Withdraws all resources together; no prior-frame image survives retirement.</summary>
    public void Dispose() {
        resources.Dispose();resources=new();
        raw=filtered=output=null;rawTarget=filteredTarget=outputTarget=null;
        horizon=null;filter=null;horizonPipeline=filterPipeline=null;StorageBytes=0;
    }
    #endregion
    #region Private
    /// <summary>Owns sized storage and registers framebuffer borrowers in the same retirement collection.</summary>
    private GpuFramebuffer Create(int width,int height,PixelInternalFormat format,string name,out DynamicTexture2D texture) {
        texture=resources.Own(DynamicTexture2D.Create(width,height,format,debugName:$"AmbientOcclusion.{name}"));
        StorageBytes+=(long)width*height*8;
        var attachment=resources.Own(GpuFramebufferAttachment.FromTexture(texture));
        return resources.Own(GpuFramebuffer.Create([attachment],debugName:$"AmbientOcclusion.{name}"));
    }
    #endregion
}
