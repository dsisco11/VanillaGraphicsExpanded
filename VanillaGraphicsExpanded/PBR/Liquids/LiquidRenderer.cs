using System;
using VanillaGraphicsExpanded.ModSystems;
using VanillaGraphicsExpanded.PBR.Materials;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Owns liquid OIT submission while borrowing engine mesh pools and transparent targets.</summary>
internal sealed class LiquidRenderer : IRenderer
{
    private readonly ICoreClientAPI api;
    private readonly Func<WaterRefractionScene?> getRefractionScene;
    private static LiquidRenderer? active;
    private bool failed;
    public double RenderOrder => .369;
    public int RenderRange => int.MaxValue;

    #region Lifetime
    /// <summary>Registers immediately before the engine terrain OIT callback.</summary>
    internal LiquidRenderer(ICoreClientAPI api, Func<WaterRefractionScene?>? getRefractionScene = null)
    {
        this.api = api;
        this.getRefractionScene = getRefractionScene ?? (() => null);
        active = this;
        api.Event.RegisterRenderer(this, EnumRenderStage.OIT, "vge_liquids");
        api.Event.LeaveWorld += LeaveWorld;
    }
    /// <summary>Releases borrowed world state without deleting any engine resources.</summary>
    private void LeaveWorld() { LiquidMeshSource.Remove(api); failed = false; }
    /// <summary>Restores unconditional vanilla submission by retiring the owned renderer.</summary>
    public void Dispose()
    {
        api.Event.UnregisterRenderer(this, EnumRenderStage.OIT);
        api.Event.LeaveWorld -= LeaveWorld;
        LiquidMeshSource.Remove(api);
        if (ReferenceEquals(active, this)) active = null;
    }
    #endregion

    #region Submission
    /// <summary>Consumes one replacement decision only for the matching engine renderer.</summary>
    internal static bool ConsumeEngineSuppression(ChunkRenderer renderer)
    {
        if (active is null || !LiquidMeshSource.TryGet(active.api, out var source)
            || !ReferenceEquals(source.Renderer, renderer)) return false;
        bool suppress = source.SuppressNextEngineDraw;
        source.SuppressNextEngineDraw = false;
        return suppress;
    }

    /// <summary>Checks that color rendering can take ownership before depth abandons its vanilla draw.</summary>
    internal static bool CanTakeOwnership(ICoreClientAPI api, int[] atlases)
    {
        if (active is null || !ReferenceEquals(active.api, api) || active.failed
            || !MaterialAtlasSystem.Instance.IsInitialized
            || AtmosphereModSystem.Lighting is null
            || AtmosphereModSystem.AerialRadianceTextureId == 0
            || AtmosphereModSystem.AerialAttenuationTextureId == 0
            || !GpuUniformRingSystem.TryGetCurrent(out _)) return false;
        var program = GpuShaderPrograms.Get<LiquidShaderProgram>(api, "pbr_liquid");
        if (program is null || !program.EnsureReady()) return false;
        var store = MaterialAtlasSystem.Instance.TextureStore;
        foreach (int atlas in atlases)
            if (!store.TryGetMaterialParamsTextureId(atlas, out _)) return false;
        return true;
    }

