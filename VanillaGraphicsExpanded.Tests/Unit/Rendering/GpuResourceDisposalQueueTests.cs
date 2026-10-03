using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies deferred cleanup is thread-safe and isolated to the originating context lifetime.</summary>
public sealed class GpuResourceDisposalQueueTests
{
    #region Public API
    /// <summary>The common resource identifier contract exposes live storage and clears it on either retirement path.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourceIdTracksDisposalAndDetachment(bool detach)
    {
        using GpuResource resource = new ObservedResource();
        Assert.Equal((nint)1, resource.ResourceId);
        Assert.True(resource.IsValid);

        if (detach) Assert.Equal((nint)1, resource.Detach());
        else resource.Dispose();

        Assert.Equal((nint)0, resource.ResourceId);
        Assert.False(resource.IsValid);
        Assert.True(resource.IsDisposed);
        Assert.Equal((nint)0, resource.Detach());
    }

    /// <summary>Concurrent admissions retain resources until drain and duplicate admissions dispose once.</summary>
    [Fact]
    public void ConcurrentEnqueueDefersExactlyOnceDisposalUntilDrain()
    {
        var queue = new GpuResourceDisposalQueue();
        var resource = new ObservedResource();
        Parallel.For(0, 32, _ => queue.Enqueue(resource));
        Assert.False(resource.IsDisposed);
        queue.DrainPending();
        Assert.Equal(1, resource.Disposals);
        Assert.Equal(Environment.CurrentManagedThreadId, resource.DisposalThread);
    }

    /// <summary>A failed release cannot strand later resources during drain or context shutdown.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CleanupFailureDoesNotStrandFollowingResource(bool close)
    {
        var queue = new GpuResourceDisposalQueue();
        var later = new ObservedResource();
        queue.Enqueue(new ThrowingResource());
        queue.Enqueue(later);
        if (close) queue.Close(true);
        else queue.DrainPending();
        Assert.Equal(1, later.Disposals);
    }
    /// <summary>Closing a context rejects late cleanup and cannot transfer old names into a new context.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ClosedQueueRejectsLateFinalizerAdmissions(bool canDispose)
    {
        var queue = new GpuResourceDisposalQueue();
        var pending = new ObservedResource();
        var late = new ObservedResource();
        queue.Enqueue(pending);
        queue.Close(canDispose);
        queue.Enqueue(late);
        queue.DrainPending();
        Assert.True(queue.IsClosed);
        Assert.Equal(canDispose ? 1 : 0, pending.Disposals);
        Assert.Equal(0, late.Disposals);
    }
    #endregion

    #region Private
    /// <summary>Models a resource whose managed cleanup reports failure before normal release.</summary>
    private sealed class ThrowingResource : GpuResource
    {
        /// <summary>Gets the current synthetic resource identifier.</summary>
        public override nint ResourceId { get; protected set; }
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        /// <summary>Accepts labels without driver interaction.</summary>
        public override void SetDebugName(string? debugName) { }
        /// <summary>Injects a cleanup failure for queue isolation coverage.</summary>
        protected override void OnBeforeDelete(nint id) => throw new InvalidOperationException("Injected cleanup failure");
    }
    /// <summary>Records disposal without creating a GL object or invoking graphics APIs.</summary>
    private sealed class ObservedResource : GpuResource
    {
        public int Disposals { get; private set; }
        public int DisposalThread { get; private set; }
        /// <summary>Gets the current synthetic resource identifier.</summary>
        public override nint ResourceId { get; protected set; } = 1;
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;
        protected override bool OwnsResource => false;

        /// <summary>Accepts unused labels because this probe has no driver object.</summary>
        public override void SetDebugName(string? debugName) { }

        /// <summary>Records the thread and number of completed lifecycle releases.</summary>
        protected override void OnAfterDelete()
        {
            Disposals++;
            DisposalThread = Environment.CurrentManagedThreadId;
        }
    }
    #endregion
}
