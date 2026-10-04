using System.Collections.Generic;
using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Tracks engine-owned liquid managers without capturing atlas arrays across their replacement.</summary>
internal sealed class LiquidMeshSource(ChunkRenderer renderer, Vec2f tileSize)
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, LiquidMeshSource> sources = new();
    // The manager exposes draw statistics, but those are cached by prior culling.
    // Read its current pool collection and use each pool's authoritative emptiness.
    private static readonly AccessTools.FieldRef<MeshDataPoolManager, List<MeshDataPool>> ReadPools =
        AccessTools.FieldRefAccess<MeshDataPoolManager, List<MeshDataPool>>("pools");
    // The public rendering API exposes only a getter for this engine mesh-layout selector.
    internal static readonly AccessTools.FieldRef<Vintagestory.Client.RenderAPIBase, bool> UseSsbo =
        AccessTools.FieldRefAccess<Vintagestory.Client.RenderAPIBase, bool>("useSSBOs");
    internal ChunkRenderer Renderer { get; } = renderer;
    internal Vec2f TileSize { get; } = tileSize;
    internal bool SuppressNextEngineDraw { get; set; }

    #region Submission resources
    /// <summary>Proves absence only when every current liquid pool is empty; unknown or unculled geometry retains demand.</summary>
    internal bool MayHaveLiquidGeometry()
    {
        if (Renderer.textureIds is null || Renderer.poolsByRenderPass is null
            || Renderer.poolsByRenderPass.Length <= (int)EnumChunkRenderPass.Liquid
            || Renderer.poolsByRenderPass[(int)EnumChunkRenderPass.Liquid] is null
            || !TryGetAtlasPools(out var atlases, out var managers)) return true;
        for (int i = 0; i < atlases.Length; i++)
        {
            var pools = ReadPools(managers[i]);
            if (pools is null) return true;
            // Include offscreen and mini-dimension pools. Cached triangle counts
            // and camera fluid classification cannot prove that no interface exists.
            for (int pool = 0; pool < pools.Count; pool++)
                if (pools[pool] is null || !pools[pool].IsEmpty()) return true;
        }
        return false;
    }

    /// <summary>Reads current atlas and liquid-pool arrays, validating only the engine's active atlas prefix.</summary>
    internal bool TryGetAtlasPools(out int[] atlases, out MeshDataPoolManager[] pools)
    {
        atlases = Renderer.textureIds;
        pools = Renderer.poolsByRenderPass[(int)EnumChunkRenderPass.Liquid];
        // The engine reserves extra pool slots for runtime atlas growth. Those trailing
        // slots may be null; only entries indexed by the current atlas array are drawn.
        if (pools.Length < atlases.Length) return false;
        for (int i = 0; i < atlases.Length; i++)
            if (pools[i] is null) return false;
        return true;
    }
    #endregion

    #region Engine lifetime
    /// <summary>Publishes the completed renderer and its atlas metric after engine construction.</summary>
    internal static void Register(ICoreClientAPI api, ChunkRenderer renderer, Vec2f tileSize)
    {
        sources.Remove(api);
        sources.Add(api, new(renderer, tileSize));
    }
    /// <summary>Returns current world resources without querying private fields each frame.</summary>
    internal static bool TryGet(ICoreClientAPI api, out LiquidMeshSource source) => sources.TryGetValue(api, out source!);
    /// <summary>Drops borrowed world references when the client leaves its world.</summary>
    internal static void Remove(ICoreClientAPI api) => sources.Remove(api);
    #endregion
}
