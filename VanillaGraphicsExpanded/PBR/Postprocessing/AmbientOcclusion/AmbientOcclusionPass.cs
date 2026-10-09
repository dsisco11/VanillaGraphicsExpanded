using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns a bounded depth hierarchy, horizon visibility and separate spatial reconstruction targets.</summary>
internal sealed class AmbientOcclusionPass : IDisposable
{
    private GpuResourceCollection resources=new();
    private readonly DynamicTexture2D?[] depths=new DynamicTexture2D?[3];
    private readonly GpuFramebuffer?[] depthTargets=new GpuFramebuffer?[3];
    private DynamicTexture2D? raw,filtered,output;
    private GpuFramebuffer? rawTarget,filteredTarget,outputTarget;
    private AmbientOcclusionQuality quality;
    private PostSsaoShaderProgram? horizon;
    private AmbientOcclusionDepthShaderProgram? reduction;
    private AmbientOcclusionFilterShaderProgram? filter;
    private GraphicsPipeline? depthPipeline,horizonPipeline,filterPipeline;
    #region Public API
    /// <summary>Publishes visibility and its receiver depth only after the owner completes submission.</summary>
    internal GpuTexture? Texture=>output;
    /// <summary>Reports allocated texel bytes without including driver metadata.</summary>
    internal long StorageBytes { get; private set; }
    /// <summary>Reuses stable resources and prepares all executable contracts outside the command boundary.</summary>
    internal GraphicsPipeline[] Prepare(PostprocessDraw draw,int width,int height,int nativeQuality,
        PostSsaoShaderProgram horizon,AmbientOcclusionDepthShaderProgram reduction,AmbientOcclusionFilterShaderProgram filter)
    {
        quality=AmbientOcclusionQuality.FromNative(nativeQuality);
        if(output is null || output.Width!=width || output.Height!=height) {
            Dispose();
            try {
                int w=width,h=height;
                for(int i=0;i<3;i++) {
                    w=Math.Max(1,(w+1)/2);h=Math.Max(1,(h+1)/2);
                    depthTargets[i]=Create(w,h,PixelInternalFormat.Rg32f,$"Depth{i}",out var texture);
                    depths[i]=texture;
                }
                int rw=Math.Max(1,(width+quality.Divisor-1)/quality.Divisor),rh=Math.Max(1,(height+quality.Divisor-1)/quality.Divisor);
                rawTarget=Create(rw,rh,PixelInternalFormat.Rgba16f,"Raw",out raw);
                filteredTarget=Create(rw,rh,PixelInternalFormat.Rgba16f,"Filtered",out filtered);
                outputTarget=Create(width,height,PixelInternalFormat.Rgba16f,"Visibility",out output);
            } catch { Dispose();throw; }
        }
        this.horizon=horizon;this.reduction=reduction;this.filter=filter;
        if(!horizon.EnsureReady() || !reduction.EnsureReady() || !filter.EnsureReady())
            throw new InvalidOperationException("Ambient occlusion shaders are unavailable.");
        depthPipeline=draw.Prepare(reduction,depthTargets[0]!);
        horizonPipeline=draw.Prepare(horizon,rawTarget!);
        filterPipeline=draw.Prepare(filter,filteredTarget!);
        return [depthPipeline,horizonPipeline,filterPipeline];
    }
    /// <summary>Builds separate depth images, integrates horizons and reconstructs visibility without feedback or history.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,GpuTexture depth,GpuTexture surface,float[] inverseProjection,float[] view)
    {
        for(int i=0;i<3;i++) {
            reduction!.SourceImage=i==0?depth:depths[i-1];
            reduction.Capture(i!=0);
            draw.Submit(commands,depthPipeline!,depthTargets[i]!);
        }
        var frame=new System.Numerics.Vector4(output!.Width,output.Height,quality.Divisor,0);
        var sampling=new System.Numerics.Vector4(1.25f,0.25f,quality.Directions,quality.Steps);
        var distance=new System.Numerics.Vector4(64,96,0,0);
        horizon!.DepthImage=depth;horizon.SurfaceImage=surface;
        horizon.DepthHalf=depths[0];horizon.DepthQuarter=depths[1];horizon.DepthEighth=depths[2];
        horizon.Capture(inverseProjection,view,frame,sampling,distance);
        draw.Submit(commands,horizonPipeline!,rawTarget!);
        filter!.DepthImage=depth;filter.SurfaceImage=surface;filter.SourceImage=raw;
        filter.Capture(inverseProjection,view,frame,sampling,distance);
        draw.Submit(commands,filterPipeline!,filteredTarget!);
        filter.SourceImage=filtered;frame.W=1;
        filter.Capture(inverseProjection,view,frame,sampling,distance);
        draw.Submit(commands,filterPipeline!,outputTarget!);
    }
    /// <summary>Withdraws all resources together; no prior-frame image survives retirement.</summary>
    public void Dispose() {
        resources.Dispose();resources=new();
        Array.Clear(depths);Array.Clear(depthTargets);
        raw=filtered=output=null;rawTarget=filteredTarget=outputTarget=null;
        horizon=null;reduction=null;filter=null;depthPipeline=horizonPipeline=filterPipeline=null;StorageBytes=0;
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
