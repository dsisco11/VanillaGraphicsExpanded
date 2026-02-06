using System;
using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Threading;

using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.LumOn.Scene;

/// <summary>
/// Tiny dedicated UBO for bridging Vintage Story's matrix-space positions back to world-space voxel coordinates.
/// </summary>
internal static class LumOnTerrainBridgeUboState
{
    public const string BlockName = "LumOnTerrainBridgeUBO";
    public const int Binding = GpuBindingRegistry.Ubo.TerrainBridge;

    private const int UboSizeBytes = 32; // ivec4 + vec4 (std140)

    private static readonly byte[] bytes = new byte[UboSizeBytes];

    private static int version;
    private static GpuUniformBuffer? ubo;

    public static int Version => Volatile.Read(ref version);

    public static int BufferId => ubo?.BufferId ?? 0;

    public static GpuUniformBuffer? UboOrNull => (ubo is not null && ubo.BufferId != 0) ? ubo : null;

    public static void EnsureCreated()
    {
        if (ubo is not null && ubo.BufferId != 0)
        {
            return;
        }

        ubo?.Dispose();
        ubo = GpuUniformBuffer.Create(debugName: "LumOn.TerrainBridgeUBO");
    }

    public static void Dispose()
    {
        ubo?.Dispose();
        ubo = null;
        Array.Clear(bytes);
        Interlocked.Increment(ref version);
    }

    public static void Update(VectorInt3 worldChunkCoordOffset, Vector3d worldBlockOffsetRem)
    {
        EnsureCreated();

        int offset = 0;
        WriteIvec4(bytes, offset, worldChunkCoordOffset.X, worldChunkCoordOffset.Y, worldChunkCoordOffset.Z, 0); offset += 16;
        WriteVec4(bytes, offset, (float)worldBlockOffsetRem.X, (float)worldBlockOffsetRem.Y, (float)worldBlockOffsetRem.Z, 0f); offset += 16;

        if (offset != UboSizeBytes)
        {
            throw new InvalidOperationException($"Terrain bridge UBO packing size mismatch: wrote {offset} bytes, expected {UboSizeBytes}.");
        }

        ubo!.UploadOrResize(bytes, UboSizeBytes, growExponentially: false);
        Interlocked.Increment(ref version);
    }

    private static void WriteVec4(byte[] dst, int byteOffset, float x, float y, float z, float w)
    {
        Span<byte> b = dst.AsSpan(byteOffset, 16);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteSingleLittleEndian(b.Slice(12, 4), w);
    }

    private static void WriteIvec4(byte[] dst, int byteOffset, int x, int y, int z, int w)
    {
        Span<byte> b = dst.AsSpan(byteOffset, 16);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(0, 4), x);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(4, 4), y);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(8, 4), z);
        BinaryPrimitives.WriteInt32LittleEndian(b.Slice(12, 4), w);
    }
}
