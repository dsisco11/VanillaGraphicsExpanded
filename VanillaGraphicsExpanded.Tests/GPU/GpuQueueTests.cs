using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies bounded queue storage and ownership across real GPU completion and mapped decoding.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuQueueTests(HeadlessGLFixture fixture)
{
    #region Completion and ownership
    /// <summary>Known-count submissions expose only their active range and reuse retained storage for smaller batches.</summary>
    [Fact]
    public void KnownCountReusesStorageAndRestrictsActiveRange()
    {
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(8);
        Assert.False(queue.Pending);
        Assert.False(queue.TryRead(static values => values.ToArray(), out uint[] _));
        queue.WriteRecords([10, 20, 30, 40]);
        int size = queue.Buffer.SizeBytes;
        queue.Submit(4);
        Assert.True(queue.Pending);
        Assert.Equal(new uint[] { 10, 20, 30, 40 }, Read(queue));
        Assert.False(queue.Pending);
        queue.WriteRecords([99]);
        Assert.Equal(size, queue.Buffer.SizeBytes);
        queue.Submit(1);
        Assert.Equal(new uint[] { 99 }, Read(queue));
    }

    /// <summary>Submission ownership survives completion until decoding and unmapping finish.</summary>
    [Fact]
    public void PendingAndMappedQueueRejectsWritesAndResubmission()
    {
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(4, 16);
        queue.WriteHeader([1, 4, 0, 0]);
        queue.Buffer.UploadSubData<uint>([73], 16);
        queue.Submit();
        Assert.Throws<InvalidOperationException>(() => queue.Submit());
        Assert.Throws<InvalidOperationException>(() => queue.WriteHeader([0, 4, 0, 0]));
        GpuTestFence.WaitForGpuOrSkip("GPU queue mapped ownership");
        Assert.True(queue.TryRead(values =>
        {
            Assert.True(queue.Pending);
            Assert.Throws<InvalidOperationException>(() => queue.WriteHeader([0, 4, 0, 0]));
            Assert.Throws<InvalidOperationException>(() => queue.Submit());
            Assert.Throws<InvalidOperationException>(() => queue.TryRead(static nested => nested.Length, out _));
            Assert.Throws<InvalidOperationException>(() => queue.Dispose());
            return values[0];
        }, out uint result));
        Assert.Equal(73u, result);
        Assert.False(queue.Pending);
    }

    /// <summary>A failed decoder cannot allow unread or partially processed work to be silently overwritten.</summary>
    [Fact]
    public void DecoderFailurePoisonsReuseUntilDispose()
    {
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(4);
        queue.WriteRecords([17]);
        queue.Submit(1);
        GpuTestFence.WaitForGpuOrSkip("GPU queue decoder fault");
        Assert.Throws<ArithmeticException>(() => queue.TryRead<int>(static _ => throw new ArithmeticException(), out _));
        Assert.True(queue.Pending);
        Assert.Throws<InvalidOperationException>(() => queue.WriteRecords([5]));
        Assert.Throws<InvalidOperationException>(() => queue.Submit(1));
    }

    /// <summary>Disposal retires pending resources and is safe to repeat without waiting on the GPU.</summary>
    [Fact]
    public void PendingDisposeIsIdempotent()
    {
        fixture.MakeCurrent();
        var queue = new GpuQueue<uint>(4);
        queue.WriteRecords([17]);
        queue.Submit(1);
        var buffer = queue.Buffer;
        queue.Dispose();
        queue.Dispose();
        Assert.False(buffer.IsValid);
        Assert.Throws<ObjectDisposedException>(() => queue.WriteRecords([3]));
    }
    #endregion

    #region Bounds
    /// <summary>Each submission consumes its prepared input, including an explicitly empty known-count batch.</summary>
    [Fact]
    public void SubmissionRequiresFreshMatchingInput()
    {
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(4);
        Assert.Throws<InvalidOperationException>(() => queue.Submit());
        Assert.Throws<InvalidOperationException>(() => queue.Submit(0));
        queue.WriteRecords([1, 2]);
        Assert.Throws<InvalidOperationException>(() => queue.Submit(3));
        queue.Submit(2);
        Assert.Equal(2, Read(queue).Length);
        Assert.Throws<InvalidOperationException>(() => queue.Submit(2));
        queue.WriteRecords([]);
        queue.Submit(0);
        Assert.Empty(Read(queue));
        Assert.Throws<InvalidOperationException>(() => queue.Submit(0));
        using var append = new GpuQueue<uint>(4, 16);
        Assert.Throws<InvalidOperationException>(() => append.Submit());
        append.WriteHeader([0, 4, 0, 0]);
        Assert.Throws<InvalidOperationException>(() => append.Submit(0));
        append.Submit();
        Assert.Empty(Read(append));
        Assert.Throws<InvalidOperationException>(() => append.Submit());
    }

    /// <summary>GPU append counts are clamped to physical capacity, including empty and unsigned-overflow counts.</summary>
    [Theory]
    [InlineData(0u, 0)]
    [InlineData(2u, 2)]
    [InlineData(4u, 4)]
    [InlineData(uint.MaxValue, 4)]
    public void HeaderCountIsClamped(uint count, int expected)
    {
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(4, 16);
        Assert.Equal(32, queue.Buffer.SizeBytes);
        queue.WriteHeader([count, 4, 0, 0]);
        queue.Buffer.UploadSubData<uint>([11, 22, 33, 44], 16);
        queue.Submit();
        uint[] result = Read(queue);
        Assert.Equal(expected, result.Length);
        Assert.Equal(new uint[] { 11, 22, 33, 44 }.AsSpan(0, expected).ToArray(), result);
    }

    /// <summary>Invalid bounds and header sizes fail before they can overrun the allocated queue storage.</summary>
    [Fact]
    public void InvalidSizesAreRejected()
    {
        fixture.MakeCurrent();
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuQueue<uint>(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new GpuQueue<uint>(4, 3));
        using var counted = new GpuQueue<uint>(4, 16);
        Assert.Throws<ArgumentException>(() => counted.WriteHeader([0, 4]));
        Assert.Throws<ArgumentException>(() => counted.WriteHeader([0, 4, 0, 0, 0]));
        using var known = new GpuQueue<uint>(4);
        Assert.Throws<ArgumentException>(() => known.WriteRecords([1, 2, 3, 4, 5]));
        known.WriteRecords([1, 2]);
        Assert.Throws<InvalidOperationException>(() => known.Submit(5));
    }
    #endregion

    #region Helpers
    /// <summary>Waits only in the test harness, then decodes the real queue's completed mapped range.</summary>
    private static uint[] Read(GpuQueue<uint> queue)
    {
        GpuTestFence.WaitForGpuOrSkip("GPU queue test completion");
        Assert.True(queue.TryRead(static values => values.ToArray(), out uint[] result));
        return result;
    }
    #endregion
}
