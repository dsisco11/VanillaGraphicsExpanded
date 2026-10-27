using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Borrows the shared depth hierarchy and owns horizon visibility and separate spatial reconstruction targets.</summary>
internal sealed class AmbientOcclusionPass : IDisposable
{
    private GpuResourceCollection resources=new();
    private readonly MinimumChildMaskPass minimumChildren=new();
    private long targetStorageBytes;
    private DynamicTexture2D? raw,filtered,output;
    private StaticGpuFramebuffer? rawTarget,filteredTarget,outputTarget;
    private AmbientOcclusionQuality quality;
    private PostSsaoShaderProgram? horizon;
    private AmbientOcclusionFilterShaderProgram? filter;
    private GraphicsPipeline? horizonPipeline,filterPipeline;
    #region Public API
    /// <summary>Publishes visibility and its receiver depth only after the owner completes submission.</summary>
    internal GpuTexture? Texture=>output;
    /// <summary>Reports allocated texel bytes without including driver metadata.</summary>
    internal long StorageBytes => targetStorageBytes+minimumChildren.StorageBytes;
    /// <summary>Reuses stable resources and prepares all executable contracts outside the command boundary.</summary>
    internal GraphicsPipeline[] Prepare(PostprocessDraw draw,int width,int height,int nativeQuality,
        PostSsaoShaderProgram horizon,AmbientOcclusionFilterShaderProgram filter,MinimumChildMaskShaderProgram masks)
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
        horizonPipeline=draw.Prepare(horizon,rawTarget!.PreservePass);
        filterPipeline=draw.Prepare(filter,filteredTarget!.PreservePass);
        var maskPipeline=minimumChildren.Prepare(draw,width,height,masks);
        return [maskPipeline,horizonPipeline,filterPipeline];
    }
    /// <summary>Samples the shared hierarchy, integrates horizons and reconstructs visibility without feedback or history.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,GpuTexture depth,GpuTexture surface,DynamicTexture2D hierarchy,VgeFrameUniformBuffer camera)
    {
        var frame=new System.Numerics.Vector4(0,0,quality.Divisor,0);
        // Release one quarter of retained cosine-space occlusion per weaker radial observation; this is not a world-space thickness.
        var sampling=new System.Numerics.Vector4(1.25f,0.25f,quality.Directions,quality.Steps);
        var distance=new System.Numerics.Vector4(64,96,0,0);
        minimumChildren.Render(commands,draw,hierarchy);
        horizon!.DepthImage=depth;horizon.SurfaceImage=surface;
        horizon.DepthHierarchy=hierarchy;
        horizon.MinimumChildren=minimumChildren.Texture;
        horizon.Capture(camera,frame,sampling,distance);
        draw.Submit(commands,horizonPipeline!,rawTarget!.PreservePass);
        filter!.DepthImage=depth;filter.SurfaceImage=surface;filter.SourceImage=raw;
        filter.Capture(camera,frame,sampling,distance);
        draw.Submit(commands,filterPipeline!,filteredTarget!.PreservePass);
        filter.SourceImage=filtered;frame.W=1;
        filter.Capture(camera,frame,sampling,distance);
        draw.Submit(commands,filterPipeline!,outputTarget!.PreservePass);
    }
    /// <summary>Withdraws all resources together; no prior-frame image survives retirement.</summary>
    public void Dispose() {
        minimumChildren.Dispose();resources.Dispose();resources=new();
        raw=filtered=output=null;rawTarget=filteredTarget=outputTarget=null;
        horizon=null;filter=null;horizonPipeline=filterPipeline=null;targetStorageBytes=0;
    }
    #endregion
    #region Private
    /// <summary>Owns sized storage and registers framebuffer borrowers in the same retirement collection.</summary>
    private StaticGpuFramebuffer Create(int width,int height,PixelInternalFormat format,string name,out DynamicTexture2D texture) {
        texture=resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Format((OpenTK.Graphics.OpenGL.PixelInternalFormat)(format)), Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = $"AmbientOcclusion.{name}" }).RequireResource());
        targetStorageBytes+=(long)width*height*8;
        var attachment=resources.Own(GpuFramebufferAttachment.FromTexture(texture));
        return resources.Own(StaticGpuFramebuffer.Create([attachment],debugName:$"AmbientOcclusion.{name}"));
    }
    #endregion
}
