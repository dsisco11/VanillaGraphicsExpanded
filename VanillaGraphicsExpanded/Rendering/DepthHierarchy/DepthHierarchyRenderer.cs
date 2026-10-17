using System;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Profiling;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Publishes corrected opaque depth once before AO and indirect lighting, independently of any individual consumer.</summary>
internal sealed class DepthHierarchyRenderer : IRenderer
{
    private System.IDisposable? reloadRegistration;
    private static DepthHierarchyRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly Func<bool>? additionalDemand;
    private readonly Action unregisterResize;
    private readonly DepthHierarchyPass pass=new();
    private GpuResourceCollection resources=new();
    private BorrowedTexture? depth;
    private DepthHierarchyComputeShader? compute;
    private bool published,disposed;
    private uint frameIndex;
    #region Public API
    #region Publication
    /// <summary>Runs after corrected receiver capture and before the earliest hierarchy consumer.</summary>
    public double RenderOrder=>8.7;
    /// <summary>Depth hierarchy is independent of chunk rendering distance.</summary>
    public int RenderRange=>int.MaxValue;
    /// <summary>Exposes only the current view's successfully generated chain.</summary>
    internal static DynamicTexture2D? Texture=>active is {published:true} owner?owner.pass.Texture:null;
    /// <summary>Identifies the shared frame snapshot used for publication.</summary>
    internal static VgeFrameUniformBuffer? View=>active is {published:true} owner?owner.view:null;
    /// <summary>Identifies the exact frame captured into the current allocation generation.</summary>
    internal static uint? FrameIndex=>active is {published:true} owner?owner.frameIndex:null;
    /// <summary>Borrows only a completed hierarchy matching the exact camera frame and view extent.</summary>
    internal static bool TryGetCurrent(VgeFrameUniformBuffer expected,int width,int height,out DynamicTexture2D? texture)
    {
        texture=null;
        var candidate=Texture;
        // View buffers are reused between frames, so pointer identity alone cannot certify freshness.
        if(candidate is null || !candidate.IsValid || !ReferenceEquals(View,expected)
            || FrameIndex!=UboPacking.ReadUInt32(expected.Bytes,396)
            || candidate.Width!=width || candidate.Height!=height) return false;
        texture=candidate;
        return true;
    }
    #endregion
    #region Lifecycle and execution
    /// <summary>Registers publication/lifetime boundaries and optional demand from another shared consumer.</summary>
    internal DepthHierarchyRenderer(ICoreClientAPI api,Func<bool>? additionalDemand=null) {
        this.api=api;this.additionalDemand=additionalDemand;active=this;
        unregisterResize=ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder,Retire);
        api.Event.RegisterRenderer(this,EnumRenderStage.Before,"vge_depth_reset");
        api.Event.RegisterRenderer(this,EnumRenderStage.Opaque,"vge_depth_hierarchy");
        reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload);api.Event.LeaveWorld+=Retire;
    }
    /// <summary>Builds only for enabled consumers, then atomically publishes the completed current-view hierarchy.</summary>
    public void OnRenderFrame(float deltaTime,EnumRenderStage stage) {
        if(disposed)return;
        if(stage==EnumRenderStage.Before) {published=false;view=null;return;}
        if(stage!=EnumRenderStage.Opaque || published)return;
        bool required=PbrShaderLightingMode.LumOnEnabled || EnginePostprocessInputs.Capture(ScreenManager.Platform,api).Ssao || additionalDemand?.Invoke()==true;
        if(!required) {Retire();return;}
        var primary=api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        depth??=resources.Own(new BorrowedTexture(primary.DepthTextureId));
        compute??=DepthHierarchyComputeShader.Create(api);
        var receiver=SceneColorParticleCapture.ReceiverDepthTexture(api)??depth;
        pass.Prepare(primary.Width,primary.Height,compute);
        var snapshot=VgeFrameRenderer.Current;
        using var gpuScope=GlGpuProfiler.Instance.Scope("DepthHierarchy");
        pass.Render(receiver);
        view=snapshot;frameIndex=UboPacking.ReadUInt32(snapshot.Bytes,396);published=true;
    }
    /// <summary>Unregisters lifecycle callbacks before withdrawing storage.</summary>
    public void Dispose() {
        if(disposed)return;disposed=true;if(ReferenceEquals(active,this))active=null;
        api.Event.UnregisterRenderer(this,EnumRenderStage.Before);api.Event.UnregisterRenderer(this,EnumRenderStage.Opaque);
        reloadRegistration?.Dispose();api.Event.LeaveWorld-=Retire;unregisterResize();Retire();
    }
    #endregion
    #endregion
    #region Private
    private VgeFrameUniformBuffer? view;
    /// <summary>Withdraws frame identity and retires executable references before attachments.</summary>
    private void Retire() {published=false;view=null;pass.Dispose();compute?.Dispose();compute=null;resources.Dispose();resources=new();depth=null;}
    /// <summary>Rejects stale executable generations.</summary>
    private bool Reload() {published=false;view=null;pass.Dispose();return true;}
    #endregion
}
