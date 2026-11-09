using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Verifies deferred cleanup is thread-safe and isolated to the originating context lifetime.</summary>
public sealed class RHIResourceDisposalQueueTests
{
    #region Public API
    /// <summary>Concurrent admissions retain resources until drain and duplicate admissions dispose once.</summary>
    [Fact]
    public void ConcurrentEnqueueDefersExactlyOnceDisposalUntilDrain()
    {
        // Arrange: Queue one borrowed resource through many duplicate concurrent admissions.
        var queue = new RHIResourceDisposalQueue();
        var resource = new ObservedResource();
        // Act: Enqueue duplicate owners.
        Parallel.For(0, 32, _ => queue.Enqueue(resource));
        // Assert: Admission retains the resource until draining begins.
        Assert.False(resource.IsDisposed);
        // Act: Drain the queued owner on the current thread.
        queue.DrainPending();
        // Assert: Draining disposes the resource exactly once on this thread.
        Assert.Equal(1, resource.Disposals);
        Assert.Equal(Environment.CurrentManagedThreadId, resource.DisposalThread);
    }

    /// <summary>A failed release cannot strand later resources during drain or context shutdown.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CleanupFailureDoesNotStrandFollowingResource(bool close)
    {
        // Arrange: Queue a throwing cleanup before a resource whose cleanup must still run.
        var queue = new RHIResourceDisposalQueue();
        var later = new ObservedResource();
        queue.Enqueue(new ThrowingResource());
        queue.Enqueue(later);
        // Act: Drain directly or close the owning context with pending work.
        if (close) queue.Close(true);
        else queue.DrainPending();
        // Assert: The later resource is processed after the injected failure.
        Assert.Equal(1, later.Disposals);
    }
    /// <summary>Closing a context rejects late cleanup and cannot transfer old names into a new context.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClosedQueueRejectsLateFinalizerAdmissions(bool canDispose)
    {
        // Arrange: Queue one pending resource and keep a second for late admission.
        var queue = new RHIResourceDisposalQueue();
        var pending = new ObservedResource();
        var late = new ObservedResource();
        queue.Enqueue(pending);
        // Act: Close with the selected policy, then attempt a late enqueue and drain.
        queue.Close(canDispose);
        queue.Enqueue(late);
        queue.DrainPending();
        // Assert: Close applies only its pending-work policy and rejects late ownership.
        Assert.True(queue.IsClosed);
        Assert.Equal(canDispose ? 1 : 0, pending.Disposals);
        Assert.Equal(0, late.Disposals);
    }
    /// <summary>A scheduled cleanup batch retires each owner once and never carries names into a later queue.</summary>
    [Fact]
    public void OwnerScheduledCleanupIsIdempotentAndContextLocal()
    {
        // Arrange: Enqueue duplicate and distinct resources under one disposal queue.
        var queue = new RHIResourceDisposalQueue();
        using var first = new ObservedResource();
        using var second = new ObservedResource();
        queue.Enqueue(first); queue.Enqueue(first); queue.Enqueue(second);
        // Act: Drain the queued owners once.
        queue.DrainPending();
        // Assert: The first drain releases each resource exactly once.
        Assert.Equal(1, first.Disposals);
        Assert.Equal(1, second.Disposals);
        // Act: Drain an already empty queue again.
        queue.DrainPending();
        // Assert: Repeated draining does not invoke cleanup again.
        Assert.Equal(1, first.Disposals);
        // Act: Close the old queue, reject a late resource, and drain a replacement queue.
        queue.Close(true);
        var replacement = new RHIResourceDisposalQueue();
        using var late = new ObservedResource();
        queue.Enqueue(late);
        replacement.DrainPending();
        // Assert: A later queue cannot dispose names rejected by the old closed queue.
        Assert.False(late.IsDisposed);
    }
    #endregion

    #region Private
    /// <summary>Models a resource whose managed cleanup reports failure before normal release.</summary>
    private sealed class ThrowingResource : GpuResource
    {
        #region Public API
        /// <summary>Gets the current synthetic resource identifier.</summary>
        public override nint ResourceId { get; protected set; }
        /// <summary>Accepts labels without driver interaction.</summary>
        public override void SetDebugName(string? debugName) { }

        #endregion

        #region Protected/Internal
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <summary>Injects a cleanup failure for queue isolation coverage.</summary>
        protected override void OnBeforeDelete(nint id) => throw new InvalidOperationException("Injected cleanup failure");
        #endregion
    }
    /// <summary>Records disposal without creating a GL object or invoking graphics APIs.</summary>
    private sealed class ObservedResource : GpuResource
    {
        #region Public API
        /// <summary>Gets the number of completed cleanup callbacks.</summary>
        public int Disposals { get; private set; }
        /// <summary>Gets the managed thread that completed cleanup.</summary>
        public int DisposalThread { get; private set; }
        /// <summary>Gets the current synthetic resource identifier.</summary>
        public override nint ResourceId { get; protected set; } = 1;

        /// <summary>Accepts unused labels because this probe has no driver object.</summary>
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected/Internal
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <summary>Records the thread and number of completed lifecycle releases.</summary>
        protected override void OnAfterDelete()
        {
            Disposals++;
            DisposalThread = Environment.CurrentManagedThreadId;
        }
        #endregion
    }
    #endregion
}
