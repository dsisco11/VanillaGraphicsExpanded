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
    private HarmonyLib.Harmony? harmony;

    private bool? lastEnablePom;
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
        harmony.PatchAll();

        // Manually apply terrain material params texture binding patches (property setters).
        TerrainMaterialParamsTextureBindingHook.ApplyPatches(harmony, api.Logger.Notification);
        TerrainLumonSceneChunkSlotUniformBindingHook.ApplyPatches(harmony, api.Logger.Notification);

        // Preload OpenGL extension strings as early as possible (best-effort; requires a current GL context).
        api.Event.EnqueueMainThreadTask(
            () =>
            {
                GlExtensions.TryLoadExtensions();
                GpuSupport.TryInitialize();
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

        GlDebug.TrySuppressGroupDebugMessages();

        // Register GPU debug label renderers to wrap all VS render stages
        GpuDebugLabelManager.Register(api);

        GlGpuProfiler.Instance.Initialize(api);
        gpuProfilerRenderer = new GlGpuProfilerRenderer(api);

        // Single, always-available debug view entry point.
        api.Input.RegisterHotKey(
            "vgedebugview",
            "VGE Debug Viewer",
            GlKeys.F8,
            HotkeyType.DevTool);
        api.Input.SetHotKeyHandler("vgedebugview", VgeDebugViewerManager.ToggleDialog);

        // Create G-buffer manager (Harmony hooks will call into this)
        gBufferManager = new GBufferManager(api);

        // Ensure all VGE memory shader programs are registered before any renderer can request them.
        // ShaderRegistry.getProgramByName() may attempt to create/load programs on demand if missing,
        // which can lead to engine-side NREs when stage instances are null.
        LoadShaders(api);
        api.Event.ReloadShader += OnReloadShader;
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.LeaveWorld += OnLeaveWorld;

        ConfigModSystem.Config.Sanitize();

        // Track config values that require shader recompilation when changed.
        lastEnablePom = ConfigModSystem.Config.MaterialAtlas.EnableParallaxOcclusionMapping;
        lastEnableNormalMaps = ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps;
        lastNormalMapScale = ConfigModSystem.Config.MaterialAtlas.NormalMapScale;

        // Register built-in debug views for the unified debug viewer.
        VgeBuiltInDebugViews.RegisterAll(api, gBufferManager);

        // PBR (direct lighting + composite) is managed by PbrModSystem.
        api.ModLoader.GetModSystem<PbrModSystem>().SetDependencies(api, gBufferManager);

        // Initialize the debug viewer manager (GUI)
        VgeDebugViewerManager.Initialize(api);
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (capi is null) return;

        bool enablePom = ConfigModSystem.Config.MaterialAtlas.EnableParallaxOcclusionMapping;
        bool enableNormalMaps = ConfigModSystem.Config.MaterialAtlas.EnableNormalMaps;
        float normalMapScale = ConfigModSystem.Config.MaterialAtlas.NormalMapScale;

        bool shaderReloadNeeded = lastEnablePom.HasValue && lastEnablePom.Value != enablePom;
        shaderReloadNeeded |= lastEnableNormalMaps.HasValue && lastEnableNormalMaps.Value != enableNormalMaps;
        shaderReloadNeeded |= lastNormalMapScale.HasValue && Math.Abs(lastNormalMapScale.Value - normalMapScale) > 0.0001f;

        lastEnablePom = enablePom;
        lastEnableNormalMaps = enableNormalMaps;
        lastNormalMapScale = normalMapScale;

        if (!liveShaderReloadReady)
        {
            // ConfigLib emits initial setting-loaded events during startup. They establish
            // the current config but must not trigger a global shader reload while the
            // engine is still constructing/loading its GUI shaders.
            return;
        }

        capi.Logger.Debug(
            "[VGE] Live config reloaded: LumOn={0}, POM={1}, NormalMaps={2}, NormalMapScale={3}; shaderReload={4}",
            ConfigModSystem.Config.LumOn.Enabled,
            enablePom,
            enableNormalMaps,
            normalMapScale,
            shaderReloadNeeded);

        if (!shaderReloadNeeded) return;

        capi.Logger.Notification(
            "[VGE] Compile-time shader settings changed (POM={0}, NormalMaps={1}, NormalMapScale={2}). Re-enter the world or restart to apply.",
            enablePom,
            enableNormalMaps,
            normalMapScale);

        pendingShaderReload = true;
        TryReloadShadersInWorld();
    }

    private void OnLevelFinalize()
    {
        liveShaderReloadReady = true;
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
        if (!memoryShaderRegistrationQueued)
        {
            memoryShaderRegistrationQueued = true;
            capi.Event.EnqueueMainThreadTask(
                () =>
                {
                    memoryShaderRegistrationQueued = false;
                    LoadShaders(capi);
                },
                "vge-register-memory-shaders-after-reload");
        }

        return true;
    }

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

                pendingShaderReload = false;
                TerrainMaterialParamsTextureBindingHook.ClearUniformCache();
                TerrainLumonSceneChunkSlotUniformBindingHook.ClearUniformCache();

                bool ok = capi.Shader.ReloadShaders();
                capi.Logger.Notification("[VGE] Shaders reloaded after live compile-time config change. ok={0}", ok);
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

            // Unregister GPU debug label renderers
            if (capi != null)
            {
                GpuDebugLabelManager.Unregister(capi);
            }

            gpuProfilerRenderer?.Dispose();
            gpuProfilerRenderer = null;

            GlGpuProfiler.Instance.Dispose();

            gBufferManager?.Dispose();
            gBufferManager = null;

        }
        finally
        {
            // Unpatch Harmony patches
            harmony?.UnpatchAll(Constants.ModId);
            harmony = null;
        }
    }

    private static bool LoadShaders(ICoreClientAPI api)
    {
        // General-purpose debug line shader (C#-rendered overlays).
        VgeDebugLinesShaderProgram.Register(api);
        VgeWorldProbeOrbsPointsShaderProgram.Register(api);

        // PBR direct lighting shader
        PBRDirectLightingShaderProgram.Register(api);

        // PBR final composite shader
        PBRCompositeShaderProgram.Register(api);

        // Phase 18.7: World-probe clipmap resolve (CPU -> GPU textures)
        LumOnWorldProbeClipmapResolveShaderProgram.Register(api);
        LumOnWorldProbeRadianceTileResolveShaderProgram.Register(api);

        // LumOn shaders
        LumOnProbeAnchorShaderProgram.Register(api);
        LumOnProbeAtlasPisMaskShaderProgram.Register(api);
        LumOnHzbCopyShaderProgram.Register(api);
        LumOnHzbDownsampleShaderProgram.Register(api);
        LumOnScreenProbeAtlasTraceShaderProgram.Register(api);
        LumOnVelocityShaderProgram.Register(api);
        LumOnScreenProbeAtlasTemporalShaderProgram.Register(api);
        LumOnScreenProbeAtlasFilterShaderProgram.Register(api);
        LumOnScreenProbeAtlasProjectSh9ShaderProgram.Register(api);
        LumOnProbeSh9GatherShaderProgram.Register(api);
        LumOnScreenProbeAtlasGatherShaderProgram.Register(api);
        LumOnUpsampleShaderProgram.Register(api);
        LumOnCombineShaderProgram.Register(api);
        LumOnDebugShaderProgram.Register(api);

        return true;
    }
}
