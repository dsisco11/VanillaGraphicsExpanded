using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Exercises installed engine pool allocation and constructor publication without starting a client.</summary>
[Collection("GPU")]
public sealed class LiquidMeshSourceTests
{
    #region Engine lifecycle
    /// <summary>Engine spare capacity is not an incomplete active atlas prefix.</summary>
    [Fact]
    public void InstalledConstructorPublishesSpareCapacity()
    {
        var game = Empty<ClientMain>();
        AccessTools.Field(typeof(ClientMain), "WorldMap").SetValue(game, Empty<ClientWorldMap>());
        var api = Empty<ClientCoreAPI>();
        var events = Empty<ClientEventAPI>();
        var atlas = Empty<BlockTextureAtlasManager>();
        AccessTools.Field(typeof(ClientEventAPI), "game").SetValue(events, game);
        AccessTools.Field(typeof(ClientCoreAPI), "eventapi").SetValue(api, events);
        AccessTools.Field(typeof(ClientMain), "api").SetValue(game, api);
        AccessTools.Field(typeof(ClientMain), "BlockAtlasManager").SetValue(game, atlas);
        AccessTools.Field(typeof(TextureAtlasManager), "<Size>k__BackingField").SetValue(atlas, new Size2i(256,256));
        var harmony = new Harmony("VGE.Tests.LiquidMeshSource");
        try
        {
            harmony.CreateClassProcessor(typeof(LiquidMeshSourceHook)).Patch();
            var renderer = new ChunkRenderer([17], game);
            Assert.True(LiquidMeshSource.TryGet(api, out var source));
            Assert.Same(renderer, source.Renderer);
            var pools = renderer.poolsByRenderPass[(int)EnumChunkRenderPass.Liquid];
            Assert.Equal(renderer.textureIds.Length + 3, pools.Length);
            Assert.NotNull(pools[0]);
            Assert.All(pools.Skip(renderer.textureIds.Length), pool => Assert.Null(pool));
            // This was the old readiness predicate: the actual engine's valid allocation rejects it.
            Assert.False(pools.Length == renderer.textureIds.Length);
            Assert.True(source.TryGetAtlasPools(out var ids, out var activePools));
            Assert.Same(renderer.textureIds, ids);
            Assert.Same(pools, activePools);
            renderer.textureIds = [17,18];
            Assert.False(source.TryGetAtlasPools(out _, out _));
            AccessTools.Method(typeof(ChunkRenderer), "RuntimeAddBlockTextureAtlas").Invoke(renderer, [new int[] {17,18}]);
            Assert.True(source.TryGetAtlasPools(out _, out _));
            renderer.textureIds = [17,18,19,20,21];
            Assert.False(source.TryGetAtlasPools(out _, out _));
        }
        finally { harmony.UnpatchAll(harmony.Id); LiquidMeshSource.Remove(api); }
    }
    #endregion

    #region Fixture
    /// <summary>Allocates only engine data shells, avoiding window and networking initialization.</summary>
    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    #endregion
}

