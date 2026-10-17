using System;
using System.Threading;
using FluffyClouds;
using HarmonyLib;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns world-scoped map borrowing and selected draw admission while native owners retain registration and resource lifetimes.</summary>
internal sealed class CloudNativeRuntime : IDisposable
{
    private static readonly AccessTools.FieldRef<FluffyCloudsModSystem, CloudRendererMap> NativeMap = AccessTools.FieldRefAccess<FluffyCloudsModSystem, CloudRendererMap>("map");
    private static readonly AccessTools.FieldRef<FluffyCloudsModSystem, CloudRendererVolumetric> NativeVolume = AccessTools.FieldRefAccess<FluffyCloudsModSystem, CloudRendererVolumetric>("volumetric");
    private static readonly AccessTools.FieldRef<WeatherSystemClient, CloudRenderer> NativeSimple = AccessTools.FieldRefAccess<WeatherSystemClient, CloudRenderer>("cloudRenderer");
    private CloudSimpleRenderer? simple;
    private CloudVolumeRenderer? volume;
    private static long nextWorld;
    private readonly ICoreClientAPI api;
    private readonly IDisposable reload;
    private CloudRendererMap? mapOwner;
    private CloudNativeMapAdapter? map;
    private WeakReference<CloudRendererMap>? previousWorldMap;
    private WeakReference<CloudRenderer>? previousWorldSimple;
    private (nint Handle, long Generation) context;
    private bool worldReady, disposed;
    private long world;

    #region Public API
    #region Lifecycle
    /// <summary>Routes native producer hooks to the current client lifetime.</summary>
    internal static CloudNativeRuntime? Active { get; private set; }
    /// <summary>Signals changes to the active client's actual volumetric consumer ownership.</summary>
    internal static event Action? VolumeOwnershipChanged;
    /// <summary>Remains empty until a functioning selected draw consumer transfers its owned resources.</summary>
    internal CloudModeOwnership Ownership { get; } = new();
    /// <summary>Identifies a world independently of reusable engine objects and GL names.</summary>
    internal long World => world;

    /// <summary>Uses existing world and reload events without changing native subscriptions.</summary>
    internal CloudNativeRuntime(ICoreClientAPI api)
    {
        this.api = api; Active = this;
        Ownership.VolumetricChanged += NotifyVolumeOwnershipChanged;
        api.Event.LevelFinalize += BeginWorld;
        api.Event.LeaveWorld += EndWorld;
        reload = EngineShaderReload.Register(api, WithdrawForReload);
        VolumeOwnershipChanged?.Invoke();
    }

    /// <summary>Reconciles context and current mode demand without allocating GPU resources or changing native registrations.</summary>
    internal void Update()
    {
        if (disposed || !worldReady) return;
        var current = RenderContextRegistry.Current();
        if (current != context)
        {
            DropInputs();
            try { Ownership.Dispose(); }
            finally { simple = null; volume = null; context = current; }
        }
        try { Ownership.Update(current != default, api.Settings.Int.Get("cloudRenderMode", 0)); }
        finally
        {
            // Mode release disposes the selected consumer. Retaining it here would let
            // a later reload reset a retired lifetime instead of just the live draw.
            if (!Ownership.Owns(CloudMode.Simple)) simple = null;
            if (!Ownership.Owns(CloudMode.Volumetric)) volume = null;
        }
    }

    /// <summary>Prepares the selected Simple consumer before Opaque lighting storage is frozen.</summary>
    internal void PrepareSimple()
    {
        if (!worldReady || disposed || context == default || api.Settings.Int.Get("cloudRenderMode", 0) != 2) return;
        if (Ownership.Owns(CloudMode.Simple)) { simple?.Prepare(); return; }
        var candidate = new CloudSimpleRenderer(api);
        try
        {
            if (candidate.Prepare() && Ownership.TryActivate(CloudMode.Simple, candidate, true)) { simple = candidate; return; }
        }
        catch (Exception error) { api.Logger.Error("[VGE] Simple cloud preparation unavailable. {0}", error); }
        candidate.Dispose();
    }

    /// <summary>Transfers a functioning independent volume draw before scene lighting storage is frozen.</summary>
    internal void PrepareVolume()
    {
        if (!worldReady || disposed || context == default || api.Settings.Int.Get("cloudRenderMode", 0) != 1) return;
        if (Ownership.Owns(CloudMode.Volumetric)) { volume?.Prepare(); return; }
        var candidate = new CloudVolumeRenderer(api, this);
        try
        {
            if (candidate.Prepare() && Ownership.TryActivate(CloudMode.Volumetric, candidate, true))
            {
                volume = candidate;
                api.Event.RegisterRenderer(candidate, EnumRenderStage.OIT, "vge_cloud_volume");
                return;
            }
        }
        catch (Exception error)
        {
            Ownership.Dispose(); volume = null;
            api.Logger.Error("[VGE] Volumetric cloud preparation unavailable. {0}", error);
        }
        candidate.Dispose();
    }

    /// <summary>Updates and draws the native Simple root at its existing OIT callback, suppressing only a committed replacement.</summary>
    internal bool RenderSimple(CloudRenderer owner, float deltaTime, EnumRenderStage stage)
    {
        Update();
        if (!worldReady || disposed || stage != EnumRenderStage.OIT || !Ownership.Owns(CloudMode.Simple) || simple is null) return false;
        var weather = api.ModLoader.GetModSystem<WeatherSystemClient>();
        if (weather is null || !ReferenceEquals(NativeSimple(weather), owner)) return false;
        if (previousWorldSimple is not null && previousWorldSimple.TryGetTarget(out var previous) && ReferenceEquals(previous, owner)) return false;
        CloudNativeSimpleAdapter.Render(api, owner, deltaTime, world, TryGetFrame(out uint frame) ? frame : null, simple.Draw);
        return true;
    }

