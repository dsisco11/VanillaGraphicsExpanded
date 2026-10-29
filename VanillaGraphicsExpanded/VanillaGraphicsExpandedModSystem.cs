using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Profiling;

using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.Client.NoObf;
using System;

namespace VanillaGraphicsExpanded;

public sealed class VanillaGraphicsExpandedModSystem : ModSystem, ILiveConfigurable
{
    private ICoreClientAPI? capi;
    private GBufferManager? gBufferManager;
#if DEBUG
    private GlGpuProfilerRenderer? gpuProfilerRenderer;
#endif
    private VgeFrameRenderer? frameRenderer;
    private VgeLightsRenderer? lightsRenderer;
    private HarmonyLib.Harmony? harmony;

    private TerrainReliefConfiguration? lastSurfaceDetail;
    private bool? lastLumOnEnabled;
    private bool? lastEnableNormalMaps;
    private float? lastNormalMapScale;
    private bool pendingShaderReload;
    private long? queuedShaderReloadEpoch;
    private Rendering.Integration.ClientRenderingLifetime? renderingLifetime;
    private bool disposed;
    private bool liveShaderReloadReady;


    /// <summary>Composes rendering ownership after configuration and before default-order feature systems.</summary>
    public override double ExecuteOrder() => 0.05;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        // Apply Harmony patches as early as possible, especially before shaders are loaded.
        harmony = new HarmonyLib.Harmony(Constants.ModId);
        PBR.Tessellation.TerrainTessellationPrograms.Log = message => api.Logger.Warning(message);
        harmony.PatchAll();
        if (!OpenGLBindingInterception.ObservationEnabled && api is ICoreClientAPI client
            && client.Render?.FrameBuffers is { } framebuffers
            && (Rendering.Integration.RenderContextRegistry.Current() != default
                || Rendering.Integration.EngineRenderContext.TryRegisterCurrent()))
            Rendering.Integration.EnginePreexistingImages.Record(RHIResourceManagerSystem.EnsureCurrentContext(), framebuffers);
        OpenGLBindingInterception.Install();
        LiquidPoolInputBridgeHook.Install(harmony);

        // Atlas binding is injected by the renderer transpiler; retain frame-level mapping refresh.
        TerrainLumonSceneChunkSlotUniformBindingHook.ApplyPatches(harmony, api.Logger.Notification);
        FrameShaderBindingHook.ApplyPatches(harmony, api.Logger.Notification);

        // Preload OpenGL extension strings as early as possible (best-effort; requires a current GL context).
        api.Event.EnqueueMainThreadTask(
            () =>
            {
                if (disposed) return;
                try { Rendering.Integration.EngineRenderContext.RegisterCurrent(); }
                catch (InvalidOperationException) { return; } // The optional preload may precede window availability.
                GlExtensions.TryLoadExtensions();
                GpuSupport.TryInitialize();
                GlDebug.TrySuppressGroupDebugMessages();
            },
            "vge-load-gl-extensions");
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Shader pipeline setup is owned by ShaderModSystem.
    }

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;
        Rendering.Integration.EngineRenderContext.RegisterCurrent();
        var registry = GpuShaderProgramLibraries.Create(api);
        renderingLifetime = Rendering.Integration.ClientRenderingLifetime.Find(api);
        capi.ModLoader.GetModSystem<RHIResourceManagerModSystem>()?.AdoptByRenderingRoot();
        renderingLifetime!.Attach(DisposeRendering);
        // Establish HDR storage before any VGE system borrows the engine framebuffer images.
        PBR.SceneColor.SceneColorPipeline.InitializeStorage(api);
        // Name the current engine table before VGE adds its own attachments.
        Rendering.RHI.Backends.OpenGL.EngineFramebufferDebugLabels.ApplyDefaults(api.Render.FrameBuffers);
        PBR.HeldLighting.HeldLightSystem.Start(api, message => api.Logger.Error(message));

#if DEBUG
        GlGpuProfiler.Instance.Initialize(api);
#endif
#if DEBUG
        gpuProfilerRenderer = new GlGpuProfilerRenderer(api);
