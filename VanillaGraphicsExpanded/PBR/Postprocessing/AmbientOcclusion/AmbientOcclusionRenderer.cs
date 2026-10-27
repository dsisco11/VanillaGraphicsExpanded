using VanillaGraphicsExpanded.Rendering.RHI;
using System.Linq;
using System;
using System.Reactive.Linq;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes current opaque visibility before ambient lighting, independently of LumOn availability.</summary>
internal sealed class AmbientOcclusionRenderer : IRenderer
{
    private System.IDisposable? reloadRegistration;
    private static AmbientOcclusionRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly PipelineEnablement enablement;
    private readonly bool ownsEnablement;
    private readonly IDisposable enablementBinding;
    private bool enabled;
    private readonly GBufferManager buffers;
    private readonly Action unregisterResize;
    private readonly AmbientOcclusionPass pass=new();
    private GpuResourceCollection resources=new();
    private BorrowedTexture? depth;
    private PostprocessDraw? draw;
    private bool published,disposed;
    #region Public API
    /// <summary>Runs after opaque geometry and before direct/indirect lighting consumers.</summary>
    public double RenderOrder=>8.8;
    /// <summary>Screen-space visibility is independent of chunk rendering distance.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Exposes only a successfully completed current-frame result.</summary>
    internal static GpuTexture? Texture=>active is {published:true} owner?owner.pass.Texture:null;
    /// <summary>Registers coherent frame, resize, shader and world lifecycle boundaries.</summary>
    internal AmbientOcclusionRenderer(ICoreClientAPI api,GBufferManager buffers,PipelineEnablement? enablement=null) {
        this.api=api;this.buffers=buffers;active=this;
        ownsEnablement=enablement is null;this.enablement=enablement??new PipelineEnablement(api);
        enablementBinding=this.enablement.AmbientOcclusion.Subscribe(SetEnabled);
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_ao_reset");
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"vge_ambient_occlusion");
        reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload);api.Event.LeaveWorld+=Retire;
    }
    /// <summary>Uses cached native quality and completed receiver inputs, publishing only after all spatial passes succeed.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage) {
        published=false;
        if(disposed) return;
        if(stage!=EnumRenderStage.Opaque) return;
        var settings=enablement.NativePostprocess.Value;
        if(!enabled || !ScreenManager.Platform.DoPostProcessingEffects) return;
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if(buffers.SurfaceTexture is not {} surface) throw new InvalidOperationException("AO requires the owned surface G-buffer.");
        draw??=new();depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        var horizon=GpuShaderProgramLibraries.Get<PostSsaoShaderProgram>(api,"pbr_post_ssao")??throw new InvalidOperationException("AO program missing.");
        var filter=GpuShaderProgramLibraries.Get<AmbientOcclusionFilterShaderProgram>(api,"pbr_ao_filter")??throw new InvalidOperationException("AO filter program missing.");
        var masks=GpuShaderProgramLibraries.Get<MinimumChildMaskShaderProgram>(api,"pbr_ao_minimum_children")??throw new InvalidOperationException("AO source-recovery mask program missing.");
        var receiverDepth=SceneColor.SceneColorParticleCapture.ReceiverDepthTexture(api)??depth;
        var hierarchy=DepthHierarchyRenderer.Texture??throw new InvalidOperationException("AO requires the current corrected-depth hierarchy.");
        if(!ReferenceEquals(DepthHierarchyRenderer.View,VgeFrameRenderer.Current) ||
            DepthHierarchyRenderer.FrameIndex!=UboPacking.ReadUInt32(VgeFrameRenderer.Current.Bytes,396)) throw new InvalidOperationException("AO hierarchy view mismatch.");
        var pipelines=pass.Prepare(draw,primary.Width,primary.Height,settings.SsaoQuality,horizon,filter,masks);
        var commands=RHICommandContext.Current;
        using var boundary=commands.EnterBoundary(new("AmbientOcclusion",true,graphics:pipelines.Select(value=>(RHIGraphicsPipelineRef)value),operations:BoundaryOperations.RenderPasses));
        boundary.Run(()=>pass.Render(commands,draw,receiverDepth,surface,hierarchy,VgeFrameRenderer.Current));
        published=true;
    }
    /// <summary>Unregisters callbacks before retiring resources and withdrawing publication.</summary>
    public void Dispose() {
        if(disposed) return;disposed=true;
        enablementBinding.Dispose();if(ownsEnablement) enablement.Dispose();
        if(ReferenceEquals(active,this)) active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before);api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        reloadRegistration?.Dispose();api.Event.LeaveWorld-=Retire;unregisterResize();Retire();
    }
    #endregion
    #region Private
    /// <summary>Withdraws published visibility and owned resources only when persistent demand turns off.</summary>
    private void SetEnabled(bool value) { if(enabled==value) return; enabled=value; if(!enabled) Retire(); }
    /// <summary>Retires executable references before target and external texture borrowers.</summary>
    private void Retire() {published=false;draw?.Dispose();draw=null;pass.Dispose();resources.Dispose();resources=new();depth=null;}
    /// <summary>Withdraws outputs when executable generations change.</summary>
    private bool Reload() {published=false;return true;}
    #endregion
}
