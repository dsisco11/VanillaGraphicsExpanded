using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Support.Postprocessing;

/// <summary>Builds a minimal client world for postprocessing tests without starting engine subsystems.</summary>
internal static class SceneColorWorldFixture
{
    #region Internal API
    /// <summary>Creates a world with only the player, clock, and lighting tables required by render owners.</summary>
    internal static ClientMain CreateWorld()
    {
        var world = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        var map = (ClientWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldMap));
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(world);
        GC.SuppressFinalize(map);
        GC.SuppressFinalize(platform);
        world.player = CreatePlayer();
        world.WorldMap = map;
        world.Platform = platform;

        // The client clock delegates to the platform stopwatch; native construction is unnecessary.
        const BindingFlags instanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(ClientMain).GetField("FogColorSky", instanceFields)!.SetValue(world, new Vec3f(6, 2, 3));
        for (Type? type = typeof(ClientPlatformWindows); type is not null; type = type.BaseType)
        {
            foreach (var field in type.GetFields(instanceFields | BindingFlags.DeclaredOnly))
            {
                if (field.FieldType == typeof(System.Diagnostics.Stopwatch))
                    field.SetValue(platform, new System.Diagnostics.Stopwatch());
            }
        }

        typeof(ClientWorldMap).GetField("BlockLightLevels", instanceFields)!.SetValue(map,
            Enumerable.Range(0, 32).Select(i => .04f + i * .03f).ToArray());
        typeof(ClientWorldMap).GetField("SunLightLevels", instanceFields)!.SetValue(map,
            Enumerable.Range(0, 32).Select(i => .03f + i * .03f).ToArray());
        typeof(ClientWorldMap).GetField("hueLevels", instanceFields)!.SetValue(map, new byte[32]);
        typeof(ClientWorldMap).GetField("satLevels", instanceFields)!.SetValue(map, new byte[32]);
        return world;
    }
    #endregion

    #region Private
    /// <summary>Creates the minimal installed player data expected by the client world.</summary>
    private static ClientPlayer CreatePlayer()
    {
        var player = (ClientPlayer)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlayer));
        var data = (ClientWorldPlayerData)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldPlayerData));
        const BindingFlags instanceFields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        typeof(ClientWorldPlayerData).GetField("entityplayer", instanceFields)!.SetValue(data, new EntityPlayer());
        typeof(ClientPlayer).GetField("worlddata", instanceFields)!.SetValue(player, data);
        return player;
    }
    #endregion
}
