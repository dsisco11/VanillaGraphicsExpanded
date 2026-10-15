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
    private FrameLightingShaderProgram? frameLighting;
    private GpuShaderStorageBuffer? frozenLighting;
    private bool lightingFrozen;
    private uint lightingGeneration;
    private uint lightingFrameIndex;
    private float lightingBound;
    private bool localAdaptation;

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
        lightingFrozen = false;
        settings = ConfigModSystem.Config.CameraExposure.Snapshot();
        bool requested=ConfigModSystem.Config.Postprocessing.LocalExposure?.Enabled??true;
        reset|=localAdaptation!=requested;
        localAdaptation=requested;
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
    /// <summary>Returns the independent physical viewing estimate only for the enabled current-frame candidate.</summary>
    internal static GpuShaderStorageBuffer? DisplayAdaptation()
    {
        if(active is not { published:true } renderer) throw new InvalidOperationException("Visual adaptation is not published.");
        return renderer.localAdaptation ? renderer.targets!.Exposure : null;
    }
    /// <summary>Exposes matching captured policy and completed history for an explicit diagnostic request.</summary>
    internal static (CameraExposureParameters Settings, GpuShaderStorageBuffer? History) DiagnosticPublication()
    {
        if(active is not { published:true, disposed:false } renderer)
            throw new InvalidOperationException("Camera exposure is not published for this diagnostic frame.");
        return (renderer.settings, renderer.settings.Enabled || renderer.localAdaptation ? renderer.targets!.Exposure : null);
    }
    /// <summary>Freezes admitted physical-light metadata after current light publication and before the first consuming draw.</summary>
    internal static void FreezeLightingStorage(FrameLightingBounds bounds, uint calibrationGeneration)
    {
        if (active is not { captured: true, disposed: false } renderer)
            throw new InvalidOperationException("Camera inputs are unavailable for physical lighting.");
        // Access proves the current light-list publication has completed; never borrow a stale list.
        _ = VgeLightsRenderer.Current;
        float bound = bounds.Total;
        // A frozen dispatch does not read back its validity flag. Reject unsupported
        // physical bounds on the CPU first; either camera seed then admits the same
        // minimum-retention domain under the selected scale rule.
        _ = LightingStorage.FrameScale(1, bound);
        if (calibrationGeneration == 0) throw new ArgumentOutOfRangeException(nameof(calibrationGeneration));
        if (calibrationGeneration != VgeFrameRenderer.Current.CalibrationGeneration)
            throw new InvalidOperationException("Frame storage requires the current lighting calibration.");
        if (renderer.lightingFrozen)
        {
            if (renderer.lightingBound != bound || renderer.lightingGeneration != calibrationGeneration)
                throw new InvalidOperationException("Physical lighting cannot change after the frame scale is frozen.");
            return;
        }
        if (renderer.published)
            throw new InvalidOperationException("Frame lighting storage must be frozen before scene metering.");
        renderer.FreezeLighting(bound, calibrationGeneration);
    }
    /// <summary>Supplies the frozen scale to typed physical-light consumers; legacy producers never request it.</summary>
    internal static GpuShaderStorageBuffer FrozenLighting
    {
        get
        {
            if (active is not { lightingFrozen: true, disposed: false } renderer
                || renderer.lightingFrameIndex != VgeFrameRenderer.Current.FrameIndex
                || renderer.lightingGeneration != VgeFrameRenderer.Current.CalibrationGeneration)
                throw new InvalidOperationException("Physical storage does not match the current frame and calibration.");
            SceneLightingPublicationRenderer.RequirePhysicalInputsWhenActive();
            return renderer.frozenLighting!;
        }
    }
    /// <summary>Exposes admitted physical units, scale ownership and compatibility references without GPU readback or logging.</summary>
    internal static (bool Frozen, float PhysicalBoundNits, uint CalibrationGeneration, float PersistentScale,
        float BlockReferenceLux, float EmissionReferenceNits) LightingDiagnostics =>
        (active?.lightingFrozen ?? false, active?.lightingFrozen == true ? active.lightingBound : 0,
            active?.lightingFrozen == true ? active.lightingGeneration : 0, LightingStorage.PersistentScale,
            Rendering.VgeFrameRenderer.CurrentLightingCalibration.BlockReferenceLux, LightingCalibration.EmissionReferenceNits);
    /// <summary>Formats calibrated source references separately from completed frame storage integration.</summary>
    internal static string LightingDiagnosticText
    {
        get
        {
            var state = LightingDiagnostics;
            var indirect = ConfigModSystem.Config.LumOn;
            string departures = indirect.Enabled && (indirect.Intensity != 1 || indirect.IndirectTint[0] != 1
                || indirect.IndirectTint[1] != 1 || indirect.IndirectTint[2] != 1) ? "; artistic GI gain/tint" : string.Empty;
            if (!state.Frozen) return $"Light: calibrated sources; frame storage integration pending; block {state.BlockReferenceLux:G3} lx, "
                + $"emission {state.EmissionReferenceNits:G3} nits" + departures;
            return $"Light: B {state.PhysicalBoundNits:G4} nits, P GPU, C {state.PersistentScale:G3}; "
                + $"block {state.BlockReferenceLux:G3} lx, emission {state.EmissionReferenceNits:G3} nits" + departures;
        }
    }
    #endregion
    #endregion

    #region Private
    #region Submission
    /// <summary>Uses the prior published automatic EV or a bounded manual seed and preserves the result throughout postprocessing.</summary>
    private void FreezeLighting(float physicalBound, uint calibrationGeneration)
    {
        CameraExposureDispatch.RequireSupport();
        frameLighting ??= FrameLightingShaderProgram.Create(api);
        if (!frameLighting.EnsureReady()) throw new InvalidOperationException("Frame lighting scale shader is unavailable.");
        float manual = MathF.Pow(2, Math.Clamp(settings.ManualEV + settings.Compensation, settings.MinEV, settings.MaxEV));
        reset |= VgeFrameRenderer.Current.CameraCut;
        bool automaticHistory = settings.Enabled && targets is not null && !reset;
        frameLighting.Capture(physicalBound, manual, automaticHistory, VgeFrameRenderer.Current.FrameIndex, calibrationGeneration);
        if (frozenLighting is null)
        {
            frozenLighting = GpuShaderStorageBuffer.Create(debugName: "Camera.FrozenLighting");
            frozenLighting.Allocate(32);
        }
        frameLighting.PreviousExposure = automaticHistory ? targets!.Exposure : null;
        frameLighting.Output = frozenLighting;
        if (!CameraExposureDispatch.TryFreeze(frameLighting))
            throw new InvalidOperationException("Frame lighting scale publication was rejected.");
        lightingBound = physicalBound; lightingGeneration = calibrationGeneration;
        lightingFrameIndex = VgeFrameRenderer.Current.FrameIndex; lightingFrozen = true;
    }
    /// <summary>Submits dense downscaled metering and temporal reduction, then atomically publishes the completed result.</summary>
    private void Meter()
    {
        if (published) return;
        if (!captured) throw new InvalidOperationException("Camera exposure frame inputs were not captured.");
        CameraExposureDispatch.RequireSupport();
        if (!settings.Enabled && !localAdaptation)
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
        histogram.Capture(settings, reset, localAdaptation); histogram.SceneRadiance = sceneTexture;
        adaptation.Capture(settings, reset, localAdaptation);
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
        published = captured = lightingFrozen = false; reset = true; history.Reset(); DisposeResources();
        frameLighting?.Dispose(); frameLighting = null;
        frozenLighting?.Dispose(); frozenLighting = null;
    }
    /// <summary>Retires executable-dependent resources before accepting a shader reload.</summary>
    private bool Reload() { Retire(); return true; }
    #endregion
    #endregion
}
