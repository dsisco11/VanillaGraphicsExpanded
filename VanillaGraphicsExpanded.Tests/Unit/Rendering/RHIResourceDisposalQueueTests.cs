using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks GPU resource identifier and disposal contracts without queue or graphics-context behavior.</summary>
public sealed class RHIResourceDisposalQueueTests
{
    #region Public API
    /// <summary>Foreign-thread diagnostics report owner misuse in DEBUG without changing resource lifetime.</summary>
    [Fact]
    public void OwnerThreadDiagnosticDoesNotSupplyLifetime()
    {
        // Arrange: Keep one synthetic resource alive and capture any worker-thread diagnostic.
        using var resource = new ObservedResource();
        Exception? failure = null;
        var worker = new Thread(() =>
        {
            try { resource.ValidateOwnerThread(); }
            catch (Exception error) { failure = error; }
        });

        // Act: Validate ownership from a worker and wait for that call to finish.
        worker.Start();
        worker.Join();

        // Assert: Diagnostics vary by build mode but never dispose or invalidate the resource.
#if DEBUG
        Assert.IsType<InvalidOperationException>(failure);
#else
        Assert.Null(failure);
#endif
        Assert.False(resource.IsDisposed);
        Assert.Equal((nint)1, resource.ResourceId);
    }

    /// <summary>Rejected detachment preserves a borrowed identifier until disposal clears it.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ResourceIdTracksDisposalAndDetachment(bool detach)
    {
        // Arrange: Use a borrowed resource whose native handle cannot be transferred.
        using GpuResource resource = new ObservedResource();

        // Assert: The borrowed resource starts valid with its original identifier.
        Assert.Equal((nint)1, resource.ResourceId);
        Assert.True(resource.IsValid);

        // Act / Assert: A rejected transfer leaves the borrowed resource unchanged.
        if (detach)
        {
            Assert.Throws<NotSupportedException>(() => resource.Detach());
            Assert.Equal((nint)1, resource.ResourceId);
            Assert.True(resource.IsValid);
        }

        // Act: Dispose the borrowed resource.
        resource.Dispose();

        // Assert: Disposal clears the identifier and later transfer remains rejected.
        Assert.Equal((nint)0, resource.ResourceId);
        Assert.False(resource.IsValid);
        Assert.True(resource.IsDisposed);
        Assert.Throws<NotSupportedException>(() => resource.Detach());
    }

    /// <summary>An unallocated owner completes either transfer API with an empty handle and becomes disposed.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EmptyOwningDeclarationTransfersEmptyHandle(bool releaseHandle)
    {
        // Arrange: Create an owning resource declaration that has never received a native name.
        using var resource = new EmptyOwningDeclaration();

        // Act / Assert: Release or detach the unallocated resource and verify the empty transfer.
        Assert.Equal((nint)0, releaseHandle ? resource.ReleaseHandle() : resource.Detach());
        Assert.True(resource.IsDisposed);
        Assert.False(resource.IsValid);
        Assert.Equal((nint)0, resource.Detach());
    }
    #endregion

    #region Private
    /// <summary>Represents an owning declaration that has not realized a native allocation.</summary>
    private sealed class EmptyOwningDeclaration : GpuResource
    {
        #region Public API
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }

        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected/Internal
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
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
