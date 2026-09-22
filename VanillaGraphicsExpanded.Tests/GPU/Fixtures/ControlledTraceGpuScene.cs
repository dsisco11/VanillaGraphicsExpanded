using System.Numerics;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Controlled voxel input for shader tests; publication uses the shared production GPU backend.</summary>
internal readonly record struct ControlledTraceVoxel(uint Geometry, Vector4 Light);

/// <summary>Adapts test-authored cells to shared geometry without retaining the retired GPU implementation.</summary>
internal sealed class ControlledTraceGpuScene : IDisposable
{
    private TraceGeometryMaterials? uploadedMaterials;
    public TraceGeometryGpuScene Backend { get; }
    public int Resolution => Backend.Resolution;
    public int CellSize => 16;
    public int RegionResolution => Resolution / 16;
    public VectorInt3 Origin { get; private set; }
    public long Revision => Backend.Revision;
    public Texture3D Geometry => Backend.Geometry;
    public Texture3D Light => Backend.Light;
    public Texture3D Regions => Backend.Readiness;
    public Texture2D Materials => Backend.Materials;

    #region Controlled publication
    /// <summary>Allocates only the real shared backend.</summary>
    public ControlledTraceGpuScene(int resolution) => Backend = new(resolution);

    /// <summary>Installs the explicit synthetic domain, including negative test heights.</summary>
    public void SetWindow(in VectorInt3 origin)
    {
        Origin = origin;
        var bounds = new PartitionBounds(new(origin.X, origin.Y, origin.Z), new(origin.X + Resolution, origin.Y + Resolution, origin.Z + Resolution));
        Backend.SetWindow(new(bounds, bounds, bounds, Resolution, int.MaxValue));
    }

    /// <summary>Checks whether a controlled capture lies within the selected domain.</summary>
    public bool ClaimCell(PartitionRequest request) => !request.Cancellation.IsCancellationRequested &&
        request.Key.Coordinate.X * 16 >= Origin.X && (request.Key.Coordinate.X + 1) * 16 <= (long)Origin.X + Resolution &&
        request.Key.Coordinate.Y * 16 >= Origin.Y && (request.Key.Coordinate.Y + 1) * 16 <= (long)Origin.Y + Resolution &&
        request.Key.Coordinate.Z * 16 >= Origin.Z && (request.Key.Coordinate.Z + 1) * 16 <= (long)Origin.Z + Resolution;

    /// <summary>Converts X/Z/Y fixture input to immutable native upload arrays and publishes through production storage.</summary>
    public bool PublishCell(PartitionRequest request, ReadOnlySpan<ControlledTraceVoxel> cells, TraceGeometryMaterials materials)
    {
        if (!ClaimCell(request)) return false;
        var geometry = new uint[4096]; var light = new byte[16384];
        for (int z = 0; z < 16; z++) for (int y = 0; y < 16; y++) for (int x = 0; x < 16; x++)
        {
            var cell = cells[(y * 16 + z) * 16 + x];
            int dest = (z * 16 + y) * 16 + x;
            geometry[dest] = cell.Geometry;
            uint packed = TraceGeometryVoxel.PackLight(cell.Light);
            for (int c = 0; c < 4; c++) light[dest * 4 + c] = (byte)(packed >> (8 * c));
        }
        var tables = materials.Snapshot();
        if (!ReferenceEquals(uploadedMaterials, materials) || Backend.TablesRevision != tables.Revision)
        {
            Backend.UploadTableRange(tables, 0, (int)TraceGeometryTables.MaximumUploadBytes);
            uploadedMaterials = materials;
        }
        return Backend.Publish(request, new(geometry, new uint[4096], light, geometry.Any(v => (v & 3) == 3)), tables);
    }
    /// <summary>Invalidates publication through the shared owner checks.</summary>
    public void InvalidateCell(in PartitionCellKey key) => Backend.Invalidate(key);
    /// <summary>Releases the shared textures.</summary>
    public void Dispose() => Backend.Dispose();
    #endregion
}
