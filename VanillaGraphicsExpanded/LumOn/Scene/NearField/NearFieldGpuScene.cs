using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.NearField;

/// <summary>Cell-aligned GPU ring; the partition coordinator authorizes all content publication.</summary>
internal sealed class NearFieldGpuScene : IDisposable, INearFieldPublicationBackend
{
    private readonly PartitionRequest?[] owners;
    private readonly bool[] ready;
    public int Resolution { get; }
    public int CellSize { get; }
    public int RegionResolution => Resolution / CellSize;
    public Texture3D Geometry { get; }
    public Texture3D Light { get; }
    public Texture3D Regions { get; }
    public Texture2D Materials { get; }
    public long Revision { get; private set; }
    public VectorInt3 Origin { get; private set; }
    public PartitionBounds? SupportedOrigins { get; set; }
    public float MaximumTraceReach { get; set; }

    #region Lifetime
    /// <summary>Allocates a bounded ring with independently configurable publication granularity.</summary>
    public NearFieldGpuScene(int requestedResolution, int cellSize = 16)
    {
        if (cellSize <= 0 || (cellSize & (cellSize - 1)) != 0) throw new ArgumentOutOfRangeException(nameof(cellSize));
        CellSize = cellSize;
        Resolution = Math.Max(cellSize, checked((requestedResolution + cellSize - 1) / cellSize * cellSize));
        Geometry = Texture3D.Create(Resolution, Resolution, Resolution, PixelInternalFormat.R32ui, TextureFilterMode.Nearest, debugName: "LumOn.NearField.Geometry");
        Light = Texture3D.Create(Resolution, Resolution, Resolution, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, debugName: "LumOn.NearField.Light");
        Regions = Texture3D.Create(RegionResolution, RegionResolution, RegionResolution, PixelInternalFormat.R8ui, TextureFilterMode.Nearest, debugName: "LumOn.NearField.Regions");
        Materials = Texture2D.Create(NearFieldMaterialRegistry.Width, NearFieldMaterialRegistry.Height,
            PixelInternalFormat.Rgba8, TextureFilterMode.Nearest, debugName: "LumOn.NearField.Materials");
        owners = new PartitionRequest[RegionResolution * RegionResolution * RegionResolution];
        ready = new bool[owners.Length];
        Regions.UploadDataImmediate(new byte[owners.Length], 0, 0, 0, RegionResolution, RegionResolution, RegionResolution);
    }

    /// <summary>Releases all GPU resources on their render thread.</summary>
    public void Dispose()
    {
        Geometry.Dispose(); Light.Dispose(); Regions.Dispose(); Materials.Dispose();
    }
    #endregion

    #region Ownership and publication
    /// <summary>Accepts the coordinator-selected world-block window and hides all departing slot owners.</summary>
    public void SetWindow(in VectorInt3 origin)
    {
        if (origin.X % CellSize != 0 || origin.Y % CellSize != 0 || origin.Z % CellSize != 0)
            throw new ArgumentException("Near-field window must align to publication cells.");
        if (Origin == origin) return;
        Origin = origin;
        for (int i = 0; i < owners.Length; i++)
        {
            if (owners[i] is not { } owner || ContainsCell(owner.Key.Coordinate)) continue;
            ClearSlot(owner.Key.Coordinate, i);
            owners[i] = null;
        }
        Revision++;
    }

    /// <summary>Checks logical coverage with integer world arithmetic before any ring lookup.</summary>
    public bool ContainsCell(in PartitionCoordinate cell) =>
        cell.X * CellSize >= Origin.X && cell.X * CellSize < (long)Origin.X + Resolution &&
        cell.Y * CellSize >= Origin.Y && cell.Y * CellSize < (long)Origin.Y + Resolution &&
        cell.Z * CellSize >= Origin.Z && cell.Z * CellSize < (long)Origin.Z + Resolution;

