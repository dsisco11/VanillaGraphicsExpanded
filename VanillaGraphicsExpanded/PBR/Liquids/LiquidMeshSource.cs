using System.Runtime.CompilerServices;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Tracks engine-owned liquid managers without capturing atlas arrays across their replacement.</summary>
internal sealed partial class LiquidMeshSource : System.IDisposable
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, LiquidMeshSource> sources = new();
    // The public rendering API exposes only a getter for this engine mesh-layout selector.
    internal static readonly AccessTools.FieldRef<Vintagestory.Client.RenderAPIBase, bool> UseSsbo =
        AccessTools.FieldRefAccess<Vintagestory.Client.RenderAPIBase, bool>("useSSBOs");
    internal ChunkRenderer Renderer { get; }
    internal Vec2f TileSize { get; }
    internal bool SuppressNextEngineDraw { get; set; }

    #region Submission resources
    /// <summary>Retains the engine renderer and atlas metric without taking ownership.</summary>
    internal LiquidMeshSource(ChunkRenderer renderer, Vec2f tileSize)
    {
        Renderer = renderer;
        TileSize = tileSize;
    }

    /// <summary>Reads current atlas and liquid-pool arrays, validating only the engine's active atlas prefix.</summary>
    internal bool TryGetAtlasPools(out int[] atlases, out MeshDataPoolManager[] pools)
    {
        atlases = Renderer.textureIds;
        pools = [];
        var table = Renderer.poolsByRenderPass;
        if (atlases is null || table is null || table.Length <= (int)EnumChunkRenderPass.Liquid
            || table[(int)EnumChunkRenderPass.Liquid] is not { } liquid) return false;
        pools = liquid;
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
        Remove(api);
        sources.Add(api, new(renderer, tileSize));
    }
    /// <summary>Returns current world resources without querying private fields each frame.</summary>
    internal static bool TryGet(ICoreClientAPI api, out LiquidMeshSource source) => sources.TryGetValue(api, out source!);
    /// <summary>Drops borrowed world references when the client leaves its world.</summary>
    internal static void Remove(ICoreClientAPI api)
    {
        if (sources.TryGetValue(api, out var source)) source.Dispose();
        sources.Remove(api);
    }
    #endregion
}
