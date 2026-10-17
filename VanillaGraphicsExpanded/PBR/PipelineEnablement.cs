using System;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Postprocessing.LocalExposure;
using VanillaGraphicsExpanded.Rendering.Enablement;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Composes persistent rendering demand from shared native settings, committed configuration and active feature lifetimes.</summary>
internal sealed class PipelineEnablement : IDisposable
{
    private readonly ICoreClientAPI api;
    private readonly RenderThreadDispatch dispatch;
    private readonly CompositeDisposable lifetime = new();
    private readonly BehaviorSubject<Configuration> configuration;
    private readonly BehaviorSubject<bool> lumOn, clouds;
    private bool disposed;

    #region Public API
    #region Values
    /// <summary>Exposes cached native settings before applying the transient screen-pass gate.</summary>
    internal CachedObservable<EnginePostprocessInputs> NativePostprocess { get; }
    /// <summary>Exposes one atomic, sanitized VGE postprocessing configuration.</summary>
    internal CachedObservable<Configuration> Settings { get; }
    /// <summary>Enables the shared depth producer while at least one persistent consumer requires it.</summary>
    internal CachedObservable<bool> DepthHierarchy { get; }
    /// <summary>Tracks native ambient-occlusion demand.</summary>
    internal CachedObservable<bool> AmbientOcclusion { get; }
    /// <summary>Tracks native shaft visibility demand independently of radiance strength.</summary>
    internal CachedObservable<bool> LightShaftOcclusion { get; }
    /// <summary>Tracks combined native and VGE bloom demand.</summary>
    internal CachedObservable<bool> Bloom { get; }
    /// <summary>Tracks combined native and VGE shaft radiance demand.</summary>
    internal CachedObservable<bool> LightShaftRadiance { get; }
    /// <summary>Tracks the spatial exposure feature's committed configuration.</summary>
    internal CachedObservable<bool> LocalExposure { get; }
    #endregion

    #region Lifetime
    /// <summary>Connects sources once and publishes initial conditions before renderers bind to them.</summary>
    internal PipelineEnablement(ICoreClientAPI api)
    {
        this.api = api;
        dispatch = new(api);
        // Resolve the lazy shader generation before attaching its change observer.
        lumOn = new(PbrShaderLightingMode.LumOnEnabled);
        clouds = new(CloudNativeRuntime.Active?.Ownership.Owns(CloudMode.Volumetric) == true);
        configuration = new(CaptureConfiguration());
        Settings = Own(new CachedObservable<Configuration>(configuration));
        var ssao = Own(new CachedObservable<int>(NativeSettingObservable.Observe(api.Settings.Int, "ssaoQuality", dispatch)));
        var shafts = Own(new CachedObservable<int>(NativeSettingObservable.Observe(api.Settings.Int, "godRays", dispatch)));
        var bloom = Own(new CachedObservable<bool>(NativeSettingObservable.Observe(api.Settings.Bool, "bloom", dispatch)));
        var fxaa = Own(new CachedObservable<bool>(NativeSettingObservable.Observe(api.Settings.Bool, "fxaa", dispatch)));
        NativePostprocess = Own(new CachedObservable<EnginePostprocessInputs>(Observable.CombineLatest(
            ssao, shafts, bloom, fxaa, (ao, rays, glare, aa) =>
                new EnginePostprocessInputs(glare, rays > 0, ao > 0, aa, ao, Math.Clamp(rays, 0, 3)))));
        AmbientOcclusion = Own(new CachedObservable<bool>(ssao.Select(value => value > 0)));
        LightShaftOcclusion = Own(new CachedObservable<bool>(shafts.Select(value => value > 0)));
        DepthHierarchy = Own(new CachedObservable<bool>(Observable.CombineLatest(AmbientOcclusion, lumOn, clouds,
            (ao, lighting, volume) => ao || lighting || volume)));
        Bloom = Own(new CachedObservable<bool>(Observable.CombineLatest(bloom, Settings,
            (native, config) => native && config.Postprocess.BloomStrength > 0)));
        LightShaftRadiance = Own(new CachedObservable<bool>(Observable.CombineLatest(LightShaftOcclusion, Settings,
            (native, config) => native && config.Postprocess.LightShaftStrength > 0 && config.Postprocess.LightShaftLimit > 0)));
        LocalExposure = Own(new CachedObservable<bool>(Settings.Select(value => value.LocalExposure.Enabled)));
        PbrShaderLightingMode.EnabledChanged += OnLightingChanged;
        CloudNativeRuntime.VolumeOwnershipChanged += OnCloudsChanged;
        LiveConfigReload.Applied += OnConfigurationApplied;
    }

    /// <summary>Detaches producers and cancels queued notifications before disconnecting all cached conditions.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        dispatch.Dispose();
        PbrShaderLightingMode.EnabledChanged -= OnLightingChanged;
        CloudNativeRuntime.VolumeOwnershipChanged -= OnCloudsChanged;
        LiveConfigReload.Applied -= OnConfigurationApplied;
        lifetime.Dispose();
        configuration.Dispose(); lumOn.Dispose(); clouds.Dispose();
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Registers each connected observable under the pipeline's explicit subscription lifetime.</summary>
    private T Own<T>(T owner) where T : IDisposable
    {
        lifetime.Add(owner);
        return owner;
    }

    /// <summary>Captures related postprocessing fields together so derived conditions see a complete update.</summary>
    private static Configuration CaptureConfiguration()
    {
        var settings = ConfigModSystem.Config.Postprocessing;
        return new(settings.Snapshot(), (settings.LocalExposure ?? new()).Snapshot());
    }

    /// <summary>Publishes the active shader mode on its owning thread, independently of pending configuration.</summary>
    private void OnLightingChanged()
    {
        bool enabled = PbrShaderLightingMode.LumOnEnabled;
        dispatch.Run(() => lumOn.OnNext(enabled));
    }

    /// <summary>Publishes actual volume admission or release, rather than the user's requested cloud mode.</summary>
    private void OnCloudsChanged()
    {
        bool owned = CloudNativeRuntime.Active?.Ownership.Owns(CloudMode.Volumetric) == true;
        dispatch.Run(() => clouds.OnNext(owned));
    }

    /// <summary>Marshals a complete configuration snapshot only for this client API lifetime.</summary>
    private void OnConfigurationApplied(ICoreAPI changedApi)
    {
        if (!ReferenceEquals(api, changedApi)) return;
        var snapshot = CaptureConfiguration();
        dispatch.Run(() => configuration.OnNext(snapshot));
    }

    /// <summary>Groups all VGE inputs which must commit together before enablement predicates run.</summary>
    internal readonly record struct Configuration(PostprocessParameters Postprocess, LocalExposureParameters LocalExposure);
    #endregion
}
