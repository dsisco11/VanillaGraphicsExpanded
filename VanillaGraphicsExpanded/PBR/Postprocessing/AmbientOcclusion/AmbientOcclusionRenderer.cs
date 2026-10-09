using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Publishes current opaque visibility before ambient lighting, independently of LumOn availability.</summary>
internal sealed class AmbientOcclusionRenderer : IRenderer
{
    private static AmbientOcclusionRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly GBufferManager buffers;
    private readonly Action unregisterResize;
    private readonly AmbientOcclusionPass pass=new();
    private readonly float[] inverseProjection=new float[16];
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
    internal AmbientOcclusionRenderer(ICoreClientAPI api,GBufferManager buffers) {
        this.api=api;this.buffers=buffers;active=this;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_ao_reset");
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"vge_ambient_occlusion");
        api.Event.ReloadShader+=Reload;api.Event.LeaveWorld+=Retire;
    }
    /// <summary>Captures native quality and completed receiver inputs, then publishes only after all spatial passes succeed.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage) {
        published=false;
        if(disposed) return;
        if(stage!=EnumRenderStage.Opaque) return;
        var settings=EnginePostprocessInputs.Capture(ScreenManager.Platform,api);
        if(!settings.Ssao) {if(draw is not null) Retire();return;}
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if(buffers.SurfaceTexture is not {} surface) throw new InvalidOperationException("AO requires the owned surface G-buffer.");
        if(!MatrixHelper.Invert(api.Render.CurrentProjectionMatrix,inverseProjection)) throw new InvalidOperationException("AO projection is singular.");
        draw??=new();depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        var horizon=GpuShaderPrograms.Get<PostSsaoShaderProgram>(api,"pbr_post_ssao")??throw new InvalidOperationException("AO program missing.");
        var reduction=GpuShaderPrograms.Get<AmbientOcclusionDepthShaderProgram>(api,"pbr_ao_depth")??throw new InvalidOperationException("AO depth program missing.");
        var filter=GpuShaderPrograms.Get<AmbientOcclusionFilterShaderProgram>(api,"pbr_ao_filter")??throw new InvalidOperationException("AO filter program missing.");
        var receiverDepth=SceneColor.SceneColorParticleCapture.ReceiverDepthTexture(api)??depth;
        var pipelines=pass.Prepare(draw,primary.Width,primary.Height,settings.SsaoQuality,horizon,reduction,filter);
        if(!GraphicsCommandContext.TryRun("AmbientOcclusion",pipelines,true,commands=>
            pass.Render(commands,draw,receiverDepth,surface,inverseProjection,api.Render.CameraMatrixOriginf)))
            throw new InvalidOperationException("AO graphics boundary rejected.");
        published=true;
    }
    /// <summary>Unregisters callbacks before retiring resources and withdrawing publication.</summary>
    public void Dispose() {
        if(disposed) return;disposed=true;
        if(ReferenceEquals(active,this)) active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before);api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        api.Event.ReloadShader-=Reload;api.Event.LeaveWorld-=Retire;unregisterResize();Retire();
    }
    #endregion
    #region Private
    /// <summary>Retires executable references before target and external texture borrowers.</summary>
    private void Retire() {published=false;draw?.Dispose();draw=null;pass.Dispose();resources.Dispose();resources=new();depth=null;}
    /// <summary>Withdraws outputs when executable generations change.</summary>
    private bool Reload() {Retire();return true;}
    #endregion
}
