using System.Reflection;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Adapts controlled voxel data to the narrow accessor API used by production tracing.</summary>
internal class ControlledBlockAccessor : DispatchProxy
{
    private ControlledVoxelWorld world = null!;
    private IWorldChunk loadedChunk = null!;

    #region Proxy API
    /// <summary>Creates an accessor whose chunk presence, blocks and light come from the fixture.</summary>
    internal static IBlockAccessor Create(ControlledVoxelWorld world)
    {
        var accessor = Create<IBlockAccessor, ControlledBlockAccessor>();
        var proxy = (ControlledBlockAccessor)(object)accessor;
        proxy.world = world;
        proxy.loadedChunk = Create<IWorldChunk, LoadedChunkSentinel>();
        return accessor;
    }

    /// <summary>Rejects unexpected calls so an incomplete mock cannot silently produce darkness.</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod is null || args is null) throw new InvalidOperationException("Missing accessor call.");
        var position = ReadPosition(args);
        return targetMethod.Name switch
        {
            nameof(IBlockAccessor.GetChunkAtBlockPos) => world.IsLoaded(position) ? loadedChunk : null,
            nameof(IBlockAccessor.GetMostSolidBlock) => world.GetBlock(position),
            nameof(IBlockAccessor.GetLightRGBs) => ToLight(world.GetLight(position)),
            _ => throw new InvalidOperationException($"Unexpected accessor call: {targetMethod.Name}")
        };
    }
    #endregion

    #region Argument Conversion
    /// <summary>Supports the block-position and integer-coordinate accessor overloads.</summary>
    private static (int X, int Y, int Z) ReadPosition(object?[] args)
    {
        if (args.Length >= 1 && args[0] is BlockPos p) return (p.X, p.Y, p.Z);
        if (args.Length >= 3 && args[0] is int x && args[1] is int y && args[2] is int z) return (x, y, z);
        throw new InvalidOperationException("Unexpected position arguments.");
    }

    /// <summary>Converts the fixture's RGB and sunlight vector to the engine API representation.</summary>
    private static Vec4f ToLight(System.Numerics.Vector4 light) => new(light.X, light.Y, light.Z, light.W);
    #endregion
}

