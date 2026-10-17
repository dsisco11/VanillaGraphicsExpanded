using System;
using System.Reactive.Linq;
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
    private System.IDisposable? reloadRegistration;
    private readonly ICoreClientAPI api;
    private readonly PipelineEnablement enablement;
    private readonly bool ownsEnablement;
    private readonly IDisposable enablementBinding;
    private bool enabled,disposed;
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
    internal LightShaftOcclusionRenderer(ICoreClientAPI api,PipelineEnablement? enablement=null)
    {
        this.api=api; active=this;
        ownsEnablement=enablement is null;this.enablement=enablement??new PipelineEnablement(api);
        enablementBinding=this.enablement.LightShaftOcclusion.Subscribe(SetEnabled);
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_lightshaft_reset");
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"vge_lightshaft_occlusion");
        reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload); api.Event.LeaveWorld+=Retire;
    }
    /// <summary>Builds visibility from completed opaque depth without sampling scene radiance or retaining prior-frame history.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage)
    {
        published=false;
        if(disposed || stage!=EnumRenderStage.Opaque) return;
        var native=enablement.NativePostprocess.Value;
        if(!enabled || !ScreenManager.Platform.DoPostProcessingEffects) return;
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        var settings=enablement.Settings.Value.Postprocess;
        draw??=new(); depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        var pipeline=shafts.Prepare(api,draw,primary.Width,primary.Height,native.LightShaftQuality,settings.LightShaftSamples,occlusionOnly:true);
        if(!GraphicsCommandContext.TryRun("LightShafts.Occlusion",[pipeline],true,commands=>
            shafts.Render(commands,draw,api,VgeFrameRenderer.Current,depth,depth,depth,(null,0),settings,occlusionOnly:true)))
            throw new InvalidOperationException("Light-shaft occlusion graphics boundary was rejected.");
        published=true;
    }
    /// <summary>Withdraws publication before unregistering and deleting its resources.</summary>
    public void Dispose()
    {
        if(disposed) return;disposed=true;
        enablementBinding.Dispose();if(ownsEnablement) enablement.Dispose();
        if(ReferenceEquals(active,this)) active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before); api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        reloadRegistration?.Dispose(); api.Event.LeaveWorld-=Retire; unregisterResize(); Retire();
    }
    #endregion
    #region Private
    /// <summary>Withdraws occlusion and retires its resources when native persistent demand turns off.</summary>
    private void SetEnabled(bool value) { if(enabled==value) return; enabled=value; if(!enabled) Retire(); }
    /// <summary>Retires executable references before images and clears every publication.</summary>
    private void Retire()
    {
        published=false; draw?.Dispose(); draw=null; shafts.Dispose(); resources.Dispose(); resources=new(); depth=null;
    }
    /// <summary>Invalidates screen and executable state on shader reload.</summary>
    private bool Reload() { Retire(); return true; }
    #endregion
}
