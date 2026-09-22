using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.LumOn.Scene.Geometry;

/// <summary>One ring of coherent geometry, dual lighting and material tables for both consumers.</summary>
internal sealed class TraceGeometryGpuScene : ITraceGeometryBackend
{
    private readonly PartitionRequest?[] owners;
    private TraceGeometryCoverage? coverage;
    public TraceGeometryCoverage? Coverage => coverage;
    public int Resolution { get; }
    public long TablesRevision { get; private set; } = -1;
    public long UploadedBytes { get; private set; }
    public long Revision { get; private set; }
    public long InvalidationRevision { get; private set; }
    public Texture3D Geometry { get; }
    public Texture3D Legacy { get; }
    public Texture3D Light { get; }
    public Texture3D Readiness { get; }
    public Texture2D Faces { get; }
    public Texture2D Materials { get; }
    public Texture2D Surfaces { get; }
    public Texture2D LightColors { get; }
    public Texture2D BlockLevels { get; }
    public Texture2D SunLevels { get; }
    public long TextureBytes => Resolution * (long)Resolution * Resolution * 12 + owners.Length + 2097152 + 644;

    #region Lifetime
    /// <summary>Allocates exact contract formats and clears readiness before any voxel can be sampled.</summary>
    public TraceGeometryGpuScene(int resolution)
    {
        if (resolution < 16 || resolution % 16 != 0) throw new ArgumentOutOfRangeException(nameof(resolution));
        Resolution = resolution;
        int slots = resolution / 16;
        owners = new PartitionRequest[slots * slots * slots];
        Geometry = Texture3D.Create(resolution, resolution, resolution, PixelInternalFormat.R32ui, TextureFilterMode.Nearest);
        Legacy = Texture3D.Create(resolution, resolution, resolution, PixelInternalFormat.R32ui, TextureFilterMode.Nearest);
        Light = Texture3D.Create(resolution, resolution, resolution, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest);
        Readiness = Texture3D.Create(slots, slots, slots, PixelInternalFormat.R8ui, TextureFilterMode.Nearest);
        Faces = Texture2D.Create(16384, 1, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest);
        Materials = Texture2D.Create(256, 768, PixelInternalFormat.Rgba8, TextureFilterMode.Nearest);
        Surfaces = Texture2D.Create(256, 256, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest);
        LightColors = Texture2D.Create(64, 1, PixelInternalFormat.Rgba16f, TextureFilterMode.Nearest);
        BlockLevels = Texture2D.Create(33, 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest);
        SunLevels = Texture2D.Create(33, 1, PixelInternalFormat.R16f, TextureFilterMode.Nearest);
        Readiness.UploadDataImmediate(new byte[owners.Length], 0, 0, 0, slots, slots, slots);
        BlockLevels.UploadDataImmediate(ScalarTable()); SunLevels.UploadDataImmediate(ScalarTable());
        UploadedBytes = owners.Length + 66 * sizeof(float);
    }

    /// <summary>Matches the existing TraceScene linear scalar LUT rather than changing its shading scale.</summary>
    private static float[] ScalarTable()
    {
        var result = new float[33];
        for (int i = 0; i < result.Length; i++) result[i] = i / 32f;
        return result;
    }

    /// <summary>Releases all storage on the render thread after registration retirement.</summary>
    public void Dispose()
    {
        Geometry.Dispose(); Legacy.Dispose(); Light.Dispose(); Readiness.Dispose(); Faces.Dispose(); Materials.Dispose();
        Surfaces.Dispose(); LightColors.Dispose(); BlockLevels.Dispose(); SunLevels.Dispose();
    }
    #endregion

    #region Coherent publication
    /// <summary>Streams immutable table rows within coordinator credit; published entries remain immutable.</summary>
    public void UploadTableRange(TraceGeometryTables tables, int offset, int bytes)
    {
        if (offset < 0 || offset % 4096 != 0 || bytes <= 0 || (long)offset + bytes > TraceGeometryTables.MaximumUploadBytes ||
            (bytes % 4096 != 0 && (long)offset + bytes != TraceGeometryTables.MaximumUploadBytes))
            throw new ArgumentOutOfRangeException(nameof(offset));
        int end = offset + bytes;
        while (offset < end)
        {
            int length;
            if (offset < 262144)
            {
                length = Math.Min(end, 262144) - offset;
                Faces.UploadDataImmediate(tables.Faces.Slice(offset / 4, length / 4).ToArray(), offset / 16, 0, length / 16, 1);
            }
            else if (offset < 1048576)
            {
                length = Math.Min(end, 1048576) - offset;
                int local = offset - 262144;
                Materials.UploadDataImmediate(tables.Colors.Slice(local, length).ToArray(), 0, local / 1024, 256, length / 1024);
            }
            else if (offset < 2097152)
            {
                length = Math.Min(end, 2097152) - offset;
                int local = offset - 1048576;
                Surfaces.UploadDataImmediate(tables.Surfaces.Slice(local / 4, length / 4).ToArray(), 0, local / 4096, 256, length / 4096);
            }
            else
            {
                length = 1024;
                LightColors.UploadDataImmediate(tables.Lights.ToArray());
            }
            offset += length; UploadedBytes += length;
        }
        if (end == TraceGeometryTables.MaximumUploadBytes) TablesRevision = tables.Revision;
    }

