using System.Numerics;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

/// <summary>Mutable voxel and light data shared by CPU and GPU reproduction scenarios.</summary>
internal sealed class ControlledVoxelWorld
{
    private readonly Dictionary<(int X, int Y, int Z), Block> blocks = new();
    private readonly Dictionary<(int X, int Y, int Z), Vector4> lights = new();
    private readonly Block air = new() { BlockId = 0 };

    public Vector4 DefaultLight { get; set; }
    public int MapSizeY { get; set; }
    public System.Func<(int X, int Y, int Z), bool> IsLoaded { get; set; } = _ => true;
    public List<(int X, int Y, int Z)> LightQueries { get; } = new();

    #region Scene Setup
    /// <summary>Sets a cell's material and collision geometry; null removes the solid cell.</summary>
    public void SetBlock(int x, int y, int z, Block? block)
    {
        if (block is null) blocks.Remove((x, y, z));
        else blocks[(x, y, z)] = block;
    }

    /// <summary>Fills an inclusive integer box with explicit light values, including air cells.</summary>
    public void FillLight((int X, int Y, int Z) min, (int X, int Y, int Z) max, Vector4 light)
    {
        for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int x = min.X; x <= max.X; x++)
            lights[(x, y, z)] = light;
    }

    /// <summary>Adds a hollow box with inclusive outer bounds and configurable wall thickness.</summary>
    public void AddRoom((int X, int Y, int Z) min, (int X, int Y, int Z) max, int thickness = 1, int materialId = 1)
    {
        if (thickness < 1 || max.X - min.X < 2 * thickness || max.Y - min.Y < 2 * thickness || max.Z - min.Z < 2 * thickness)
            throw new ArgumentOutOfRangeException(nameof(thickness), "Room must contain an air interior.");
        var solid = new Block { BlockId = materialId, CollisionBoxes = Block.DefaultCollisionSelectionBoxes };
        for (int z = min.Z; z <= max.Z; z++)
        for (int y = min.Y; y <= max.Y; y++)
        for (int x = min.X; x <= max.X; x++)
        {
            if (x < min.X + thickness || x > max.X - thickness ||
                y < min.Y + thickness || y > max.Y - thickness ||
                z < min.Z + thickness || z > max.Z - thickness)
                blocks[(x, y, z)] = solid;
        }
    }
    #endregion

    #region Production Adapter
    /// <summary>Uses the production traversal and surface-light lookup over these controlled cells.</summary>
    public IWorldProbeTraceScene CreateTraceScene() => new BlockAccessorWorldProbeTraceScene(ControlledBlockAccessor.Create(this));

    /// <summary>Returns the explicit solid or the shared air block.</summary>
    internal Block GetBlock((int X, int Y, int Z) position) => blocks.GetValueOrDefault(position, air);

    /// <summary>Records which cell production traversal samples before returning controlled light.</summary>
    internal Vector4 GetLight((int X, int Y, int Z) position)
    {
        LightQueries.Add(position);
        return lights.GetValueOrDefault(position, DefaultLight);
    }
    #endregion
}
