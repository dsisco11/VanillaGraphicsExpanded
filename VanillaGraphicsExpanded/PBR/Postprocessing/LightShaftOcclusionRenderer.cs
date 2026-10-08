using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;
using Vintagestory.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes current-frame depth occlusion before deferred aerial scattering and forward OIT consumers.</summary>
internal sealed class LightShaftOcclusionRenderer : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly Action unregisterResize;
    private readonly LightShaftRenderer shafts=new();
    private PostprocessDraw? draw;
    private GpuResourceCollection resources=new();
    private BorrowedTexture? depth;
    private static LightShaftOcclusionRenderer? active;
    private bool published;
    #region Public API
    /// <summary>Runs after opaque receivers and before direct lighting and aerial composition.</summary>
    public double RenderOrder=>8.75;
    /// <summary>Screen-space visibility does not depend on world render range.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Exposes only this frame's completed occlusion; missing means zero occlusion.</summary>
    internal static GpuTexture? Texture=>active is {published:true} owner?owner.shafts.Occlusion:null;
    /// <summary>Registers frame invalidation and pre-lighting publication under the shared screen lifetime.</summary>
    internal LightShaftOcclusionRenderer(ICoreClientAPI api)
    {
        this.api=api; active=this;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_lightshaft_reset");
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"vge_lightshaft_occlusion");
        api.Event.ReloadShader+=Reload; api.Event.LeaveWorld+=Retire;
    }
    /// <summary>Builds visibility from completed opaque depth without sampling scene radiance or retaining prior-frame history.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        published=false;
        if(stage!=EnumRenderStage.Opaque) return;
        var native=EnginePostprocessInputs.Capture(ScreenManager.Platform,api);
        if(!native.LightShafts) { if(draw is not null) Retire(); return; }
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var settings=ConfigModSystem.Config.Postprocessing.Snapshot();
        draw??=new(); depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        var pipeline=shafts.Prepare(api,draw,primary.Width,primary.Height,native.LightShaftQuality,settings.LightShaftSamples,occlusionOnly:true);
        if(!GraphicsCommandContext.TryRun("LightShafts.Occlusion",[pipeline],true,commands=>
            shafts.Render(commands,draw,api,api.Render.CurrentProjectionMatrix,depth,depth,depth,(null,0),settings,occlusionOnly:true)))
            throw new InvalidOperationException("Light-shaft occlusion graphics boundary was rejected.");
        published=true;
    }
    /// <summary>Withdraws publication before unregistering and deleting its resources.</summary>
    public void Dispose()
    {
        if(ReferenceEquals(active,this)) active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before); api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        api.Event.ReloadShader-=Reload; api.Event.LeaveWorld-=Retire; unregisterResize(); Retire();
    }
    #endregion
    #region Private
    /// <summary>Retires executable references before images and clears every publication.</summary>
    private void Retire()
    {
        published=false; draw?.Dispose(); draw=null; shafts.Dispose(); resources.Dispose(); resources=new(); depth=null;
    }
    /// <summary>Invalidates screen and executable state on shader reload.</summary>
    private bool Reload() { Retire(); return true; }
    #endregion
}