    /// <summary>Clears departed owners before publishing the new physical address envelope.</summary>
    public void SetWindow(TraceGeometryCoverage next)
    {
        if (next.Resolution != Resolution) throw new ArgumentException("Window requires a new backend.");
        // Logical domains can move while every physical slot remains resident. Histories must
        // observe that mapping change even when no owner is evicted from the shared ring.
        if (coverage != null && coverage != next)
        {
            Revision++;
            if (coverage.Surface != next.Surface) InvalidationRevision++;
        }
        for (int i = 0; i < owners.Length; i++)
            if (owners[i] is { } owner && !Contains(next, owner.Key.Coordinate)) { Clear(owner.Key.Coordinate); owners[i] = null; }
        coverage = next;
    }

    /// <summary>Writes tables and every companion before readiness; exceptions leave the claimed cell unpublished.</summary>
    public bool Publish(PartitionRequest request, TraceGeometryCell cell, TraceGeometryTables tables)
    {
        if (coverage == null || TablesRevision != tables.Revision || request.Cancellation.IsCancellationRequested || !Contains(coverage, request.Key.Coordinate)) return false;
        var c = request.Key.Coordinate;
        int slot = Slot(c);
        Clear(c); owners[slot] = request;
        int x = Wrap(c.X) * 16, y = Wrap(c.Y) * 16, z = Wrap(c.Z) * 16;
        Geometry.UploadDataImmediate(cell.Geometry.ToArray(), x, y, z, 16, 16, 16);
        Legacy.UploadDataImmediate(cell.Legacy.ToArray(), x, y, z, 16, 16, 16);
        Light.UploadDataImmediate(cell.Light.ToArray(), x, y, z, 16, 16, 16);
        UploadedBytes += TraceGeometryCell.UploadBytes;
        GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
        if (request.Cancellation.IsCancellationRequested || owners[slot] != request) return false;
        Readiness.UploadDataImmediate(new byte[] { 1 }, Wrap(c.X), Wrap(c.Y), Wrap(c.Z), 1, 1, 1);
        UploadedBytes++; Revision++;
        return true;
    }

    /// <summary>Clears only this key's slot, so retiring an old key cannot invalidate its replacement.</summary>
    public void Invalidate(in PartitionCellKey key)
    {
        int slot = Slot(key.Coordinate);
        if (owners[slot]?.Key != key) return;
        Clear(key.Coordinate); owners[slot] = null;
    }

    /// <summary>Checks the logical coordinate before modulo addressing can alias another owner.</summary>
    private static bool Contains(TraceGeometryCoverage plan, in PartitionCoordinate c) =>
        c.X * 16 >= plan.Window.Min.X && c.Y * 16 >= plan.Window.Min.Y && c.Z * 16 >= plan.Window.Min.Z &&
        (c.X + 1) * 16 <= plan.Window.Max.X && (c.Y + 1) * 16 <= plan.Window.Max.Y && (c.Z + 1) * 16 <= plan.Window.Max.Z;

    /// <summary>Writes readiness invalidation before recycling or rebuilding storage.</summary>
    private void Clear(in PartitionCoordinate c)
    {
        if (owners[Slot(c)] != null) InvalidationRevision++;
        Readiness.UploadDataImmediate(new byte[1], Wrap(c.X), Wrap(c.Y), Wrap(c.Z), 1, 1, 1); UploadedBytes++; Revision++;
    }

    /// <summary>Wraps fixed-zero publication coordinates into the physical ring.</summary>
    private int Wrap(long value) => (int)((value % (Resolution / 16) + Resolution / 16) % (Resolution / 16));

    /// <summary>Maps three ring axes to the backend's owner array.</summary>
    private int Slot(in PartitionCoordinate c) => (Wrap(c.Z) * (Resolution / 16) + Wrap(c.Y)) * (Resolution / 16) + Wrap(c.X);
    #endregion
}
