using System;
using System.Buffers;
using System.Threading;

namespace VanillaGraphicsExpanded.Voxels.ChunkProcessing;

public sealed class PooledChunkSnapshot<TVoxel> : IChunkSnapshot, IChunkSnapshotSizeInfo
{
    private TVoxel[]? buffer;

    public PooledChunkSnapshot(
        ChunkKey key,
        int version,
        int sizeX,
        int sizeY,
        int sizeZ,
        TVoxel[] buffer,
        int length)
    {
        if (sizeX < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeX));
        }

        if (sizeY < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeY));
        }

        if (sizeZ < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sizeZ));
        }

        if (buffer is null)
        {
            throw new ArgumentNullException(nameof(buffer));
        }

        if ((uint)length > (uint)buffer.Length)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        Key = key;
        Version = version;
        SizeX = sizeX;
        SizeY = sizeY;
        SizeZ = sizeZ;
        this.buffer = buffer;
        Length = length;
    }

    public ChunkKey Key { get; }

    public int Version { get; }

    public int SizeX { get; }

    public int SizeY { get; }

    public int SizeZ { get; }

    public int Length { get; }

    public ReadOnlyMemory<TVoxel> Voxels
    {
        get
        {
            TVoxel[]? buf = Volatile.Read(ref buffer);
            if (buf is null)
            {
                return ReadOnlyMemory<TVoxel>.Empty;
            }

            return buf.AsMemory(0, Length);
        }
    }

    public long EstimatedBytes => (long)Length * System.Runtime.CompilerServices.Unsafe.SizeOf<TVoxel>();

    public void Dispose()
    {
        TVoxel[]? buf = Interlocked.Exchange(ref buffer, null);
        if (buf is null)
        {
            return;
        }

        ArrayPool<TVoxel>.Shared.Return(buf);
    }

    public static PooledChunkSnapshot<TVoxel> Rent(ChunkKey key, int version, int sizeX, int sizeY, int sizeZ, int length)
    {
        if (length < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(length));
        }

        TVoxel[] buffer = ArrayPool<TVoxel>.Shared.Rent(length);
        return new PooledChunkSnapshot<TVoxel>(key, version, sizeX, sizeY, sizeZ, buffer, length);
    }
}