    /// <summary>Includes the complete Simple response in frozen scene storage before native tile updates.</summary>
    internal static float SimpleMaximum(Atmosphere.AtmosphereLighting lighting, float localFogMaximum) =>
        Active is { } current && current.Ownership.Owns(CloudMode.Simple) ? CloudSimpleLighting.Maximum(lighting, localFogMaximum) : 0;

    /// <summary>Reserves the complete physical volume response independently of the later native map.</summary>
    internal static float VolumeMaximum(Atmosphere.AtmosphereLighting lighting, float localFogMaximum) =>
        Active is { } current && current.Ownership.Owns(CloudMode.Volumetric)
            ? CloudVolumeLighting.Maximum(lighting, localFogMaximum, current.volume?.Settings ?? CloudVolumeSettings.Default) : 0;

    /// <summary>Removes VGE subscriptions and consumers; native callbacks automatically resume through their draw gates.</summary>
    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        bool wasActive=ReferenceEquals(Active,this);
        if(wasActive) Active=null;
        RenderingCleanup.Run(
            () => { if(wasActive) VolumeOwnershipChanged?.Invoke(); },
            () => Ownership.VolumetricChanged -= NotifyVolumeOwnershipChanged,
            () => api.Event.LevelFinalize -= BeginWorld,
            () => api.Event.LeaveWorld -= EndWorld,
            reload.Dispose, EndWorld);
    }
    #endregion

    #region Borrowing and draw admission
    /// <summary>Acquires only the live native root after world creation; previous-world references cannot reacquire a borrow.</summary>
    internal CloudNativeMapAdapter? ForMap(CloudRendererMap owner)
    {
        if (!worldReady || disposed || context == default || RenderContextRegistry.Current() != context) return null;
        if (previousWorldMap is not null && previousWorldMap.TryGetTarget(out var previous) && ReferenceEquals(previous, owner)) return null;
        var native = api.ModLoader.GetModSystem<FluffyCloudsModSystem>();
        if (native is null || !ReferenceEquals(NativeMap(native), owner)) return null;
        if (!ReferenceEquals(mapOwner, owner)) { map?.Retire(); mapOwner = owner; map = new(owner, world); }
        return map;
    }

    /// <summary>Requires this client's published camera and current world/context for same-frame sampling.</summary>
    internal bool TryGetFrame(out uint frame)
    {
        frame = 0;
        return worldReady && !disposed && context != default && context == RenderContextRegistry.Current() &&
            VgeFrameRenderer.TryGetCurrentFrame(api, out frame);
    }

    /// <summary>Returns the completed native map only for the admitted main-view frame.</summary>
    internal bool TryGetMap(out CloudMapInput input)
    {
        input = default;
        return TryGetFrame(out uint frame) && mapOwner is not null && ForMap(mapOwner) is { } current &&
            current.Publication.TryGet(mapOwner, world, frame, out input);
    }

    /// <summary>Suppresses only the current native volume draw when its functioning VGE consumer owns the selected mode.</summary>
    internal bool SuppressVolume(CloudRendererVolumetric owner)
    {
        // A setting can change between Before and OIT. Reconcile here without observing
        // or altering the native settings watcher's renderer registrations.
        Update();
        if (!worldReady || disposed || !Ownership.Owns(CloudMode.Volumetric)) return false;
        var native = api.ModLoader.GetModSystem<FluffyCloudsModSystem>();
        return native is not null && ReferenceEquals(NativeVolume(native), owner);
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Forwards ownership transitions only from the active client lifetime.</summary>
    private void NotifyVolumeOwnershipChanged()
    {
        if(ReferenceEquals(Active,this)) VolumeOwnershipChanged?.Invoke();
    }
    /// <summary>Starts a fresh world after invalidating any previously observed world.</summary>
    private void BeginWorld() { EndWorld(); worldReady = true; }

    /// <summary>Uses native world teardown to revoke borrowed resources before another world can render.</summary>
    private void EndWorld()
    {
        if (worldReady)
        {
            var native = api.ModLoader.GetModSystem<FluffyCloudsModSystem>();
            var previous = native is null ? null : NativeMap(native);
            previousWorldMap = previous is null ? null : new(previous);
            var weather = api.ModLoader.GetModSystem<WeatherSystemClient>();
            var previousSimple = weather is null ? null : NativeSimple(weather);
            previousWorldSimple = previousSimple is null ? null : new(previousSimple);
        }
        worldReady = false; world = Interlocked.Increment(ref nextWorld);
        DropInputs();
        try { Ownership.Dispose(); }
        finally { context = default; simple = null; volume = null; }
    }

    /// <summary>Revokes certificates and drops strong native references without deleting native storage.</summary>
    private void DropInputs() { map?.Retire(); map = null; mapOwner = null; }

    /// <summary>Withdraws readiness immediately; the next producer callback validates the current linked program.</summary>
    private bool WithdrawForReload()
    {
        // Withdraw every dependent publication even if one resource cleanup fails.
        RenderingCleanup.Run(() => map?.InvalidateShader(), () => simple?.Reset(), () => volume?.Reset());
        return true;
    }
    #endregion
}
