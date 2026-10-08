using System;
using System.Collections.Generic;
using System.Linq;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns scene postprocessing order and atomic effect publication independently of engine shader passes.</summary>
internal sealed class PostprocessPipeline : IRenderer
{
    private static PostprocessPipeline? active;
    private readonly ICoreClientAPI api;
    private readonly Action unregisterResize;
    private readonly BloomRenderer bloom=new();
    private readonly GodRayRenderer rays=new();
    private readonly RetainedPostprocessRenderer retained=new();
    private GpuResourceCollection resources=new();
    private BorrowedTexture? scene,glow,depth;
    private PostprocessDraw? draw;
    private PostprocessImage? neutral;
    private PostprocessParameters settings;
    private bool captured,published,disposed;
    private GpuTexture? bloomTexture,rayTexture;
    #region Public API
    #region Lifetime
    /// <summary>Registers frame publication and attachment lifecycle without replacing final presentation.</summary>
    internal PostprocessPipeline(ICoreClientAPI api)
    {
        this.api=api;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_postprocess");
        api.Event.LeaveWorld+=Retire; api.Event.ReloadShader+=Reload; active=this;
    }
    /// <summary>Captures settings after scene and camera preparation.</summary>
    public double RenderOrder=>1002;
    /// <summary>Runs independently of world view distance.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Withdraws publication and captures one coherent settings snapshot for the coming scene.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        if(disposed||stage!=EnumRenderStage.Before) return;
        published=false; captured=true; settings=ConfigModSystem.Config.Postprocessing.Snapshot();
    }
    /// <summary>Unregisters all callbacks before retiring owned resources.</summary>
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        if(ReferenceEquals(active,this)) active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before); api.Event.LeaveWorld-=Retire; api.Event.ReloadShader-=Reload;
        unregisterResize(); Retire();
    }
    #endregion
    #region Engine handoff
    /// <summary>Replaces HDR scene postprocessing, leaving non-scene menu rendering at its existing display boundary.</summary>
    internal static bool ReplaceEnginePass(ClientPlatformWindows platform,float[]? projection)
    {
        if(!SceneColorPipeline.HasSceneInput) return false;
        var owner=active??throw new InvalidOperationException("VGE postprocessing owner is unavailable.");
        owner.Render(projection??throw new InvalidOperationException("Scene postprocessing requires a projection."),EnginePostprocessInputs.Capture(platform,owner.api));
        // Preserve the installed method's documented exit target/state for the following overlay stage.
        var primary=owner.api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        platform.LoadFrameBuffer(EnumFrameBuffer.Primary);
        platform.GlViewport(0,0,primary.Width,primary.Height);
        platform.GlToggleBlend(true,EnumBlendMode.Standard);
        return true;
    }
    /// <summary>Binds the published owned bloom image at the existing final composition sampler.</summary>
    internal static void BindBloom(ShaderProgramFinal program,int legacyTexture)
        => program.BloomParts2D=SceneColorPipeline.HasSceneInput?Published().bloomTexture!.TextureId:legacyTexture;
    /// <summary>Binds the published owned solar-shaft image at the existing final composition sampler.</summary>
    internal static void BindGodRays(ShaderProgramFinal program,int legacyTexture)
        => program.GodrayParts2D=SceneColorPipeline.HasSceneInput?Published().rayTexture!.TextureId:legacyTexture;
    /// <summary>Executes owned algorithms and retained SSAO/luma responsibilities, publishing only after complete success.</summary>
    internal void Render(float[] projection,EnginePostprocessInputs engine)
    {
        if(published) return;
        if(!captured) throw new InvalidOperationException("VGE postprocessing frame was not captured.");
        CameraExposureRenderer.MeterScene();
        var exposure=CameraExposureRenderer.DisplayExposure();
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        draw??=new(); neutral??=new(1,1,"Postprocess.Neutral",clear:true);
        bool useBloom=engine.Bloom&&settings.BloomStrength>0;
        bool useRays=engine.GodRays&&settings.GodRayStrength>0&&settings.GodRayLimit>0;
        var pipelines=new List<GraphicsPipeline>();
        if(useBloom) pipelines.Add(bloom.Prepare(api,draw,primary.Width,primary.Height,settings.BloomLevels)); else bloom.Dispose();
        if(useRays) pipelines.Add(rays.Prepare(api,draw,primary.Width,primary.Height)); else rays.Dispose();
        // Capture typed inputs once for this framebuffer publication, before entering a pass.
        scene??=resources.Own(new BorrowedTexture(primary.ColorTextureIds[0]));
        if(useRays||engine.Ssao) depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        if(useRays) glow??=resources.Own(new BorrowedTexture(primary.ColorTextureIds[1]));
        pipelines.AddRange(retained.Prepare(api,draw,engine.Ssao));
        if(!GraphicsCommandContext.TryRun("Postprocess.Scene",pipelines,true,commands=>{
            // Metering precedes generated glare; glare cannot feed back into the camera.
            if(useBloom) bloom.Render(commands,draw,scene,exposure,settings);
            if(useRays) rays.Render(commands,draw,api,projection,glow!,depth!,settings);
            retained.Render(commands,draw,projection,engine,scene,depth,exposure);
        })) throw new InvalidOperationException("VGE postprocessing graphics boundary was rejected.");
        bloomTexture=useBloom?bloom.Texture:neutral.Texture;
        rayTexture=useRays?rays.Texture:neutral.Texture;
        published=true;
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Rejects stale or partial outputs instead of sampling old engine effect images.</summary>
    private static PostprocessPipeline Published()
    {
        if(active is not { published:true } owner) throw new InvalidOperationException("Owned postprocessing outputs were not published for this frame.");
        return owner;
    }
    /// <summary>Retires PSOs before their images and borrowed framebuffer views.</summary>
    private void Retire()
    {
        published=captured=false; bloomTexture=rayTexture=null;
        draw?.Dispose(); draw=null; bloom.Dispose(); rays.Dispose(); retained.Dispose(); resources.Dispose(); resources=new(); scene=glow=depth=null; neutral?.Dispose(); neutral=null;
    }
    /// <summary>Withdraws all executable-dependent publication during shader reload.</summary>
    private bool Reload() { Retire(); return true; }
    #endregion
}
