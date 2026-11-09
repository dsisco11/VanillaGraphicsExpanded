using VanillaGraphicsExpanded.Rendering;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies bounded queue storage and ownership across real GPU completion and mapped decoding.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuQueueTests(HeadlessGLFixture fixture)
{
    #region Completion and ownership
    /// <summary>Known-count submissions expose only their active range and reuse retained storage for smaller batches.</summary>
    [Fact]
    public void KnownCountReusesStorageAndRestrictsActiveRange()
    {
        // Arrange: Begin with an empty known-count queue and write a four-record batch.
        // Act: Submit/read that batch, then reuse the queue for one record.
        // Assert: Reads contain only submitted records and the smaller batch retains the same buffer size.
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
        // Arrange: Submit one value and wait until its completed range is being decoded.
        // Act: Attempt writes, nested reads, resubmission, and disposal while ownership is held.
        // Assert: Each conflicting operation is rejected; decoding returns the value and releases Pending.
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
        // Arrange: Submit a single record and wait for GPU completion before decoding.
        // Act: Make the decoder throw, then try to overwrite or resubmit the queue.
        // Assert: The decode error propagates and the queue remains pending and rejects reuse.
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
        // Arrange: Submit a record without reading the pending result.
        // Act: Dispose twice, then attempt another write.
        // Assert: The backing buffer is invalidated and later writes report disposal.
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
    /// <summary>GPU-only preparation follows the same active range and ownership contract without uploading dummy input.</summary>
    [Fact]
    public void GpuPreparationEnforcesBoundsAndPendingOwnership()
    {
        // Arrange: Create known-count and append queues with their respective header requirements.
        // Act: Reject invalid preparation/counts, then upload two GPU-written values and submit.
        // Assert: Valid GPU-only preparation reads the active values while pending preparation is rejected.
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(4);
        using var append = new GpuQueue<uint>(4, 16);
        Assert.Throws<ArgumentException>(() => append.PrepareGpuWrite(1));
        Assert.Throws<ArgumentException>(() => queue.PrepareGpuWrite(5));
        Assert.Throws<ArgumentException>(() => queue.PrepareGpuWrite(-1));
        queue.PrepareGpuWrite(2);
        Assert.Throws<InvalidOperationException>(() => queue.Submit(3));
        queue.Buffer.UploadSubData<uint>([37, 91], 0);
        queue.Submit(2);
        Assert.Throws<InvalidOperationException>(() => queue.PrepareGpuWrite(1));
        Assert.Equal(new uint[] { 37, 91 }, Read(queue));
    }

    /// <summary>Gradual growth retains spare storage, caps at a non-power-of-two bound and never exposes spare records.</summary>
    [Fact]
    public void GradualGrowthRetainsBoundedStorageAndActiveData()
    {
        // Arrange: Define batches that grow to the queue's non-power-of-two maximum, then shrink.
        // Act: Upload distinct values for each generation and read each submitted count.
        // Assert: Capacity follows the recorded growth sequence, never exceeds the limit, and spare slots are omitted.
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(7);
        int[] counts = [1, 2, 3, 4, 5, 6, 7, 2];
        int[] retainedCounts = [1, 2, 4, 4, 7, 7, 7, 7];
        for (int batch = 0; batch < counts.Length; batch++)
        {
            // Distinct generations reveal stale storage if readback accidentally includes retained capacity.
            uint[] values = Enumerable.Range(0, counts[batch]).Select(index => (uint)(batch * 100 + index)).ToArray();
            queue.WriteRecords(values);
            Assert.Equal(retainedCounts[batch] << 2, queue.Buffer.SizeBytes);
            queue.Submit(values.Length);
            Assert.Equal(values, Read(queue));
        }
    }

    /// <summary>A large jump allocates enough for the requested batch rather than limiting growth to one doubling.</summary>
    [Fact]
    public void DirectGrowthAccommodatesLargeBatch()
    {
        // Arrange: Allocate a small initial queue and submit one record.
        // Act: Grow directly to a nine-record batch and read it back.
        // Assert: Storage fits the requested batch and readback preserves all nine values.
        fixture.MakeCurrent();
        using var queue = new GpuQueue<uint>(11);
        queue.WriteRecords([42]);
        Assert.Equal(sizeof(uint), queue.Buffer.SizeBytes);
        queue.Submit(1);
        Assert.Equal(new uint[] { 42 }, Read(queue));
        uint[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9];
        queue.WriteRecords(values);
        Assert.Equal(values.Length << 2, queue.Buffer.SizeBytes);
        queue.Submit(values.Length);
        Assert.Equal(values, Read(queue));
    }

    /// <summary>Each submission consumes its prepared input, including an explicitly empty known-count batch.</summary>
    [Fact]
    public void SubmissionRequiresFreshMatchingInput()
    {
        // Arrange: Create known-count and append queues with no prepared input.
        // Act: Vary writes, counts, and empty submissions, including append-header preparation.
        // Assert: Only matching fresh input permits submission; every successful read returns its active range.
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
        // Arrange: Fill all four physical slots and write the theory's append count into the header.
        // Act: Submit and read the resulting range.
        // Assert: Output length is the clamped expected count and values match that prefix.
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
        // Arrange: Construct queues with valid and invalid capacities/header widths.
        // Act: Try oversized records, malformed headers, and a count beyond prepared input.
        // Assert: Invalid arguments or state are rejected before queue storage can be overrun.
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
