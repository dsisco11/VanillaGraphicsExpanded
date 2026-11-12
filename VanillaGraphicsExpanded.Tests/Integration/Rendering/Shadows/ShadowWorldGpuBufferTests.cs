using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Threading;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Shadows.Geometry;
using VanillaGraphicsExpanded.Rendering.Shadows.World;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shadows;

/// <summary>Verifies the GL 4.5 world table and range records cross the RHI buffer boundary with owner-scoped generations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShadowWorldGpuBufferTests(HeadlessGLFixture fixture)
{
    private static int pendingFencePolls;
    private static int observedPendingFencePolls;

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

    /// <summary>Growing uploads charge old and new storage through retirement, then reuse the grown storage for smaller data.</summary>
    [Fact]
    public void GrowingPublicationTracksRetirementAndSmallerOwnerReusesCapacity()
    {
        // Arrange: begin with one table/range record, then describe a larger complete publication.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var buffers = new ShadowWorldGpuBuffers();
        var first = Publication(11, 101, Entry(1, 10, 0), new ShadowGeometryRange(1, 2, 3, 4, 5, 0, 6, 7));
        var grown = new ShadowWorldPublication(12, true, null,
            ImmutableArray.Create(Entry(2, 20, 0), Entry(3, 30, 1)),
            ImmutableArray.Create(new ShadowGeometryRange(2, 4, 6, 8, 10, 1, 12, 14),
                new ShadowGeometryRange(3, 6, 9, 12, 15, 2, 18, 21)),
            ImmutableDictionary<int, ShadowBounds>.Empty, 2) { Owner = 202 };

        // Act: preflight and upload the initial and growing capacities while charging both live generations.
        Assert.Equal(96, buffers.AdditionalBytesFor(first));
        Assert.True(buffers.Upload(first));
        var oldTable = buffers.Table;
        var oldRanges = buffers.Ranges;
        Assert.Equal(96, buffers.AllocatedBytes);
        Assert.Equal(192, buffers.AdditionalBytesFor(grown));
        Assert.True(buffers.Upload(grown));
        var currentTable = buffers.Table;
        var currentRanges = buffers.Ranges;
        Assert.NotSame(oldTable, currentTable);
        Assert.NotSame(oldRanges, currentRanges);
        Assert.Equal(288, buffers.AllocatedBytes);

        // Hold retirement pending so the next preflight proves the old allocation stays charged and owned.
        Interlocked.Exchange(ref pendingFencePolls, 100);
        Interlocked.Exchange(ref observedPendingFencePolls, 0);
        var harmony = new Harmony("Tests.ShadowWorldBufferRetirementPendingFence");
        try
        {
            harmony.Patch(AccessTools.Method(typeof(OpenGLBackend), "WaitCpu"),
                prefix: new HarmonyMethod(typeof(ShadowWorldGpuBufferTests), nameof(KeepNextFencePending)));
            var smaller = Publication(13, 303, Entry(-9, 0xABCDEF01, 0),
                new ShadowGeometryRange(9, 40, 50, 60, 70, 2, 20, 21));
            Assert.Equal(0, buffers.AdditionalBytesFor(smaller));
            Assert.True(buffers.Upload(smaller));

            // Assert: fixed-capacity upload changes ownership/data without orphaning another GPU generation.
            Assert.True(Interlocked.Exchange(ref pendingFencePolls, 0) > 0);
            Assert.True(Interlocked.Exchange(ref observedPendingFencePolls, 0) >= 2);
            Assert.Same(currentTable, buffers.Table);
            Assert.Same(currentRanges, buffers.Ranges);
            Assert.True(oldTable.IsValid);
            Assert.True(oldRanges.IsValid);
            Assert.Equal(288, buffers.AllocatedBytes);
            Assert.True(buffers.Matches(smaller));
            Assert.Equal(smaller.Table[0], Read<ShadowChunkGpu>(buffers.Table, 1)[0]);
            Assert.Equal(smaller.Ranges[0], Read<ShadowGeometryRange>(buffers.Ranges, 1)[0]);
        }
        finally { harmony.UnpatchAll(harmony.Id); Interlocked.Exchange(ref pendingFencePolls, 0); }

        // Act: complete the real fence and let the bounded retired pair leave the byte ledger.
        RHICommandContext.Current.WaitIdle();
        Assert.Equal(0, buffers.AdditionalBytesFor(Publication(14, 404, Entry(4, 40, 0), default)));

        // Assert: only the reused grown capacity remains charged after retirement completes.
        Assert.Equal(192, buffers.AllocatedBytes);
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

    /// <summary>Forces a bounded number of render-thread polls to remain pending without sleeping or replacing fences.</summary>
    private static bool KeepNextFencePending(ref CompletionStatus __result)
    {
        if (Interlocked.Decrement(ref pendingFencePolls) < 0) return true;
        Interlocked.Increment(ref observedPendingFencePolls);
        __result = CompletionStatus.Pending;
        return false;
    }

    /// <summary>Maps the current GPU-owned buffer and copies the requested records for exact readback assertions.</summary>
    private static T[] Read<T>(GpuShaderStorageBuffer buffer, int count) where T : unmanaged
    {
        using var mapped = buffer.MapRange<T>(dstOffsetBytes: 0, elementCount: count, access: BufferMapAccess.Read);
        Assert.True(mapped.IsMapped);
        return mapped.Span.ToArray();
    }
    #endregion
}
