using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Profiling;
using VanillaGraphicsExpanded.Rendering;
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


    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartPre(ICoreAPI api)
    {
        // Apply Harmony patches as early as possible, especially before shaders are loaded.
        harmony = new HarmonyLib.Harmony(Constants.ModId);
        harmony.PatchAll();

        // Manually apply terrain material params texture binding patches (property setters).
        TerrainMaterialParamsTextureBindingHook.ApplyPatches(harmony, api.Logger.Notification);
    }

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);

        // Initialize the shader imports system to load mod shader imports (shaders/includes)
        ShaderImportsSystem.Instance.Initialize(api);

        // Load PBR material definitions (config/vge/material_definitions.json)
        PbrMaterialRegistry.Instance.Initialize(api);
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
            "VGE Debug View",
            GlKeys.F8,
            HotkeyType.DevTool);
        api.Input.SetHotKeyHandler("vgedebugview", VgeDebugViewManager.ToggleDialog);

        // Create G-buffer manager (Harmony hooks will call into this)
        gBufferManager = new GBufferManager(api);

        // Ensure all VGE memory shader programs are registered before any renderer can request them.
        // ShaderRegistry.getProgramByName() may attempt to create/load programs on demand if missing,
        // which can lead to engine-side NREs when stage instances are null.
        LoadShaders(api);
        api.Event.ReloadShader += () =>
        {
            // Clear cached uniform locations before shader recompile.
            TerrainMaterialParamsTextureBindingHook.ClearUniformCache();
            return LoadShaders(api);
        };

        ConfigModSystem.Config.Sanitize();

        // PBR (direct lighting + composite) is managed by PbrModSystem.
        api.ModLoader.GetModSystem<PbrModSystem>().SetDependencies(api, gBufferManager);

        // Initialize the debug view manager (GUI)
        VgeDebugViewManager.Initialize(
            api,
            ConfigModSystem.Config);
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        // Intentionally empty: live config reload is handled by specialized mod systems.
    }

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            VgeDebugViewManager.Dispose();

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

            // Clear shader imports cache
            ShaderImportsSystem.Instance.Clear();

            // Clear material registry cache
            PbrMaterialRegistry.Instance.Clear();
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
        // PBR direct lighting shader
        PBRDirectLightingShaderProgram.Register(api);

        // PBR final composite shader
        PBRCompositeShaderProgram.Register(api);

        // Phase 18.7: World-probe clipmap resolve (CPU -> GPU textures)
        LumOnWorldProbeClipmapResolveShaderProgram.Register(api);

        // LumOn shaders
        LumOnProbeAnchorShaderProgram.Register(api);
        LumOnHzbCopyShaderProgram.Register(api);
        LumOnHzbDownsampleShaderProgram.Register(api);
        LumOnProbeTraceShaderProgram.Register(api);
        LumOnScreenProbeAtlasTraceShaderProgram.Register(api);
        LumOnVelocityShaderProgram.Register(api);
        LumOnTemporalShaderProgram.Register(api);
        LumOnScreenProbeAtlasTemporalShaderProgram.Register(api);
        LumOnScreenProbeAtlasFilterShaderProgram.Register(api);
        LumOnScreenProbeAtlasProjectSh9ShaderProgram.Register(api);
        LumOnGatherShaderProgram.Register(api);
        LumOnProbeSh9GatherShaderProgram.Register(api);
        LumOnScreenProbeAtlasGatherShaderProgram.Register(api);
        LumOnUpsampleShaderProgram.Register(api);
        LumOnCombineShaderProgram.Register(api);
        LumOnDebugShaderProgram.Register(api);

        return true;
    }
}
