using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.DebugView;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.PBR.Materials.Diagnostics;
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
    private DirectLightingBufferManager? directLightingBufferManager;
    private DirectLightingRenderer? directLightingRenderer;
    private PBRCompositeRenderer? pbrCompositeRenderer;
    private GlGpuProfilerRenderer? gpuProfilerRenderer;
    private HarmonyLib.Harmony? harmony;

    private TextureStreamingManagerRenderer? textureStreamingRenderer;
    private bool textureStreamingRendererRegistered;

    // LumOn components
    private LumOnBufferManager? lumOnBufferManager;
    private LumOnRenderer? lumOnRenderer;
    private LumOnDebugRenderer? lumOnDebugRenderer;

    private HudMaterialAtlasProgressPanel? materialAtlasProgressPanel;

    private bool isLevelFinalized;
    private bool pendingMaterialAtlasPopulate;
    private long materialAtlasPopulateCallbackId;

    private LumOnLiveConfigSnapshot? lastLiveConfigSnapshot;

    private readonly record struct LumOnLiveConfigSnapshot(
        bool LumOnEnabled,
        int ProbeSpacingPx,
        bool HalfResolution)
    {
        public static LumOnLiveConfigSnapshot From(LumOnConfig cfg)
        {
            return new LumOnLiveConfigSnapshot(
                cfg.Enabled,
                cfg.ProbeSpacingPx,
                cfg.HalfResolution);
        }
    }

    private static TextureStreamingSettings BuildTextureStreamingSettings(LumOnConfig cfg)
    {
        return new TextureStreamingSettings(
            EnablePboStreaming: cfg.TextureStreamingEnabled,
            AllowDirectUploads: cfg.TextureStreamingAllowDirectUploads,
            ForceDisablePersistent: cfg.TextureStreamingForceDisablePersistent,
            UseCoherentMapping: cfg.TextureStreamingUseCoherentMapping,
            MaxUploadsPerFrame: cfg.TextureStreamingMaxUploadsPerFrame,
            MaxBytesPerFrame: cfg.TextureStreamingMaxBytesPerFrame,
            MaxStagingBytes: cfg.TextureStreamingMaxStagingBytes,
            PersistentRingBytes: cfg.TextureStreamingPersistentRingBytes,
            TripleBufferBytes: cfg.TextureStreamingTripleBufferBytes,
            PboAlignment: cfg.TextureStreamingPboAlignment);
    }

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

        // Generic texture streaming (PBO) manager tick.
        textureStreamingRenderer ??= new TextureStreamingManagerRenderer();
        api.Event.RegisterRenderer(textureStreamingRenderer, EnumRenderStage.Before, "vge_texture_streaming");
        textureStreamingRendererRegistered = true;

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

        // Material params + normal/depth atlas textures:
        // - Phase 1 (allocation) can happen any time (no-op until atlas exists)
        // - Phase 2 (populate/bake) must only run after the block atlas is finalized
        MaterialAtlasSystem.Instance.CreateTextureObjects(api);
        api.Event.BlockTexturesLoaded += () =>
        {
            // Keep textures in sync with the block atlas as soon as it exists,
            // but defer the expensive population/bake until the world is fully ready.
            MaterialAtlasSystem.Instance.CreateTextureObjects(api);

            if (isLevelFinalized)
            {
                MaterialAtlasSystem.Instance.PopulateAtlasContents(api);
            }
            else
            {
                pendingMaterialAtlasPopulate = true;
            }
        };

        api.Event.LevelFinalize += () =>
        {
            isLevelFinalized = true;

            if (!pendingMaterialAtlasPopulate)
            {
                return;
            }

            pendingMaterialAtlasPopulate = false;

            if (materialAtlasPopulateCallbackId != 0)
            {
                api.Event.UnregisterCallback(materialAtlasPopulateCallbackId);
            }

            // Give the client a brief moment after finalize to finish settling (GUI, chunk init, etc.).
            materialAtlasPopulateCallbackId = api.Event.RegisterCallback(
                _ => MaterialAtlasSystem.Instance.PopulateAtlasContents(api),
                millisecondDelay: 500);
        };

        // Optional: small in-game progress overlay while the material atlas builds.
        materialAtlasProgressPanel = new HudMaterialAtlasProgressPanel(api, MaterialAtlasSystem.Instance);
        api.Event.ReloadTextures += () => {
            api.Logger.Debug("[VGE] ReloadTextures event");
            MaterialAtlasSystem.Instance.RequestRebuild(api);
        };

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

        // Initialize LumOn based on config (loaded by ConfigModSystem).
        ConfigModSystem.Config.Sanitize();
        TextureStreamingSystem.Configure(BuildTextureStreamingSettings(ConfigModSystem.Config));
        lastLiveConfigSnapshot = LumOnLiveConfigSnapshot.From(ConfigModSystem.Config);
        if (ConfigModSystem.Config.Enabled)
        {
            api.Logger.Notification("[VGE] LumOn enabled - using Screen Probe Gather");
            lumOnBufferManager = new LumOnBufferManager(api, ConfigModSystem.Config);

            var worldProbeSystem = api.ModLoader.GetModSystem<WorldProbeModSystem>();
            var clipmapManager = worldProbeSystem.EnsureClipmapResources(api, "LumOn startup");
            lumOnRenderer = new LumOnRenderer(api, ConfigModSystem.Config, lumOnBufferManager, gBufferManager!, clipmapManager);
        }
        else
        {
            api.Logger.Notification("[VGE] LumOn disabled - direct lighting only");
            lumOnBufferManager = null;
            lumOnRenderer = null;
        }

        // Create direct lighting pass buffers + renderer (Opaque @ 9.0)
        directLightingBufferManager = new DirectLightingBufferManager(api);
        directLightingRenderer = new DirectLightingRenderer(api, gBufferManager, directLightingBufferManager);

        // Unified debug overlay (AfterBlit) driven by the VGE Debug View dialog.
        var initialClipmapManager = api.ModLoader.GetModSystem<WorldProbeModSystem>().GetClipmapBufferManagerOrNull();
        lumOnDebugRenderer = new LumOnDebugRenderer(api, ConfigModSystem.Config, lumOnBufferManager, gBufferManager, directLightingBufferManager, initialClipmapManager);

        // Final composite (Opaque @ 11.0): direct + optional indirect + fog
        pbrCompositeRenderer = new PBRCompositeRenderer(
            api,
            gBufferManager,
            directLightingBufferManager,
            ConfigModSystem.Config,
            lumOnBufferManager);

        // Initialize the debug view manager (GUI)
        VgeDebugViewManager.Initialize(
            api,
            ConfigModSystem.Config);
    }

    public void OnConfigReloaded(ICoreAPI api)
    {
        if (api is not ICoreClientAPI clientApi) return;

        // Ensure any external mutation (ConfigLib) is clamped into safe bounds.
        ConfigModSystem.Config.Sanitize();
        TextureStreamingSystem.Configure(BuildTextureStreamingSettings(ConfigModSystem.Config));

        var current = LumOnLiveConfigSnapshot.From(ConfigModSystem.Config);
        if (lastLiveConfigSnapshot is null)
        {
            lastLiveConfigSnapshot = current;
            return;
        }

        var prev = lastLiveConfigSnapshot.Value;
        lastLiveConfigSnapshot = current;

        // LumOn enable: create missing runtime objects.
        // LumOn disable: keep objects alive (renderer remains registered) but it will early-out.
        if (current.LumOnEnabled && lumOnRenderer is null)
        {
            if (gBufferManager is null)
            {
                clientApi.Logger.Warning("[VGE] LumOn enabled via live config reload, but GBufferManager is not initialized yet.");
                return;
            }

            clientApi.Logger.Notification("[VGE] LumOn enabled via live config reload");
            lumOnBufferManager ??= new LumOnBufferManager(clientApi, ConfigModSystem.Config);

            var worldProbeSystem = clientApi.ModLoader.GetModSystem<WorldProbeModSystem>();
            var clipmapManager = worldProbeSystem.EnsureClipmapResources(clientApi, "LumOn live enable");
            lumOnDebugRenderer?.SetWorldProbeClipmapBufferManager(clipmapManager);
            lumOnRenderer = new LumOnRenderer(clientApi, ConfigModSystem.Config, lumOnBufferManager, gBufferManager, clipmapManager);
        }

        // Screen-probe resource sizing depends on these keys.
        if (lumOnBufferManager is not null)
        {
            if (prev.ProbeSpacingPx != current.ProbeSpacingPx || prev.HalfResolution != current.HalfResolution)
            {
                lumOnBufferManager.RequestRecreateBuffers("live config change (ProbeSpacingPx/HalfResolution)");
            }
        }


        // Bind Phase 18 clipmap resources if they exist (WorldProbeModSystem owns them).
        if (current.LumOnEnabled)
        {
            var clipmapManager = clientApi.ModLoader.GetModSystem<WorldProbeModSystem>().GetClipmapBufferManagerOrNull();
            if (clipmapManager is not null)
            {
                lumOnDebugRenderer?.SetWorldProbeClipmapBufferManager(clipmapManager);
                lumOnRenderer?.SetWorldProbeClipmapBufferManager(clipmapManager);
            }
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        try
        {
            VgeDebugViewManager.Dispose();

            if (capi != null && materialAtlasPopulateCallbackId != 0)
            {
                capi.Event.UnregisterCallback(materialAtlasPopulateCallbackId);
                materialAtlasPopulateCallbackId = 0;
            }

            materialAtlasProgressPanel?.Dispose();
            materialAtlasProgressPanel = null;

            // Unregister GPU debug label renderers
            if (capi != null)
            {
                GpuDebugLabelManager.Unregister(capi);
            }

            if (capi != null && textureStreamingRendererRegistered && textureStreamingRenderer is not null)
            {
                try
                {
                    capi.Event.UnregisterRenderer(textureStreamingRenderer, EnumRenderStage.Before);
                }
                catch
                {
                    // Best-effort.
                }
            }

            textureStreamingRenderer?.Dispose();
            textureStreamingRenderer = null;
            textureStreamingRendererRegistered = false;
            TextureStreamingSystem.Dispose();

            gpuProfilerRenderer?.Dispose();
            gpuProfilerRenderer = null;

            GlGpuProfiler.Instance.Dispose();

            directLightingRenderer?.Dispose();
            directLightingRenderer = null;

            directLightingBufferManager?.Dispose();
            directLightingBufferManager = null;

            pbrCompositeRenderer?.Dispose();
            pbrCompositeRenderer = null;

            // Dispose LumOn components
            lumOnDebugRenderer?.Dispose();
            lumOnDebugRenderer = null;

            lumOnRenderer?.Dispose();
            lumOnRenderer = null;

            lumOnBufferManager?.Dispose();
            lumOnBufferManager = null;

            MaterialAtlasSystem.Instance.Dispose();

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
