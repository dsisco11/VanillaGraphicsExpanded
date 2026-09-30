using System.Runtime.CompilerServices;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.PBR.Liquids;

/// <summary>Tracks engine-owned liquid managers without capturing atlas arrays across their replacement.</summary>
internal sealed class LiquidMeshSource(ChunkRenderer renderer, Vec2f tileSize)
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, LiquidMeshSource> sources = new();
    internal ChunkRenderer Renderer { get; } = renderer;
    internal Vec2f TileSize { get; } = tileSize;
    internal bool SuppressNextEngineDraw { get; set; }

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
