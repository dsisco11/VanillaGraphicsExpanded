using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.Liquids;
using Vintagestory.API.Client;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Integration.Water;

/// <summary>Exercises liquid-pool source publication through the installed engine constructor.</summary>
[Collection("CPU state")]
public sealed class LiquidMeshSourceTests
{
    #region Public API
    /// <summary>Engine spare capacity remains distinct from the active liquid atlas prefix.</summary>
    [Fact]
    public void InstalledConstructorPublishesSpareCapacity()
    {
        // Arrange uninitialized engine shells with only the constructor fields needed by liquid pool allocation.
        var game = Empty<ClientMain>();
        AccessTools.Field(typeof(ClientMain), "WorldMap").SetValue(game, Empty<ClientWorldMap>());
        var api = Empty<ClientCoreAPI>();
        var events = Empty<ClientEventAPI>();
        var atlas = Empty<BlockTextureAtlasManager>();
        AccessTools.Field(typeof(ClientEventAPI), "game").SetValue(events, game);
        AccessTools.Field(typeof(ClientCoreAPI), "eventapi").SetValue(api, events);
        AccessTools.Field(typeof(ClientMain), "api").SetValue(game, api);
        AccessTools.Field(typeof(ClientMain), "BlockAtlasManager").SetValue(game, atlas);
        AccessTools.Field(typeof(TextureAtlasManager), "<Size>k__BackingField").SetValue(atlas, new Size2i(256, 256));
        var harmony = new Harmony("VGE.Tests.LiquidMeshSource");
        try
        {
            // Act by installing the production constructor hooks and constructing a real engine renderer.
            harmony.CreateClassProcessor(typeof(LiquidMeshSourceHook)).Patch();
            harmony.CreateClassProcessor(typeof(LiquidGeometryManagerHook)).Patch();
            var renderer = new ChunkRenderer([17], game);

            // Assert the source publishes the same active ids/pools while retaining unused engine capacity.
            Assert.True(LiquidMeshSource.TryGet(api, out var source));
            Assert.Same(renderer, source.Renderer);
            var pools = renderer.poolsByRenderPass[(int)EnumChunkRenderPass.Liquid];
            Assert.Equal(renderer.textureIds.Length + 3, pools.Length);
            Assert.NotNull(pools[0]);
            Assert.All(pools.Skip(renderer.textureIds.Length), pool => Assert.Null(pool));
            Assert.False(pools.Length == renderer.textureIds.Length);
            Assert.True(source.TryGetAtlasPools(out var ids, out var activePools));
            Assert.Same(renderer.textureIds, ids);
            Assert.Same(pools, activePools);

            // Act and assert mismatched active prefixes are rejected, then restored when the engine updates pools.
            renderer.textureIds = [17, 18];
            Assert.False(source.TryGetAtlasPools(out _, out _));
            AccessTools.Method(typeof(ChunkRenderer), "RuntimeAddBlockTextureAtlas").Invoke(renderer, [new int[] { 17, 18 }]);
            Assert.True(source.TryGetAtlasPools(out _, out activePools));
            renderer.textureIds = [17, 18, 19, 20, 21];
            Assert.False(source.TryGetAtlasPools(out _, out _));
        }
        finally { harmony.UnpatchAll(harmony.Id); LiquidMeshSource.Remove(api); }
    }
    #endregion

    #region Private
    /// <summary>Creates only an engine data shell and avoids client, window, and network initialization.</summary>
    private static T Empty<T>() => (T)RuntimeHelpers.GetUninitializedObject(typeof(T));
    #endregion
}
