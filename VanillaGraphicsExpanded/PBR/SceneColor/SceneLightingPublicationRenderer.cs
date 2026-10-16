using System;
using System.Numerics;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.CameraExposure;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Freezes physical source metadata after native light collection and before scene draws.</summary>
internal sealed class SceneLightingPublicationRenderer : IRenderer
{
    private static SceneLightingPublicationRenderer? active;
    private readonly ICoreClientAPI api;
    private readonly Action unregisterResize;
    private readonly IDisposable reloadRegistration;
    private SceneColorCompatibilityPublication? compatibility;
    private FrameLightingBounds bounds;
    private uint calibrationGeneration;
    private bool lumOnEnabled;
    private float perceptionIntensity;
    private AtmosphereLighting? atmosphere;
    private double indirectGain;
    private double pointMaximum;
    private float emissionMaximum;
    private bool disposed;

    #region Public API
    /// <summary>Registers the source publication immediately after the existing dynamic-light owner.</summary>
    internal SceneLightingPublicationRenderer(ICoreClientAPI api)
    {
        this.api = api;
        active = this;
        unregisterResize = ScreenResourceManager.Register(ScreenResourceManager.CompositeOrder, Reset);
        reloadRegistration = Rendering.Integration.EngineShaderReload.Register(api, Reload);
        api.Event.LeaveWorld += Reset;
        api.Event.RegisterRenderer(this, EnumRenderStage.Before, "vge_scene_lighting_reset");
        api.Event.RegisterRenderer(this, EnumRenderStage.Opaque, "vge_scene_lighting_freeze");
    }

    /// <summary>Runs after the immutable dynamic-light snapshot at order minus ten thousand.</summary>
    public double RenderOrder => -9999;
    /// <summary>Source publication is independent of visibility distance.</summary>
    public int RenderRange => int.MaxValue;

