using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns scene luminance preparation and the explicitly neutral ambient occlusion placeholder.</summary>
internal sealed class RetainedPostprocessRenderer : IDisposable
{
    private GpuResourceCollection resources=new(),aoResources=new();
    private GpuFramebuffer? lumaTarget,aoTarget;
    private DynamicTexture2D? lumaImage,aoImage;
    private PostLumaShaderProgram? luma;
    private GraphicsPipeline? lumaPipeline,aoPipeline;
    #region Public API
    /// <summary>Publishes unexposed scene color and its perceptual antialiasing metric.</summary>
    internal GpuTexture Luma=>lumaImage!;
    /// <summary>Publishes neutral visibility when native AO is selected.</summary>
    internal GpuTexture? Occlusion=>aoImage;
    /// <summary>Prepares owned targets outside submission; no temporary occlusion algorithm or blur chain is allocated.</summary>
    internal IEnumerable<GraphicsPipeline> Prepare(ICoreClientAPI api,PostprocessDraw draw,bool useSsao)
    {
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        luma=GpuShaderPrograms.Get<PostLumaShaderProgram>(api,"pbr_post_luma");
        if(luma?.EnsureReady()!=true) throw new InvalidOperationException("Owned luminance shader unavailable.");
        lumaTarget??=CreateTarget(resources,primary.Width,primary.Height,"Luma",out lumaImage);
        yield return lumaPipeline=draw.Prepare(luma,lumaTarget);
        if(!useSsao)
        {
            if(aoTarget is not null) RetireOcclusion();
            yield break;
        }
        var placeholder=GpuShaderPrograms.Get<PostSsaoShaderProgram>(api,"pbr_post_ssao");
        if(placeholder?.EnsureReady()!=true) throw new InvalidOperationException("Neutral AO shader unavailable.");
        aoTarget??=CreateTarget(aoResources,1,1,"NeutralAO",out aoImage);
        yield return aoPipeline=draw.Prepare(placeholder,aoTarget);
    }
    /// <summary>Publishes neutral AO and preserves scene RGB while preparing an exposure-aware luminance metric.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,EnginePostprocessInputs engine,
        GpuTexture scene,(GpuTexture? Texture,float ManualEV) exposure)
    {
        if(engine.Ssao) draw.Submit(commands,aoPipeline!,aoTarget!);
        luma!.SourceImage=scene;
        luma.SecondaryImage=exposure.Texture??scene;
        luma.Capture(new(0,0,engine.Fxaa?1:0,exposure.Texture is not null?1:0),new(0,0,0,exposure.ManualEV));
        draw.Submit(commands,lumaPipeline!,lumaTarget!);
    }
    /// <summary>Retires owned targets and clears publication before a new resource lifetime.</summary>
    public void Dispose()
    {
        resources.Dispose(); resources=new(); lumaTarget=null; lumaImage=null;
        luma=null; lumaPipeline=null; RetireOcclusion();
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
    /// <summary>Releases the placeholder when disabled or when the screen lifetime ends.</summary>
    private void RetireOcclusion()
    {
        aoResources.Dispose(); aoResources=new(); aoTarget=null; aoImage=null; aoPipeline=null;
    }
    #endregion
}
