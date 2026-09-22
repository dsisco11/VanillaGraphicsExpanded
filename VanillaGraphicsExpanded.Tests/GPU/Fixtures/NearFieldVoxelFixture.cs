using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.NearField;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Controls voxel input while production partition ownership drives the actual GPU scene.</summary>
internal sealed class NearFieldVoxelFixture : IDisposable
{
    public NearFieldGpuScene Scene { get; }
    public PartitionCoordinator Coordinator { get; }
    public long Instance { get; }
    private readonly NearFieldMaterialRegistry materials = new();
    private readonly NearFieldVoxelProvider provider;
    private long tick;

    #region Fixture lifecycle
    /// <summary>Allocates an independent cell window centered on the supplied world position.</summary>
    public NearFieldVoxelFixture(VectorInt3 center = default, int resolution = 64)
    {
        Scene = new NearFieldGpuScene(resolution);
        provider = new NearFieldVoxelProvider(Scene, materials);
        var limits = new PartitionLimits(10000, 10000, 10000, 10000, long.MaxValue);
        Coordinator = new PartitionCoordinator(limits);
        Instance = Coordinator.Register("controlled-geometry", "test", new(new(Scene.CellSize, Scene.CellSize, Scene.CellSize)), new(0, 0, 0), limits, provider);
        MoveCenter(center);
    }

    /// <summary>Retires partition storage before disposing textures on their owning thread.</summary>
    public void Dispose()
    {
        Coordinator.Unregister(Instance);
        Scene.Dispose();
    }
    #endregion

    #region Coverage and invalidation
    /// <summary>Moves the fixture's explicit window while retaining overlapping logical cells.</summary>
    public void MoveCenter(in VectorInt3 center)
    {
        int size = Scene.CellSize;
        SetWindow(new((int)Math.Floor((double)center.X / size) * size - (Scene.RegionResolution / 2) * size,
            (int)Math.Floor((double)center.Y / size) * size - (Scene.RegionResolution / 2) * size,
            (int)Math.Floor((double)center.Z / size) * size - (Scene.RegionResolution / 2) * size));
    }

    /// <summary>Applies independent world-block bounds and updates desired coverage without implicit capture.</summary>
    public void SetWindow(in VectorInt3 origin)
    {
        Scene.SetWindow(origin);
        Coordinator.SetSource(new(1, Instance, "test", new(origin.X, origin.Y, origin.Z),
            new(new(origin.X, origin.Y, origin.Z), new(origin.X + Scene.Resolution, origin.Y + Scene.Resolution, origin.Z + Scene.Resolution))));
    }

    /// <summary>Advances only requested lifecycle work, preserving unchanged material captures.</summary>
    public void Pump() => Coordinator.Pump(++tick);

    /// <summary>Invalidates existing cells through their lifecycle authority.</summary>
    public void InvalidateAll()
    {
        foreach (var cell in Coordinator.Cells(Instance)) Coordinator.Dirty(cell.Key);
    }
    #endregion

    #region Controlled publication
    /// <summary>Captures synthetic geometry and light while retaining production GPU packing.</summary>
    public void Publish(ControlledVoxelWorld world, Vector4? material = null, uint materialIdentity = 1)
    {
        PublishCells(position => new(!world.IsLoaded(position) ? 0u : world.GetBlock(position).BlockId == 0 ? 1u : 2u | (materialIdentity << 2), world.GetLight(position)));
        var value = material ?? new Vector4(1, 1, 1, 0);
        var data = new float[NearFieldMaterialRegistry.Width * NearFieldMaterialRegistry.Height * 4];
        for (int face = 0; face < 6; face++)
        {
            int i = (12 + face * 2) * 4;
            data[i] = value.X; data[i + 1] = value.Y; data[i + 2] = value.Z; data[i + 3] = 1;
            data[i + 4] = value.X * value.W; data[i + 5] = value.Y * value.W; data[i + 6] = value.Z * value.W;
        }
        Scene.Materials.UploadDataImmediate(data);
    }

    /// <summary>Uses production classification and material lookup for controlled game-access data.</summary>
    public void PublishCaptured(ControlledVoxelWorld world)
    {
        var accessor = ControlledBlockAccessor.Create(world);
        var position = new Vintagestory.API.MathTools.BlockPos(0);
        PublishCells(cell =>
        {
            position.Set(cell.X, cell.Y, cell.Z);
            return NearFieldCellCapture.Capture(accessor, world.GetBlock(cell), position, materials);
        });
    }

    /// <summary>Rebuilds current captures through immutable snapshots and acknowledged publication.</summary>
    private void PublishCells(Func<(int X, int Y, int Z), NearFieldSourceCell> capture)
    {
        provider.CaptureCell = capture;
        InvalidateAll();
        Pump();
    }

    /// <summary>Replays the most recent accepted completion to verify obsolete callbacks cannot publish.</summary>
    public Action CaptureCompletionReplay() => provider.CaptureCompletionReplay();
    #endregion

}
