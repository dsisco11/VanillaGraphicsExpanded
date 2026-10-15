using System;
using System.Collections.Generic;
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
    private System.IDisposable? reloadRegistration;
    private static PostprocessPipeline? active;
    private readonly ICoreClientAPI api;
    private readonly Action unregisterResize;
    private readonly BloomRenderer bloom=new();
    private readonly LightShaftRenderer rays=new();
    private readonly RetainedPostprocessRenderer retained=new();
    private readonly ColorGradingLut grading=new();
    private readonly LocalExposure.LocalExposureRenderer local=new();
    private LocalExposure.LocalExposureParameters localSettings;
    private GpuResourceCollection resources=new();
    private BorrowedTexture? scene,glow,depth;
    private PostprocessDraw? draw;
    private GpuFramebuffer? finalTarget;
    private EnginePostprocessInputs frameEffects;
    private PostprocessImage? neutral;
    private PostprocessParameters settings;
    private bool captured,published,disposed;
    private GpuTexture? bloomTexture,rayTexture;
    #region Public API
    #region Lifetime
    /// <summary>Registers owned postprocessing and final composition while preserving the presentation handoff.</summary>
    internal PostprocessPipeline(ICoreClientAPI api)
    {
        this.api=api;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_postprocess");
        api.Event.LeaveWorld+=RetireWorld; reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload); active=this;
    }
    /// <summary>Captures settings after scene and camera preparation.</summary>
    public double RenderOrder=>1002;
    /// <summary>Runs independently of world view distance.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Supplies the captured glare gains used to reserve visible-disk storage headroom.</summary>
    internal static (float Bloom, float Shafts) GlareStrengths => active is { captured: true } owner
        ? (owner.settings.BloomStrength, owner.settings.LightShaftStrength)
        : throw new InvalidOperationException("Scene glare settings have not been captured.");
    /// <summary>Withdraws publication and captures one coherent settings snapshot for the coming scene.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        if(disposed||stage!=EnumRenderStage.Before) return;
        published=false; captured=true; settings=ConfigModSystem.Config.Postprocessing.Snapshot();
        localSettings=(ConfigModSystem.Config.Postprocessing.LocalExposure??new()).Snapshot();
    }
    /// <summary>Unregisters all callbacks before retiring owned resources.</summary>
    public void Dispose()
    {
        if(disposed) return; disposed=true;
        if(ReferenceEquals(active,this)) active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before); api.Event.LeaveWorld-=RetireWorld; reloadRegistration?.Dispose();
        unregisterResize(); RetireWorld();
    }
    #endregion
    #region Engine handoff
    /// <summary>Replaces HDR scene postprocessing, leaving non-scene menu rendering at its existing display boundary.</summary>
    internal static bool ReplaceEnginePass(ClientPlatformWindows platform)
    {
        if(!SceneColorPipeline.HasSceneInput) return false;
        var owner=active??throw new InvalidOperationException("VGE postprocessing owner is unavailable.");
        owner.Render(EnginePostprocessInputs.Capture(platform,owner.api));
        // Preserve the installed method's documented exit target/state for the following overlay stage.
        var primary=owner.api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        platform.LoadFrameBuffer(EnumFrameBuffer.Primary);
        platform.GlViewport(0,0,primary.Width,primary.Height);
        platform.GlToggleBlend(true,EnumBlendMode.Standard);
        return true;
    }
    /// <summary>Consumes the HDR publication at the existing final scheduling boundary, leaving menus independent.</summary>
    internal static bool ReplaceFinalPass(ClientPlatformWindows platform)
    {
        if(!SceneColorPipeline.HasSceneInput) return false;
        try
        {
            var owner=Published();
            owner.RenderFinal(FinalDisplayParameters.Capture(owner.api));
            // The following overlay stage expects the native final pass's depth/blend exit state.
            platform.GlDisableDepthTest();
            platform.GlToggleBlend(true,EnumBlendMode.Standard);
            return true;
        }
        finally { SceneColorPipeline.EndScene(); }
    }
    /// <summary>Composes owned intermediate outputs directly into the primary presentation image.</summary>
    internal void RenderFinal(FinalDisplayParameters display)
    {
        if(!published) throw new InvalidOperationException("Owned postprocessing outputs were not published for this frame.");
        Diagnostics.ExposureSnapshotLog.CaptureIfRequested(api,retained.Luma,
            localSettings.Enabled ? local.BaseTexture : null,localSettings,frameEffects.Fxaa);
        var shader=Rendering.Shaders.GpuShaderProgramLibraries.Get<FinalDisplayShaderProgram>(api,
            localSettings.Enabled ? "pbr_final_local_exposure" : "pbr_final");
        if(shader?.EnsureReady()!=true) throw new InvalidOperationException("Owned final display shader unavailable.");
        // Only the presentation destination is borrowed; every sampled postprocess output is owned.
        if(finalTarget is null)
        {
            var attachment=resources.Own(GpuFramebufferAttachment.FromTexture(scene!));
            finalTarget=resources.Own(GpuFramebuffer.Create([attachment],debugName:"Postprocess.Final"));
        }
        var exposure=CameraExposureRenderer.DisplayExposure();
        shader.SceneImage=retained.Luma; shader.BloomImage=bloomTexture; shader.ShaftImage=rayTexture;
        shader.ExposureBuffer=exposure.Buffer;
        shader.LocalBase=localSettings.Enabled ? local.BaseTexture : null;
        shader.CaptureLocal(localSettings);
        shader.GradingLookup=grading.Prepare(api,display.Grading);
        shader.Capture(frameEffects.Fxaa,display,new(exposure.ManualLogGain,exposure.Buffer is null?0:1,0,CameraExposureRenderer.DisplayLogMiddleGray));
        var pipeline=draw!.Prepare(shader,finalTarget);
        if(!GraphicsCommandContext.TryRun("Postprocess.Final",[pipeline],true,
            commands=>draw.Submit(commands,pipeline,finalTarget)))
            throw new InvalidOperationException("VGE final composition graphics boundary was rejected.");
        published=captured=false;
    }
    /// <summary>Executes owned glare and luminance preparation, publishing only after complete success.</summary>
    internal void Render(EnginePostprocessInputs engine)
    {
        if(published) return;
        if(!captured) throw new InvalidOperationException("VGE postprocessing frame was not captured.");
        CameraExposureRenderer.MeterScene();
        var exposure=CameraExposureRenderer.DisplayExposure();
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        draw??=new(); neutral??=new(1,1,"Postprocess.Neutral",clear:true);
        bool useBloom=engine.Bloom&&settings.BloomStrength>0;
        bool useRays=engine.LightShafts&&settings.LightShaftStrength>0&&settings.LightShaftLimit>0;
        var pipelines=new List<GraphicsPipeline>();
        // Capture typed inputs once for this framebuffer publication, before entering a pass.
        scene??=resources.Own(new BorrowedTexture(primary.ColorTextureIds[0]));
        // Publish scene-only guidance before bloom captures source-local exposure.
        // Glare never feeds the base or the global camera meter.
        if(localSettings.Enabled) local.Render(api,scene,localSettings); else local.Dispose();
        if(useBloom) pipelines.Add(bloom.Prepare(api,draw,primary.Width,primary.Height,settings.BloomLevels,
            localSettings,localSettings.Enabled ? local.BaseTexture : null,CameraExposureRenderer.DisplayLogMiddleGray));
        else bloom.Dispose();
        if(useRays) pipelines.Add(rays.Prepare(api,draw,primary.Width,primary.Height,engine.LightShaftQuality,settings.LightShaftSamples)); else rays.Dispose();
        if(useRays) depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        if(useRays) glow??=resources.Own(new BorrowedTexture(primary.ColorTextureIds[1]));
        pipelines.AddRange(retained.Prepare(api,draw));
        if(!GraphicsCommandContext.TryRun("Postprocess.Scene",pipelines,true,commands=>{
            // Metering precedes generated glare; glare cannot feed back into the camera.
            if(useBloom) bloom.Render(commands,draw,scene,exposure,settings);
            if(useRays) rays.Render(commands,draw,api,VgeFrameRenderer.Current,scene,glow!,depth!,exposure,settings);
            retained.Render(commands,draw,engine,scene,exposure);
        })) throw new InvalidOperationException("VGE postprocessing graphics boundary was rejected.");
        frameEffects=engine;
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
        Diagnostics.ExposureSnapshotLog.Cancel();
        draw?.Dispose(); draw=null; bloom.Dispose(); rays.Dispose(); retained.Dispose(); local.Dispose(); resources.Dispose(); resources=new(); finalTarget=null; scene=glow=depth=null; neutral?.Dispose(); neutral=null;
    }
    /// <summary>Withdraws all executable-dependent publication during shader reload.</summary>
    private bool Reload() { Retire(); grading.Invalidate(); return true; }
    /// <summary>Retires size-dependent passes and persistent display transforms when the world leaves.</summary>
    private void RetireWorld() => Rendering.Integration.RenderingCleanup.Run(Retire,grading.Dispose);
    #endregion
}
