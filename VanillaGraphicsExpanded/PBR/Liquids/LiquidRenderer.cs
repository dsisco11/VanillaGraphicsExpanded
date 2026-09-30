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
    private static LiquidRenderer? active;
    private bool failed;
    // The API exposes only a getter; resolve the installed engine field once, never per draw.
    private static readonly HarmonyLib.AccessTools.FieldRef<Vintagestory.Client.RenderAPIBase, bool> UseSsbo =
        HarmonyLib.AccessTools.FieldRefAccess<Vintagestory.Client.RenderAPIBase, bool>("useSSBOs");
    public double RenderOrder => .369;
    public int RenderRange => int.MaxValue;

    #region Lifetime
    /// <summary>Registers immediately before the engine terrain OIT callback.</summary>
    internal LiquidRenderer(ICoreClientAPI api)
    {
        this.api = api;
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

    /// <summary>Prepares all resources before submitting, then restores engine state even on failure.</summary>
    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (stage != EnumRenderStage.OIT || !LiquidMeshSource.TryGet(api, out var source)) return;
        source.SuppressNextEngineDraw = false;
        if (failed || api.Render.FrameWidth <= 0 || api.Render.FrameHeight <= 0) return;
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
            if (!source.TryGetAtlasPools(out var atlases, out var pools) || !MaterialAtlasSystem.Instance.IsInitialized
                || AtmosphereModSystem.Lighting is null || AtmosphereModSystem.AerialRadianceTextureId == 0
                || AtmosphereModSystem.AerialAttenuationTextureId == 0) return;
            var store = MaterialAtlasSystem.Instance.TextureStore;
            foreach (int atlas in atlases) if (!store.TryGetMaterialParamsTextureId(atlas, out _)) return;
            if (!GpuUniformRingSystem.TryGetCurrent(out _)) return;
            program.CaptureFrameInputs(api, source.TileSize);
            using var scope = program.UseScope();
            if (!ReferenceEquals(ShaderProgramBase.CurrentShaderProgram, program)) return;
            program.ModelViewMatrix = render.CameraMatrixOriginf;
            program.ForcedTransparency = 0;
            program.ApplyInputs();
            program.DepthTexture = buffers[(int)EnumFrameBuffer.Primary].DepthTextureId;
            program.ShadowMapNear = buffers[(int)EnumFrameBuffer.ShadowmapNear]?.DepthTextureId ?? 0;
            program.ShadowMapFar = buffers[(int)EnumFrameBuffer.ShadowmapFar]?.DepthTextureId ?? 0;
            program.AerialRadianceTexture = AtmosphereModSystem.AerialRadianceTextureId;
            program.AerialAttenuationTexture = AtmosphereModSystem.AerialAttenuationTextureId;
            var engineRender = (Vintagestory.Client.RenderAPIBase)render;
            bool previousSsbo = UseSsbo(engineRender);
            try
            {
                UseSsbo(engineRender) = false;
                // Once submission begins, a partial failure must not draw the same water twice.
                source.SuppressNextEngineDraw = true;
                for (int i = 0; i < atlases.Length; i++)
                {
                    store.TryGetMaterialParamsTextureId(atlases[i], out int material);
                    program.TerrainTexture = atlases[i];
                    program.MaterialParamsTexture = material;
                    pools[i].Render(api.World.Player.Entity.CameraPos, "origin", EnumFrustumCullMode.CullNormal);
                }
            }
            finally { UseSsbo(engineRender) = previousSsbo; }
        }
        catch (Exception error)
        {
            failed = true;
            api.Logger.Error("[VGE] Liquid renderer disabled; vanilla submission resumes next invocation. {0}", error.ToString());
        }
        finally { GlStateCache.Current.InvalidateAll(); }
    }
    #endregion
}