    /// <summary>Withdraws old metadata and publishes one admitted frame scale before the first scene consumer.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (disposed) return;
        if (stage == EnumRenderStage.Before) { Reset(); return; }
        if (stage != EnumRenderStage.Opaque || compatibility.HasValue) return;
        if (!SceneColorPipeline.HasSceneInput)
            throw new InvalidOperationException("Physical scene publication requires prepared scene storage.");
        var fog = SceneColorFogEnvelope.FromNative(api);
        var clouds = SceneColorCloudInputs.FromNative(api);
        var native = SceneColorCompatibilityPublication.FromNative(api, fog, clouds);
        var currentBounds = Assemble(native.MaximumNits);
        CameraExposureRenderer.FreezeLightingStorage(currentBounds, VgeFrameRenderer.Current.CalibrationGeneration);
        // Expose source admission only after the GPU scale publication succeeds.
        bounds = currentBounds;
        calibrationGeneration = VgeFrameRenderer.Current.CalibrationGeneration;
        lumOnEnabled = PbrShaderLightingMode.LumOnEnabled;
        perceptionIntensity = api.Render.ShaderUniforms.PerceptionEffectIntensity;
        atmosphere = AtmosphereModSystem.Lighting;
        indirectGain = CurrentIndirectGain();
        pointMaximum = VgeLightsRenderer.Current.SummedCandelaMaximum;
        emissionMaximum = Math.Max(LightingCalibration.EmissionReferenceNits,
            MaterialAtlasSystem.Instance.TextureStore.MaximumEmissionNits);
        compatibility = native;
    }

    /// <summary>Checks native compatibility inputs against the immutable earlier publication before binding a scene program.</summary>
    internal static void RequireNativeInputs(string passName)
    {
        if (!SceneColorPipeline.HasSceneInput)
            throw new InvalidOperationException("Native scene admission requires prepared scene storage.");
        var owner = RequireCurrent();
        var fog = SceneColorFogEnvelope.FromNative(owner.api);
        // The native cloud owner multiplies its vertex perception input around Use
        // and restores it before fragment uniforms. Undo only that known temporary
        // owner operation when rebuilding its geometry certificate at this boundary.
        var clouds = SceneColorCloudInputs.FromNative(owner.api, passName == "clouds" ? 20 : 1,
            passName == "clouds" ? owner.perceptionIntensity : null);
        var native = SceneColorCompatibilityPublication.FromNative(owner.api, fog, clouds);
        native.RequireWithin(owner.compatibility!.Value);
        RequirePhysicalInputsWhenActive();
    }

    /// <summary>Checks participating scene sources before typed or native consumers obtain their frozen storage.</summary>
    internal static void RequirePhysicalInputsWhenActive()
    {
        // Standalone camera-storage qualification supplies its bound explicitly;
        // the composed scene owner additionally enforces actual live source admission.
        if (active is null) return;
        var owner = RequireCurrent();
        var current = owner.Assemble(owner.compatibility!.Value.MaximumNits);
        // Check source envelopes separately. A decrease in one source cannot pay
        // for an increase in another after earlier draws have already stored light.
        if (VgeLightsRenderer.Current.SummedCandelaMaximum > owner.pointMaximum
            || CurrentIndirectGain() > owner.indirectGain
            || Math.Max(LightingCalibration.EmissionReferenceNits,
                MaterialAtlasSystem.Instance.TextureStore.MaximumEmissionNits) > owner.emissionMaximum
            || current.Total > owner.bounds.Total)
            throw new InvalidOperationException("Physical sources increased after the frame scale was frozen.");
    }

    /// <summary>Unregisters source admission before camera and scene resource owners are retired.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        if (ReferenceEquals(active, this)) active = null;
        api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
        api.Event.UnregisterRenderer(this, EnumRenderStage.Opaque);
        api.Event.LeaveWorld -= Reset;
        reloadRegistration.Dispose();
        unregisterResize();
        Reset();
    }
    #endregion

    #region Private
    /// <summary>Assembles current atmosphere, calibration, material and mode inputs through their existing owners.</summary>
    private FrameLightingBounds Assemble(float compatibilityMaximum)
    {
        var atmosphere = AtmosphereModSystem.Lighting
            ?? throw new InvalidOperationException("Physical atmosphere has not been published.");
        var frame = VgeFrameRenderer.Current;
        // Atmospheric transport has its own fixed solar calibration. The frame's
        // changing identity belongs to engine block/point conversion; neither may
        // relabel the other's data. RequireCurrent retains the exact atmosphere snapshot.
        var indirect = ConfigModSystem.Config.LumOn;
        if (indirect.IndirectTint is not { Length: >= 3 } tint)
            throw new InvalidOperationException("Indirect tint is incomplete.");
        float emission = Math.Max(LightingCalibration.EmissionReferenceNits,
            MaterialAtlasSystem.Instance.TextureStore.MaximumEmissionNits);
        return FrameLightingBounds.FromPublications(atmosphere, VgeLightsRenderer.Current,
            frame.LightingCalibration, PbrShaderLightingMode.LumOnEnabled, indirect.Intensity,
            new Vector3(tint[0], tint[1], tint[2]), emission,
            LightingStorage.UpperBound((double)compatibilityMaximum + Atmosphere.Celestial.CelestialRenderer.MaximumNits
                + Atmosphere.Celestial.MoonRenderer.MaximumNits
                + Clouds.CloudNativeRuntime.SimpleMaximum(atmosphere, compatibilityMaximum)
                + Clouds.CloudNativeRuntime.VolumeMaximum(atmosphere, compatibilityMaximum)));
    }

    /// <summary>Rejects missing, retired and differently framed source metadata.</summary>
    private static SceneLightingPublicationRenderer RequireCurrent()
    {
        if (active is not { disposed: false, compatibility: { } publication } owner
            || publication.FrameIndex != VgeFrameRenderer.Current.FrameIndex
            || owner.calibrationGeneration != VgeFrameRenderer.Current.CalibrationGeneration
            || owner.lumOnEnabled != PbrShaderLightingMode.LumOnEnabled
            || !ReferenceEquals(owner.atmosphere, AtmosphereModSystem.Lighting))
            throw new InvalidOperationException("Physical scene source metadata is unavailable for this draw.");
        return owner;
    }

    /// <summary>Returns the maximum artistic GI gain independently of other source envelopes.</summary>
    private static double CurrentIndirectGain()
    {
        if (!PbrShaderLightingMode.LumOnEnabled) return 0;
        var indirect = ConfigModSystem.Config.LumOn;
        if (indirect.IndirectTint is not { Length: >= 3 } tint)
            throw new InvalidOperationException("Indirect tint is incomplete.");
        return (double)indirect.Intensity * Math.Max(tint[0], Math.Max(tint[1], tint[2]));
    }

    /// <summary>Withdraws publication without retaining transient scale or compatibility history across lifetimes.</summary>
    private void Reset() => compatibility = null;
    /// <summary>Requires a fresh publication after executable recreation.</summary>
    private bool Reload() { Reset(); return true; }
    #endregion
}
