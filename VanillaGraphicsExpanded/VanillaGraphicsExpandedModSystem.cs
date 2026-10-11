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
    private GlGpuProfilerRenderer? gpuProfilerRenderer;
    private VgeFrameRenderer? frameRenderer;
    private VgeLightsRenderer? lightsRenderer;
    private HarmonyLib.Harmony? harmony;

    private TerrainReliefConfiguration? lastSurfaceDetail;
    private bool? lastLumOnEnabled;
    private bool? lastEnableNormalMaps;
    private float? lastNormalMapScale;
    private bool pendingShaderReload;
    private bool shaderReloadQueued;
    private bool memoryShaderRegistrationQueued;
    private bool liveShaderReloadReady;


    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        // Apply Harmony patches as early as possible, especially before shaders are loaded.
        harmony = new HarmonyLib.Harmony(Constants.ModId);
        PBR.Tessellation.TerrainTessellationPrograms.Log = message => api.Logger.Warning(message);
        harmony.PatchAll();
        // Rebuild render API callers only after their native state callees have been routed.
        EngineRenderApiStatePatches.Apply(harmony);
        LiquidPoolInputBridgeHook.Install(harmony);

        // Atlas binding is injected by the renderer transpiler; retain frame-level mapping refresh.
        TerrainLumonSceneChunkSlotUniformBindingHook.ApplyPatches(harmony, api.Logger.Notification);
        FrameShaderBindingHook.ApplyPatches(harmony, api.Logger.Notification);

        // Preload OpenGL extension strings as early as possible (best-effort; requires a current GL context).
        api.Event.EnqueueMainThreadTask(
            () =>
            {
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
        // Begin renderer ownership with unknown state; later external mutations invalidate their affected categories.
        StateCache.Current.InvalidateAll();
        // Establish HDR storage before any VGE system borrows the engine framebuffer images.
        PBR.SceneColor.SceneColorPipeline.InitializeStorage(api);
        // Name the current engine table before VGE adds its own attachments.
        Rendering.Diagnostics.EngineFramebufferDebugLabels.ApplyDefaults(api.Render.FrameBuffers);
        PBR.HeldLighting.HeldLightSystem.Start(api, message => api.Logger.Error(message));

        GlGpuProfiler.Instance.Initialize(api);
        gpuProfilerRenderer = new GlGpuProfilerRenderer(api);
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
        shaderReloadQueued = false;
    }

    private bool OnReloadShader()
    {
        if (capi is null)
        {
            return true;
        }

        TerrainMaterialParamsTextureBindingHook.ClearUniformCache();
        TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();
        FrameShaderBindingHook.ClearUniformCache();
        if (!memoryShaderRegistrationQueued)
        {
            memoryShaderRegistrationQueued = true;
            capi.Event.EnqueueMainThreadTask(
                () =>
                {
                    memoryShaderRegistrationQueued = false;
                    VgeShaderPrograms.RegisterAll(capi);
                },
                "vge-register-memory-shaders-after-reload");
        }

        return true;
    }

    /// <summary>Coalesces shader changes onto the main thread without reloading during GUI initialization.</summary>
    private void TryReloadShadersInWorld()
    {
        if (!pendingShaderReload || capi?.World?.Player is null || shaderReloadQueued)
        {
            return;
        }

        shaderReloadQueued = true;
        capi.Event.EnqueueMainThreadTask(
            () =>
            {
                shaderReloadQueued = false;
                if (capi.World?.Player is null)
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
            },
            "vge-reload-shaders-on-config-change");
    }

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            if (capi is not null)
            {
                capi.Event.ReloadShader -= OnReloadShader;
                capi.Event.LevelFinalize -= OnLevelFinalize;
                capi.Event.LeaveWorld -= OnLeaveWorld;
            }

            VgeDebugViewerManager.Dispose();
            if (capi != null)
            {
                LumOnDebugShaderProgramFamily.Dispose(capi);
                GpuShaderPrograms.Dispose(capi);
            }


            gpuProfilerRenderer?.Dispose();
            gpuProfilerRenderer = null;
            lightsRenderer?.Dispose();
            lightsRenderer = null;
            frameRenderer?.Dispose();
            frameRenderer = null;

            GlGpuProfiler.Instance.Dispose();

            gBufferManager?.Dispose();
            gBufferManager = null;

        }
        finally
        {
            // Unpatch Harmony patches
            PBR.HeldLighting.HeldLightSystem.Stop();
            harmony?.UnpatchAll(Constants.ModId);
            harmony = null;
            PBR.Tessellation.TerrainTessellationPrograms.Log = null;
        }
    }

}
