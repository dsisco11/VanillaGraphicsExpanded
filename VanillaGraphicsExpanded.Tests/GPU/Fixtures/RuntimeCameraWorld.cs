using System.Reflection;
using System.Runtime.CompilerServices;
using Vintagestory.Client.NoObf;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Supplies native camera and light-table ownership without constructing client rendering subsystems.</summary>
internal sealed class RuntimeCameraWorld
{
    private readonly Func<IClientPlayer> player;

    #region Public API
    /// <summary>Exposes the installed world type required by active engine-table calibration.</summary>
    public ClientMain World { get; }

    /// <summary>Initializes finite source tables and a native clock while borrowing the fixture's moving player.</summary>
    public RuntimeCameraWorld(Func<IClientPlayer> player)
    {
        this.player = player;
        World = (ClientMain)RuntimeHelpers.GetUninitializedObject(typeof(ClientMain));
        var map = (ClientWorldMap)RuntimeHelpers.GetUninitializedObject(typeof(ClientWorldMap));
        var platform = (ClientPlatformWindows)RuntimeHelpers.GetUninitializedObject(typeof(ClientPlatformWindows));
        GC.SuppressFinalize(World); GC.SuppressFinalize(map); GC.SuppressFinalize(platform);
        World.WorldMap = map; World.Platform = platform;
        const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        // The clock delegates to a platform stopwatch. Native initialization would create unrelated windows.
        for (Type? type = typeof(ClientPlatformWindows); type != null; type = type.BaseType)
            foreach (var field in type.GetFields(fields | BindingFlags.DeclaredOnly))
                if (field.FieldType == typeof(System.Diagnostics.Stopwatch))
                    field.SetValue(platform, new System.Diagnostics.Stopwatch());
        typeof(ClientWorldMap).GetField("BlockLightLevels", fields)!.SetValue(map,
            Enumerable.Range(0, 32).Select(i => .04f + i * .03f).ToArray());
        typeof(ClientWorldMap).GetField("SunLightLevels", fields)!.SetValue(map,
            Enumerable.Range(0, 32).Select(i => .03f + i * .03f).ToArray());
        typeof(ClientWorldMap).GetField("hueLevels", fields)!.SetValue(map, new byte[32]);
        typeof(ClientWorldMap).GetField("satLevels", fields)!.SetValue(map, new byte[32]);
        RefreshCamera();
    }

    /// <summary>Updates player coordinates before the production frame callback samples them.</summary>
    public void RefreshCamera() => World.player = (ClientPlayer)player();
    #endregion
}