    /// <summary>Assigns a slot to a current coordinator request before uploading any payload.</summary>
    public bool ClaimCell(PartitionRequest request)
    {
        if (request.Cancellation.IsCancellationRequested || !ContainsCell(request.Key.Coordinate)) return false;
        int slot = Slot(request.Key.Coordinate);
        // Claims only originate from the coordinator's validated Publish callback; stale direct publications cannot claim implicitly.
        ClearSlot(request.Key.Coordinate, slot);
        owners[slot] = request;
        return true;
    }

    /// <summary>Uploads one claimed cell and exposes readiness only after all companion resources are coherent.</summary>
    public bool PublishCell(PartitionRequest request, ReadOnlySpan<NearFieldSourceCell> cells, NearFieldMaterialRegistry materials)
    {
        if (request.Cancellation.IsCancellationRequested || !ContainsCell(request.Key.Coordinate)) return false;
        var coordinate = request.Key.Coordinate;
        int slot = Slot(coordinate);
        if (owners[slot] != request) return false;
        if (cells.Length != CellSize * CellSize * CellSize) throw new ArgumentException("Incorrect cell payload size.");
        int sx = Mod(coordinate.X), sy = Mod(coordinate.Y), sz = Mod(coordinate.Z);
        var geometry = new uint[cells.Length];
        var light = new float[cells.Length * 4];
        // Source snapshots are X/Z/Y; OpenGL uploads use X/Y/Z.
        for (int z = 0; z < CellSize; z++)
        for (int y = 0; y < CellSize; y++)
        for (int x = 0; x < CellSize; x++)
        {
            NearFieldSourceCell c = cells[(y * CellSize + z) * CellSize + x];
            int dest = (z * CellSize + y) * CellSize + x;
            geometry[dest] = c.Geometry;
            light[dest * 4] = c.Light.X; light[dest * 4 + 1] = c.Light.Y;
            light[dest * 4 + 2] = c.Light.Z; light[dest * 4 + 3] = c.Light.W;
        }
        Geometry.UploadDataImmediate(geometry, sx * CellSize, sy * CellSize, sz * CellSize, CellSize, CellSize, CellSize);
        Light.UploadDataImmediate(light, sx * CellSize, sy * CellSize, sz * CellSize, CellSize, CellSize, CellSize);
        if (materials.TakeUpload() is { } upload) Materials.UploadDataImmediate(upload);
        GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        Regions.UploadDataImmediate(new byte[] { 1 }, sx, sy, sz, 1, 1, 1);
        ready[slot] = true;
        Revision++;
        return true;
    }

    /// <summary>Hides known-stale content immediately without releasing another cell's aliased slot.</summary>
    public void InvalidateCell(in PartitionCellKey key)
    {
        int slot = Slot(key.Coordinate);
        if (owners[slot]?.Key != key) return;
        ClearSlot(key.Coordinate, slot);
        owners[slot] = null;
    }

    /// <summary>Releases cell ownership before its physical slot can be reused.</summary>
    public void RetireCell(in PartitionCellKey key) => InvalidateCell(key);

    /// <summary>Writes readiness changes before a new mapping or owner can become visible.</summary>
    private void ClearSlot(in PartitionCoordinate coordinate, int slot)
    {
        Regions.UploadDataImmediate(new byte[1], Mod(coordinate.X), Mod(coordinate.Y), Mod(coordinate.Z), 1, 1, 1);
        if (ready[slot]) Revision++;
        ready[slot] = false;
    }

    /// <summary>Maps a logical cell to a physical ring index.</summary>
    private int Slot(in PartitionCoordinate coordinate) => (Mod(coordinate.Z) * RegionResolution + Mod(coordinate.Y)) * RegionResolution + Mod(coordinate.X);

    /// <summary>Wraps negative and positive cell coordinates identically.</summary>
    private int Mod(long coordinate) => (int)((coordinate % RegionResolution + RegionResolution) % RegionResolution);
    #endregion
}