#endif
        frameRenderer = new VgeFrameRenderer(api);
        lightsRenderer = new VgeLightsRenderer(api);

        // Single, always-available debug view entry point.
        api.Input.RegisterHotKey(
            "vgedebugview",
            "VGE Debug Viewer",
            GlKeys.F8,
            HotkeyType.DevTool);
        api.Input.SetHotKeyHandler("vgedebugview", VgeDebugViewerManager.ToggleDialog);

        // Create G-buffer manager (Harmony hooks will call into this)
        gBufferManager = new GBufferManager(api);

        // Declare shader owners; rendering systems explicitly preload or prepare their required selections.
        // ShaderRegistry.getProgramByName() may attempt to create/load programs on demand if missing,
        // which can lead to engine-side NREs when stage instances are null.
        VgeShaderPrograms.RegisterAll(api);
        api.Event.ReloadShader += OnReloadShader;
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += OnLeaveWorld;

        ConfigModSystem.Config.Sanitize();

        // Track config values that require shader recompilation when changed.
        lastLumOnEnabled = ConfigModSystem.Config.LumOn.Enabled;
        lastSurfaceDetail = TerrainReliefConfiguration.Capture(ConfigModSystem.Config.MaterialAtlas);
        lastEnableNormalMaps = ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps;
        lastNormalMapScale = ConfigModSystem.Config.MaterialAtlas.NormalMapScale;

        // Register built-in debug views for the unified debug viewer.
        VgeBuiltInDebugViews.RegisterAll(api, gBufferManager);

        // PBR (direct lighting + composite) is managed by PbrModSystem.
        api.ModLoader.GetModSystem<PbrModSystem>().SetDependencies(api, gBufferManager);

        // Initialize the debug viewer manager (GUI)
        VgeDebugViewerManager.Initialize(api);
    }

    /// <summary>Queues engine recompilation only when compile-time settings change.</summary>
    public void OnConfigReloaded(ICoreAPI api)
    {
        if (capi is null) return;

        bool enablePom = (ConfigModSystem.Config.MaterialAtlas.TerrainSurfaceDetailMode == (int)VanillaGraphicsExpanded.PBR.Materials.TerrainSurfaceDetailMode.Relief);
        bool lumOnEnabled = ConfigModSystem.Config.LumOn.Enabled;
        bool enableNormalMaps = ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps;
        float normalMapScale = ConfigModSystem.Config.MaterialAtlas.NormalMapScale;

        var surfaceDetail = TerrainReliefConfiguration.Capture(ConfigModSystem.Config.MaterialAtlas);
        bool shaderReloadNeeded = lastSurfaceDetail.HasValue && lastSurfaceDetail.Value != surfaceDetail;
        shaderReloadNeeded |= lastLumOnEnabled.HasValue && lastLumOnEnabled.Value != lumOnEnabled;
        shaderReloadNeeded |= lastEnableNormalMaps.HasValue && lastEnableNormalMaps.Value != enableNormalMaps;
        shaderReloadNeeded |= lastNormalMapScale.HasValue && Math.Abs(lastNormalMapScale.Value - normalMapScale) > 0.0001f;

        lastSurfaceDetail = surfaceDetail;
        lastLumOnEnabled = lumOnEnabled;
        lastEnableNormalMaps = enableNormalMaps;
        lastNormalMapScale = normalMapScale;

        if (!liveShaderReloadReady)
        {
            // ConfigLib emits initial setting-loaded events during startup. They establish
            // the current config but must not trigger a global shader reload while the
            // engine is still constructing/loading its GUI shaders.
            pendingShaderReload |= PbrShaderLightingMode.GenerationLumOnEnabled is bool compiledMode
                && compiledMode != lumOnEnabled;
            return;
        }

        capi.Logger.Debug(
            "[VGE] Live config reloaded: LumOn={0}, POM={1}, NormalMaps={2}, NormalMapScale={3}; shaderReload={4}",
            ConfigModSystem.Config.LumOn.Enabled,
            enablePom,
            enableNormalMaps,
            normalMapScale,
            shaderReloadNeeded);

        if (!shaderReloadNeeded && !pendingShaderReload) return;

        capi.Logger.Notification(
            "[VGE] Compile-time shader settings changed (POM={0}, NormalMaps={1}, NormalMapScale={2}, LumOn={3}); scheduling shader reload.",
            enablePom,
            enableNormalMaps,
            normalMapScale,
            lumOnEnabled);

        pendingShaderReload = true;
        TryReloadShadersInWorld();
    }

    /// <summary>Applies deferred compile-time changes once world shader reload is safe.</summary>
    private void OnLevelFinalize()
    {
        liveShaderReloadReady = true;
        pendingShaderReload |= PbrShaderLightingMode.GenerationLumOnEnabled is bool compiledMode
            && compiledMode != ConfigModSystem.Config.LumOn.Enabled;
        TryReloadShadersInWorld();
    }

    private void OnLeaveWorld()
    {
        liveShaderReloadReady = false;
        pendingShaderReload = false;
        queuedShaderReloadEpoch = null;
        renderingLifetime?.LeaveWorld();
    }

    private bool OnReloadShader()
    {
        if (disposed || capi is null)
        {
            return true;
        }

        TerrainMaterialParamsTextureBindingHook.ClearUniformCache();
        TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
        FrameShaderBindingHook.ClearUniformCache();

        if (renderingLifetime?.ReloadFailure is { } failure)
            capi.Logger.Error("[VGE] Native reload returned; VGE preparation/publication failure: {0}", failure);
        return renderingLifetime?.ReloadFailure is null;
    }

    /// <summary>Coalesces shader changes onto the main thread without reloading during GUI initialization.</summary>
    private void TryReloadShadersInWorld()
    {
        if (!pendingShaderReload || capi?.World?.Player is null || queuedShaderReloadEpoch == renderingLifetime?.Epoch)
        {
            return;
        }

        long epoch = renderingLifetime?.Epoch ?? -1;
        queuedShaderReloadEpoch = epoch;
        capi.Event.EnqueueMainThreadTask(
            renderingLifetime!.GuardWorldCallback(() =>
            {
                if (disposed || renderingLifetime?.IsClosed == true || renderingLifetime?.Epoch != epoch) return;
                queuedShaderReloadEpoch = null;
                if (capi!.World?.Player is null)
                {
                    return;
                }

                if (ShaderRegistry.SupressShaderAndBufferReloads)
                {
                    // Keep the request pending for the next config event or world-finalize callback.
                    capi.Logger.Warning("[VGE] Shader reload deferred while engine reloads are suppressed.");
                    return;
                }

                pendingShaderReload = false;
                TerrainMaterialParamsTextureBindingHook.ClearUniformCache();
                TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
                FrameShaderBindingHook.ClearUniformCache();

                bool ok = capi.Shader.ReloadShaders();
                if (ok)
                    capi.Logger.Notification("[VGE] Shaders reloaded after live compile-time config change.");
                else
                    capi.Logger.Error("[VGE] Shader reload failed. The engine discarded the previous programs; check shader errors and reload again after correcting them.");
            }),
            "vge-reload-shaders-on-config-change");
    }

    /// <summary>Requests coordinated rendering teardown before the engine disposes this mod system.</summary>
    public override void Dispose()
    {
        if (renderingLifetime is not null) renderingLifetime.Shutdown();
        else DisposeRendering();
    }

    /// <summary>Retires feature borrowers, shared programs and the deletion manager in explicit dependency order.</summary>
    private void DisposeRendering()
    {
        if (disposed) return;
        disposed = true;
        base.Dispose();
        var cleanupFailures = new System.Collections.Generic.List<Exception>();
        try
        {
            if (capi is not null)
            {
                TryCleanup(() => capi.Event.ReloadShader -= OnReloadShader, cleanupFailures);
                TryCleanup(() => capi.Event.LevelFinalize -= OnLevelFinalize, cleanupFailures);
                TryCleanup(() => capi.Event.LeaveWorld -= OnLeaveWorld, cleanupFailures);
            }

            // Feature borrowers and compute owners retire before shared graphics declarations.
            if (capi is not null)
            {
                TryCleanup(() => capi.ModLoader.GetModSystem<PbrModSystem>()?.Dispose(), cleanupFailures);
                TryCleanup(() => capi.ModLoader.GetModSystem<LumOnModSystem>()?.Dispose(), cleanupFailures);
                TryCleanup(() => capi.ModLoader.GetModSystem<WorldProbeModSystem>()?.Dispose(), cleanupFailures);
                TryCleanup(() => capi.ModLoader.GetModSystem<AtmosphereModSystem>()?.Dispose(), cleanupFailures);
            }
            TryCleanup(() => VgeDebugViewerManager.Dispose(), cleanupFailures);



#if DEBUG
            TryCleanup(() => gpuProfilerRenderer?.Dispose(), cleanupFailures);
#endif
#if DEBUG
            gpuProfilerRenderer = null;
#endif
            TryCleanup(() => lightsRenderer?.Dispose(), cleanupFailures);
            lightsRenderer = null;
            TryCleanup(() => frameRenderer?.Dispose(), cleanupFailures);
            frameRenderer = null;

#if DEBUG
            TryCleanup(() => GlGpuProfiler.Instance.Dispose(), cleanupFailures);
#endif

            if (capi != null)
            {
                TryCleanup(() => LumOnDebugShaderProgramFamily.Dispose(capi), cleanupFailures);
                TryCleanup(() => GpuShaderProgramLibraries.Dispose(capi), cleanupFailures);
            }

            TryCleanup(() => gBufferManager?.Dispose(), cleanupFailures);
            gBufferManager = null;
            TryCleanup(() => capi?.ModLoader.GetModSystem<RHIResourceManagerModSystem>()?.ShutdownRenderingResources(), cleanupFailures);

        }
        finally
        {
            OpenGLBindingInterception.Uninstall();
            // Unpatch Harmony patches
            TryCleanup(PBR.HeldLighting.HeldLightSystem.Stop, cleanupFailures);
            TryCleanup(() => harmony?.UnpatchAll(Constants.ModId), cleanupFailures);
            harmony = null;
            PBR.Tessellation.TerrainTessellationPrograms.Log = null;
        }
        if (cleanupFailures.Count != 0) throw new AggregateException("VGE rendering cleanup failed.", cleanupFailures);
    }
    /// <summary>Attempts each independent teardown owner so one failure cannot prevent later native cleanup.</summary>
    private static void TryCleanup(Action cleanup, System.Collections.Generic.List<Exception> failures)
    {
        try { cleanup(); } catch (Exception error) { failures.Add(error); }
    }

}