    /// <summary>Prepares all resources before submitting, then restores engine state even on failure.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.OIT || !LiquidMeshSource.TryGet(api, out var source)) return;
        source.SuppressNextEngineDraw = false;
        if (failed || !LiquidDepthRenderer.TryGetCompletedWaveFrame(out var waves)
            || api.Render.FrameWidth <= 0 || api.Render.FrameHeight <= 0) return;
        var render = api.Render;
        var buffers = render.FrameBuffers;
        if (buffers.Count <= (int)EnumFrameBuffer.Transparent
            || buffers[(int)EnumFrameBuffer.Transparent] is not { } transparent
            || render.CurrentFrameBuffer is not { } current || current.FboId != transparent.FboId) return;
        var program = GpuShaderPrograms.Get<LiquidShaderProgram>(api, "pbr_liquid");
        if (program is null) return;
        try
        {
            // Engine callbacks bind GL resources directly between VGE passes.
            GlStateCache.Current.InvalidateAll();
            if (!program.EnsureReady()) return;
            // Missing atlas or atmosphere data retains vanilla ownership for the entire invocation.
            if (!source.TryGetAtlasPools(out var atlases, out var pools) || !CanTakeOwnership(api, atlases)) return;
            var store = MaterialAtlasSystem.Instance.TextureStore;
            program.CaptureFrameInputs(api, source.TileSize);
            program.VolumeTransportEnabled = WaterVolumeRenderer.WasComposed(api);
            var refraction = ConfigModSystem.Config.WaterRefractionEnabled ? getRefractionScene() : null;
            program.RefractionEnabled = refraction?.Published == true;
            program.RefractionColorTexture = refraction?.Published == true ? refraction.Color : null;
            program.RefractionDepthTexture = refraction?.Published == true ? refraction.Depth : null;
            program.WaveFrame = waves;
            program.ModelViewMatrix = render.CameraMatrixOriginf;
            program.ForcedTransparency = 0;
            program.DepthTexture = buffers[(int)EnumFrameBuffer.Primary].DepthTextureId;
            program.ShadowMapNear = buffers[(int)EnumFrameBuffer.ShadowmapNear]?.DepthTextureId ?? 0;
            program.ShadowMapFar = buffers[(int)EnumFrameBuffer.ShadowmapFar]?.DepthTextureId ?? 0;
            program.AerialRadianceTexture = AtmosphereModSystem.AerialRadianceTexture;
            program.AerialAttenuationTexture = AtmosphereModSystem.AerialAttenuationTexture;
            // Establish complete initial state before entering the engine pool loop.
            if (atlases.Length == 0) return;
            store.TryGetPageTextures(atlases[0], out var initialMaterial);
            program.TerrainTexture = atlases[0];
            program.MaterialParamsTexture = initialMaterial.MaterialParamsTexture;
            BindWaterMedium(program, store, atlases[0]);
            using var scope = program.UseScope();
            if (!ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, program)) return;
            var engineRender = (Vintagestory.Client.RenderAPIBase)render;
            bool previousSsbo = LiquidMeshSource.UseSsbo(engineRender);
            try
            {
                LiquidMeshSource.UseSsbo(engineRender) = false;
                // Once submission begins, a partial failure must not draw the same water twice.
                source.SuppressNextEngineDraw = true;
                for (int i = 0; i < atlases.Length; i++)
                {
                    store.TryGetPageTextures(atlases[i], out var material);
                    program.TerrainTexture = atlases[i];
                    program.MaterialParamsTexture = material.MaterialParamsTexture;
                    BindWaterMedium(program, store, atlases[i]);
                    pools[i].Render(api.World.Player.Entity.CameraPos, "origin", EnumFrustumCullMode.CullNormal);
                }
            }
            finally { LiquidMeshSource.UseSsbo(engineRender) = previousSsbo; }
        }
        catch (Exception error)
        {
            failed = true;
            api.Logger.Error("[VGE] Liquid renderer disabled; vanilla submission resumes next invocation. {0}", error.ToString());
        }
        finally { GlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Internal API
    /// <summary>Stages a dimension-matched medium generation before the pool hook publishes the complete draw inputs.</summary>
    internal static void BindWaterMedium(LiquidShaderProgram program, MaterialAtlasTextureStore store, int atlas)
    {
        bool available = store.TryGetWaterMediumTextures(atlas, out var medium);
        program.MediumLookupEnabled = available;
        program.WaterMediumIndicesTexture = available ? medium!.Indices : null;
        program.WaterMediumRecordsTexture = available ? medium!.Records : null;
    }
    #endregion
}
