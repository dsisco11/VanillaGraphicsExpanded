using System.Numerics;
using System.Threading;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Numerics;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Reusable dark room inside a controlled exterior enclosure, with full directional tracing.</summary>
internal static class WorldProbeRoomScenario
{
    public const int TileSize = 16;

    #region Scenario Setup
    /// <summary>Builds a sealed dark interior; the outer enclosure gives exterior probes known hit lighting.</summary>
    public static ControlledVoxelWorld Create(float exteriorBlockLight = 0, float exteriorSunlight = 0)
    {
        var world = new ControlledVoxelWorld { DefaultLight = new Vector4(exteriorBlockLight, exteriorBlockLight, exteriorBlockLight, exteriorSunlight) };
        world.AddRoom((-8, -8, -8), (8, 8, 8));
        // Interior air is x=[-3,1), y/z=[-3,4). The positive X wall occupies [1,2).
        world.AddRoom((-4, -4, -4), (1, 4, 4));
        world.FillLight((-3, -3, -3), (0, 3, 3), Vector4.Zero);
        return world;
    }

    /// <summary>Runs every octahedral texel through the real integrator without scheduling or partial updates.</summary>
    public static LumOnWorldProbeTraceResult Trace(ControlledVoxelWorld world, Vector3d position, int x = 0, int y = 0, int z = 0)
    {
        var request = new LumOnWorldProbeUpdateRequest(0, new Vec3i(x, y, z), new Vec3i(x, y, z), x + 2 * (z + 2 * y));
        var item = new LumOnWorldProbeTraceWorkItem(
            FrameIndex: 0, Request: request, ProbePosWorld: position, MaxTraceDistanceWorld: 64,
            WorldProbeOctahedralTileSize: TileSize, WorldProbeAtlasTexelsPerUpdate: TileSize * TileSize,
            EnableDirectionPIS: false, DirectionPISExploreFraction: 0.25f,
            DirectionPISExploreCount: -1, DirectionPISWeightEpsilon: 1e-6f);
        return new LumOnWorldProbeTraceIntegrator().TraceProbe(world.CreateTraceScene(), item, CancellationToken.None);
    }
    #endregion
}
