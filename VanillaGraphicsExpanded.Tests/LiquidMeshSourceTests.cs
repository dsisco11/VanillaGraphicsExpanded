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
            Assert.False(source.MayHaveLiquidGeometry());
            var livePools = (List<MeshDataPool>)AccessTools.Field(typeof(MeshDataPoolManager), "pools").GetValue(pools[0])!;
            var pool = (MeshDataPool)AccessTools.Constructor(typeof(MeshDataPool), [typeof(int),typeof(int),typeof(int)]).Invoke([16,24,4]);
            var locations = (List<ModelDataPoolLocation>)AccessTools.Field(typeof(MeshDataPool), "poolLocations").GetValue(pool)!;
            livePools.Add(pool);
            Assert.True(pool.IsEmpty()); Assert.False(source.MayHaveLiquidGeometry());
            // Visibility and dimensions do not make allocated liquid geometry safe
            // to ignore. The installed IsEmpty contract observes locations directly.
            var location = new ModelDataPoolLocation { Hide = true, FrustumVisible = false };
            AccessTools.Field(typeof(MeshDataPool), "dimensionId").SetValue(pool,7);
            pool.RenderedTriangles = 0;
            locations.Add(location);
            Assert.False(pool.IsEmpty()); Assert.True(source.MayHaveLiquidGeometry());
            pool.RemoveLocation(location);
            Assert.True(pool.IsEmpty()); Assert.False(source.MayHaveLiquidGeometry());
            livePools.Add(null!);
            Assert.True(source.MayHaveLiquidGeometry());
            livePools.RemoveAt(livePools.Count - 1);
            renderer.textureIds = [17,18];
            Assert.False(source.TryGetAtlasPools(out _, out _));
            Assert.True(source.MayHaveLiquidGeometry());
            AccessTools.Method(typeof(ChunkRenderer), "RuntimeAddBlockTextureAtlas").Invoke(renderer, [new int[] {17,18}]);
            Assert.True(source.TryGetAtlasPools(out _, out activePools));
            Assert.False(source.MayHaveLiquidGeometry());
            var newPools = (List<MeshDataPool>)AccessTools.Field(typeof(MeshDataPoolManager), "pools").GetValue(activePools[1])!;
            newPools.Add(pool); locations.Add(location);
            Assert.True(source.MayHaveLiquidGeometry());
            pool.RemoveLocation(location);
            Assert.False(source.MayHaveLiquidGeometry());
            renderer.textureIds = [17,18,19,20,21];
            Assert.False(source.TryGetAtlasPools(out _, out _));
            Assert.True(source.MayHaveLiquidGeometry());
        }
        finally { harmony.UnpatchAll(harmony.Id); LiquidMeshSource.Remove(api); }
    }
    #endregion

    #region Fixture
    /// <summary>Allocates only engine data shells, avoiding window and networking initialization.</summary>
    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    #endregion
}

