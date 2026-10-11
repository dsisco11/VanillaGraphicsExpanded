using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;

using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.CameraExposure;

/// <summary>Meters the completed HDR scene before generated glare and publishes GPU-resident camera exposure.</summary>
internal sealed class CameraExposureRenderer : IRenderer
{
    private System.IDisposable? reloadRegistration;
    private static CameraExposureRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly CameraExposureHistory history = new();
    private readonly Action unregisterResize;
    private CameraExposureTargets? targets;
    private BorrowedTexture? sceneTexture;
    private CameraHistogramShaderProgram? histogram;
    private CameraAdaptShaderProgram? adaptation;
    private CameraExposureParameters settings;
    private bool reset = true;
    private bool published, captured, disposed;

    #region Public API
    #region Lifetime
    /// <summary>Registers frame timing and resource lifecycle independently of the display shader.</summary>
    internal CameraExposureRenderer(ICoreClientAPI api)
    {
        this.api = api;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Retire);
        api.Event.LeaveWorld += Retire;
        reloadRegistration = VanillaGraphicsExpanded.Rendering.Integration.EngineShaderReload.Register(api, Reload);
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_camera_exposure");
        active = this;
    }
    /// <summary>Captures timing before scene rendering begins.</summary>
    public double RenderOrder => 1001;
    /// <summary>Does not depend on terrain range.</summary>
    public int RenderRange => int.MaxValue;
    /// <summary>Withdraws publication before unregistering and retiring GPU resources.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.LeaveWorld -= Retire;
        reloadRegistration?.Dispose();
        unregisterResize(); Retire();
    }
    #endregion

    #region Frame publication
    /// <summary>Snapshots settings and camera continuity once per frame without submitting GPU work.</summary>
    public void OnRenderFrame(float dt, EnumRenderStage stage)
    {
        if (disposed || stage != EnumRenderStage.Before) return;
        published = false;
        settings = ConfigModSystem.Config.CameraExposure.Snapshot();
        var position = api.World.Player.Entity.Pos;
        reset |= history.Capture(settings, dt, position.X, position.Y, position.Z,
            position.Yaw, position.Pitch, position.Dimension, (int)api.Render.CameraType);
        captured = true;
    }
    /// <summary>Runs at the engine postprocessing boundary after late scene contributors and before bloom/light shafts.</summary>
    internal static void MeterScene()
    {
        if (!SceneColorPipeline.HasSceneInput) return;
        if (active is not { } renderer)
            throw new InvalidOperationException("VGE HDR camera exposure owner is unavailable.");
        renderer.Meter();
    }
    /// <summary>Returns one coherent publication; automatic mode never silently substitutes manual exposure.</summary>
    internal static (GpuShaderStorageBuffer? Buffer, float ManualEV) DisplayExposure()
    {
        if (active is not { } renderer)
            throw new InvalidOperationException("VGE HDR camera exposure owner is unavailable.");
        if (!renderer.published) throw new InvalidOperationException("Camera exposure was not published for this HDR frame.");
        return (renderer.settings.Enabled ? renderer.targets!.Exposure : null,
            Math.Clamp(renderer.settings.ManualEV + renderer.settings.Compensation, renderer.settings.MinEV, renderer.settings.MaxEV));
    }
    #endregion
    #endregion

    #region Private
    #region Submission
    /// <summary>Submits fixed-size metering and temporal reduction, then atomically publishes the completed result.</summary>
    private void Meter()
    {
        if (published) return;
        if (!captured) throw new InvalidOperationException("Camera exposure frame inputs were not captured.");
        CameraExposureDispatch.RequireSupport();
        if (!settings.Enabled)
        {
            // Manual mode has no metering dispatches or retained histogram/history allocation.
            DisposeResources(); reset = true; published = true; return;
        }
        histogram ??= CameraHistogramShaderProgram.Create(api);
        adaptation ??= CameraAdaptShaderProgram.Create(api);
        if (!histogram.EnsureReady() || !adaptation.EnsureReady())
            throw new InvalidOperationException("VGE camera exposure compute shaders are unavailable.");
        targets ??= new CameraExposureTargets();
        var primary = api.Render.FrameBuffers[(int)EnumFrameBuffer.Primary];
        if (primary?.ColorTextureIds is not { Length: > 0 } colors || colors[0] == 0)
            throw new InvalidOperationException("VGE camera metering requires the completed HDR primary scene.");
        if (sceneTexture?.TextureId != colors[0]) { sceneTexture?.Dispose(); sceneTexture = new(colors[0]); }
        reset |= VgeFrameRenderer.Current.CameraCut;
        histogram.Capture(settings, reset); histogram.SceneRadiance = sceneTexture;
        adaptation.Capture(settings, reset);
        adaptation.Histogram = targets.Histogram; adaptation.PreviousExposure = targets.Exposure;
        adaptation.OutputExposure = targets.WriteTarget;
        histogram.Histogram = targets.Histogram;
        if (!CameraExposureDispatch.TryRun(histogram, adaptation))
            throw new InvalidOperationException("VGE camera exposure compute boundary was rejected.");
        targets.Publish(); reset = false; published = true;
    }
    #endregion

    #region Retirement
    /// <summary>Releases executable inputs before owned buffers and the borrowed scene texture.</summary>
    private void DisposeResources()
    {
        histogram?.Dispose(); histogram = null;
        adaptation?.Dispose(); adaptation = null;
        targets?.Dispose(); targets = null;
        sceneTexture?.Dispose(); sceneTexture = null;
    }
    /// <summary>Invalidates history and frame publication on world, storage and shader lifecycle changes.</summary>
    private void Retire()
    {
        published = captured = false; reset = true; history.Reset(); DisposeResources();
    }
    /// <summary>Retires executable-dependent resources before accepting a shader reload.</summary>
    private bool Reload() { Retire(); return true; }
    #endregion
    #endregion
}