using System;
using System.Collections.Generic;
using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Preserves SSAO, bilateral filtering and FXAA preparation using owned submissions and borrowed engine outputs.</summary>
internal sealed class RetainedPostprocessRenderer : IDisposable
{
    private GpuResourceCollection resources=new(), ssaoResources=new();
    private GpuFramebuffer? lumaTarget,aoTarget,horizontalTarget,verticalTarget;
    private BorrowedTexture? normal,position,revealage,aoImage,horizontalImage,verticalImage;
    private PostLumaShaderProgram? luma;
    private PostSsaoShaderProgram? ssao;
    private PostBilateralShaderProgram? blur;
    private GraphicsPipeline? lumaPipeline,ssaoPipeline,blurPipeline;
    #region Public API
    /// <summary>Prepares exactly the retained operations selected by the engine's current settings.</summary>
    internal IEnumerable<GraphicsPipeline> Prepare(ICoreClientAPI api,PostprocessDraw draw,bool useSsao)
    {
        luma=GpuShaderPrograms.Get<PostLumaShaderProgram>(api,"pbr_post_luma");
        if(luma?.EnsureReady()!=true) throw new InvalidOperationException("Owned luma shader unavailable.");
        lumaTarget??=CreateTarget(api,resources,EnumFrameBuffer.Luma,out _);
        yield return lumaPipeline=draw.Prepare(luma,lumaTarget);
        if(!useSsao) {
            if(normal is not null||ssao is not null) RetireSsao();
            yield break;
        }
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        normal??=ssaoResources.Own(new BorrowedTexture(primary.ColorTextureIds[2]));
        position??=ssaoResources.Own(new BorrowedTexture(primary.ColorTextureIds[3]));
        revealage??=ssaoResources.Own(new BorrowedTexture(api.Render.FrameBuffers[(int)EnumFrameBuffer.Transparent].ColorTextureIds[1]));
        ssao=GpuShaderPrograms.Get<PostSsaoShaderProgram>(api,"pbr_post_ssao");
        blur=GpuShaderPrograms.Get<PostBilateralShaderProgram>(api,"pbr_post_bilateral");
        if(ssao?.EnsureReady()!=true||blur?.EnsureReady()!=true) throw new InvalidOperationException("Owned SSAO shaders unavailable.");
        aoTarget??=CreateTarget(api,ssaoResources,EnumFrameBuffer.SSAO,out aoImage);
        horizontalTarget??=CreateTarget(api,ssaoResources,EnumFrameBuffer.SSAOBlurHorizontal,out horizontalImage);
        verticalTarget??=CreateTarget(api,ssaoResources,EnumFrameBuffer.SSAOBlurVertical,out verticalImage);
        yield return ssaoPipeline=draw.Prepare(ssao,aoTarget);
        yield return blurPipeline=draw.Prepare(blur,horizontalTarget);
    }
    /// <summary>Preserves the receiver kernel and quality-dependent blur count, then prepares unexposed scene RGB for final FXAA.</summary>
    internal void Render(GraphicsCommandContext commands,PostprocessDraw draw,float[] projection,
        EnginePostprocessInputs engine,GpuTexture scene,GpuTexture? depth,(GpuTexture? Texture,float ManualEV) exposure)
    {
        if(engine.Ssao) {
            var ao=aoTarget!;
            ssao!.Normal=normal; ssao.Position=position;
            ssao.Revealage=revealage;
            ssao.Capture(projection,ao.Width,ao.Height,engine.SsaoQuality,engine.Kernel);
            draw.Submit(commands,ssaoPipeline!,ao);
            var horizontal=horizontalTarget!;
            var vertical=verticalTarget!;
            for(int i=0;i<(engine.SsaoQuality==1?1:3);i++) {
                blur!.SourceImage=i==0?aoImage:verticalImage; blur.SecondaryImage=depth;
                blur.Capture(new(1f/horizontal.Width,1f/horizontal.Height,0,0),Vector4.Zero);
                draw.Submit(commands,blurPipeline!,horizontal);
                blur.SourceImage=horizontalImage;
                blur.Capture(new(1f/horizontal.Width,1f/horizontal.Height,1,0),Vector4.Zero);
                draw.Submit(commands,blurPipeline!,vertical);
            }
        }
        luma!.SourceImage=scene;
        luma.SecondaryImage=exposure.Texture??scene;
        luma.Capture(new(0,0,engine.Fxaa?1:0,exposure.Texture is not null?1:0),new(0,0,0,exposure.ManualEV));
        draw.Submit(commands,lumaPipeline!,lumaTarget!);
    }
    /// <summary>Releases private framebuffers and borrowed wrappers while preserving engine-owned image storage.</summary>
    public void Dispose() { resources.Dispose(); resources=new(); lumaTarget=null; luma=null; lumaPipeline=null; RetireSsao(); }
    #endregion
    #region Private
    /// <summary>Borrows the engine image once per publication without replacing its framebuffer table entry.</summary>
    private static GpuFramebuffer CreateTarget(ICoreClientAPI api,GpuResourceCollection resources,EnumFrameBuffer kind,out BorrowedTexture image)
    {
        image=resources.Own(new BorrowedTexture(api.Render.FrameBuffers[(int)kind].ColorTextureIds[0]));
        var attachment=resources.Own(GpuFramebufferAttachment.FromTexture(image));
        return resources.Own(GpuFramebuffer.Create([attachment],debugName:$"Postprocess.{kind}"));
    }
    /// <summary>Retires optional SSAO framebuffers before their borrowed inputs when disabled or republished.</summary>
    private void RetireSsao()
    {
        ssaoResources.Dispose(); ssaoResources=new();
        aoTarget=horizontalTarget=verticalTarget=null;
        normal=position=revealage=aoImage=horizontalImage=verticalImage=null;
        ssao=null; blur=null; ssaoPipeline=blurPipeline=null;
    }
    #endregion
}
