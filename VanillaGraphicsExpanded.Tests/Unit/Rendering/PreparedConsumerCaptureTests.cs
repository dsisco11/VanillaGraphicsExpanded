using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies independent CPU captures preserve every change subscription to a shared stable resource.</summary>
public sealed class PreparedConsumerCaptureTests
{
    #region Public API
    /// <summary>Concurrent declarations cannot lose subscriptions during the resource's initial weak-consumer setup.</summary>
    [Fact]
    public void ConcurrentCapturesReceiveTheNextCommittedChange()
    {
        var resource = new Declaration();
        var owners = Enumerable.Range(0, 64).Select(_ => new object()).ToArray();
        var changes = new int[owners.Length];
        Parallel.For(0, owners.Length, index => resource.ObservePreparation(owners[index], () => changes[index]++));
        resource.NotifyResourceChanged(new(ResourceChangeKind.Metadata, true, 1, 0));
        Assert.All(changes, value => Assert.Equal(1, value));
        GC.KeepAlive(owners);
    }
    #endregion

    #region Private
    /// <summary>Provides a CPU-only declaration without native allocation, metadata acquisition or disposal effects.</summary>
    private sealed class Declaration : GpuResource
    {
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }
    #endregion
}
