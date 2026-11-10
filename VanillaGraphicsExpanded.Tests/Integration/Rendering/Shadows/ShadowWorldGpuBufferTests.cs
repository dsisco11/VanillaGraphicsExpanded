using System.Collections.Immutable;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Verifies the GL 4.5 world table and range records cross the RHI buffer boundary with owner-scoped generations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowWorldGpuBufferTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Uploads exact CPU/GPU record strides and replaces bytes when a new owner reuses a numeric generation.</summary>
    [Fact]
    public void OwnerChangeWithSameGenerationUploadsAndReadsBackNewLookup()
    {
        // Arrange: establish the repository's real OpenGL fixture and verify packed std430 contracts.
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        Assert.Equal(64, Marshal.SizeOf<ShadowChunkGpu>());
        Assert.Equal(32, Marshal.SizeOf<ShadowGeometryRange>());
        Assert.Equal(0, Marshal.OffsetOf<ShadowChunkGpu>(nameof(ShadowChunkGpu.X)).ToInt32());
        Assert.Equal(16, Marshal.OffsetOf<ShadowChunkGpu>(nameof(ShadowChunkGpu.NativeLow)).ToInt32());
        Assert.Equal(40, Marshal.OffsetOf<ShadowChunkGpu>(nameof(ShadowChunkGpu.Flags)).ToInt32());
        Assert.Equal(48, Marshal.OffsetOf<ShadowChunkGpu>(nameof(ShadowChunkGpu.RangeOffset)).ToInt32());

        using var buffers = new ShadowWorldGpuBuffers();
        var firstEntry = Entry(2, 0x12345678, 4);
        var firstRange = new ShadowGeometryRange(3, 12, 18, 24, 30, 1, 7, 8);
        var entryBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref firstEntry, 1));
        var rangeBytes = MemoryMarshal.AsBytes(MemoryMarshal.CreateReadOnlySpan(ref firstRange, 1));
        Assert.Equal(64, entryBytes.Length);
        Assert.Equal(32, rangeBytes.Length);
        Assert.Equal(2, MemoryMarshal.Read<int>(entryBytes.Slice(0, 4)));
        Assert.Equal(0x12345678u, MemoryMarshal.Read<uint>(entryBytes.Slice(16, 4)));
        Assert.Equal(3, MemoryMarshal.Read<int>(entryBytes.Slice(40, 4)));
        Assert.Equal(4u, MemoryMarshal.Read<uint>(entryBytes.Slice(48, 4)));
        Assert.Equal(7u, MemoryMarshal.Read<uint>(rangeBytes.Slice(24, 4)));
        Assert.Equal(8u, MemoryMarshal.Read<uint>(rangeBytes.Slice(28, 4)));
        var first = Publication(11, 101, firstEntry, firstRange);

        // Act: read back a real GL SSBO upload, then reuse the number 11 for a different native owner.
        Assert.True(buffers.Upload(first));
        var firstGpuEntry = Read<ShadowChunkGpu>(buffers.Table, 1)[0];
        var firstGpuRange = Read<ShadowGeometryRange>(buffers.Ranges, 1)[0];
        var secondEntry = Entry(-9, 0xABCDEF01, 0);
        var secondRange = new ShadowGeometryRange(9, 40, 50, 60, 70, 2, 20, 21);
        var replacement = Publication(11, 202, secondEntry, secondRange);
        Assert.True(buffers.Upload(replacement));
        var replacementGpuEntry = Read<ShadowChunkGpu>(buffers.Table, 1)[0];
        var replacementGpuRange = Read<ShadowGeometryRange>(buffers.Ranges, 1)[0];

        // Assert: current native ownership data, ranges and exact allocation bytes survive the GPU round trip.
        Assert.Equal(firstEntry, firstGpuEntry);
        Assert.Equal(firstRange, firstGpuRange);
        Assert.Equal(secondEntry, replacementGpuEntry);
        Assert.Equal(secondRange, replacementGpuRange);
        Assert.Equal(96, buffers.AllocatedBytes);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Creates one table record with fields that expose word-order or stride errors.</summary>
    private static ShadowChunkGpu Entry(int x, uint native, uint rangeOffset) => new()
    {
        X = x, Y = -17, Z = 2049, Dimension = 2,
        NativeLow = native, NativeHigh = 0x76543210, LifetimeLow = 31, LifetimeHigh = 32,
        RevisionLow = 41, RevisionHigh = 42, Flags = 3, Connectivity = 0x7FFF,
        RangeOffset = rangeOffset, RangeCount = 1, Reserved0 = 0xF00D, Reserved1 = 0xBEEF
    };

    /// <summary>Creates a complete immutable publication with a caller-selected owner and unchanged revision number.</summary>
    private static ShadowWorldPublication Publication(long generation, long owner, ShadowChunkGpu entry,
        ShadowGeometryRange range) => new(generation, true, null, ImmutableArray.Create(entry),
            ImmutableArray.Create(range), ImmutableDictionary<int, ShadowBounds>.Empty, 1) { Owner = owner };

    /// <summary>Maps the current GPU-owned buffer and copies the requested records for exact readback assertions.</summary>
    private static T[] Read<T>(GpuShaderStorageBuffer buffer, int count) where T : unmanaged
    {
        using var mapped = buffer.MapRange<T>(dstOffsetBytes: 0, elementCount: count, access: BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }
    #endregion
}
