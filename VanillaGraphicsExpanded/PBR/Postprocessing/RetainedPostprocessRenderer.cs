using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns scene luminance preparation for final display antialiasing.</summary>
internal sealed class RetainedPostprocessRenderer : IDisposable
{
    private GpuResourceCollection resources=new();
    private GpuFramebuffer? lumaTarget;
    private DynamicTexture2D? lumaImage;
    private PostLumaShaderProgram? luma;
    private GraphicsPipeline? lumaPipeline;
    #region Public API
    /// <summary>Publishes unexposed scene color and its perceptual antialiasing metric.</summary>
    internal GpuTexture Luma=>lumaImage!;
    /// <summary>Prepares the owned scene target outside submission.</summary>
    internal IEnumerable<GraphicsPipeline> Prepare(ICoreClientAPI api,PostprocessDraw draw)
    {
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        luma=GpuShaderPrograms.Get<PostLumaShaderProgram>(api,"pbr_post_luma");
        if(luma?.EnsureReady()!=true) throw new InvalidOperationException("Owned luminance shader unavailable.");
        lumaTarget??=CreateTarget(resources,primary.Width,primary.Height,"Luma",out lumaImage);
        yield return lumaPipeline=draw.Prepare(luma,lumaTarget);

    }
    /// <summary>Preserves scene RGB while preparing an exposure-aware luminance metric.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,EnginePostprocessInputs engine,
        GpuTexture scene,(GpuTexture? Texture,float ManualEV) exposure)
    {
        luma!.SourceImage=scene;
        luma.SecondaryImage=exposure.Texture??scene;
        luma.Capture(new(0,0,engine.Fxaa?1:0,exposure.Texture is not null?1:0),new(0,0,0,exposure.ManualEV));
        draw.Submit(commands,lumaPipeline!,lumaTarget!);
    }
    /// <summary>Retires owned targets and clears publication before a new resource lifetime.</summary>
    public void Dispose()
    {
        resources.Dispose(); resources=new(); lumaTarget=null; lumaImage=null;
        luma=null; lumaPipeline=null;
    }
    #endregion
    #region Private
    /// <summary>Allocates sized storage and registers its image and framebuffer with the lifetime owner.</summary>
    private static GpuFramebuffer CreateTarget(GpuResourceCollection collection,int width,int height,string name,out DynamicTexture2D image)
    {
        image=collection.Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.Rgba16f,debugName:$"Postprocess.{name}"));
        var attachment=collection.Own(GpuFramebufferAttachment.FromTexture(image));
        return collection.Own(GpuFramebuffer.Create([attachment],debugName:$"Postprocess.{name}"));
    }
    #endregion
}
