using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies collection admission and best-effort disposal with synthetic resources.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuResourceCollectionTests
{
    #region Public API
    /// <summary>Invalid registration and post-disposal registration preserve caller ownership.</summary>
    [Fact]
    public void InvalidRegistrationLeavesCallerOwnershipIntact()
    {
        // Arrange: exercise the collection with a managed sentinel rather than a native resource.
        using var resources = new GpuResourceCollection();

        // Act and assert: rejected inputs never become owned by the collection.
        Assert.Throws<ArgumentNullException>(() => resources.Own<DisposalObservation>(null!));
        resources.Dispose();
        using var candidate = new DisposalObservation(() => { });
        Assert.Throws<ObjectDisposedException>(() => resources.Own(candidate));

        // Assert: disposing the rejected candidate remains the caller's responsibility.
        Assert.False(candidate.IsDisposed);
    }

    /// <summary>One failing release does not prevent remaining collection entries from being retired.</summary>
    [Fact]
    public void DisposalFailureStillRetiresRemainingResources()
    {
        // Arrange: the first synthetic resource fails during cleanup; the next is a survivor witness.
        using var resources = new GpuResourceCollection();
        var failed = resources.Own(new DisposalObservation(() => throw new InvalidOperationException("Release failure.")));
        var remaining = resources.Own(new DisposalObservation(() => { }));

        // Act and assert: the exception is reported only after cleanup continues through all entries.
        var error = Assert.Throws<AggregateException>(() => resources.Dispose());

        // Assert: every owner is retired once and repeated disposal is harmless.
        Assert.IsType<InvalidOperationException>(Assert.Single(error.InnerExceptions));
        Assert.True(failed.IsDisposed);
        Assert.True(remaining.IsDisposed);
        Assert.Equal(1, remaining.DisposalCount);
        resources.Dispose();
    }
    #endregion

    #region Private
    /// <summary>Tracks release behavior without creating a native handle.</summary>
    private sealed class DisposalObservation(Action onDispose) : GpuResource
    {
        #region Public API
        /// <summary>Gets the number of attempted releases.</summary>
        public int DisposalCount { get; private set; }

        /// <inheritdoc />
        public override nint ResourceId { get; protected set; }

        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Texture;

        /// <summary>Invokes the controlled release action and records the attempt.</summary>
        protected override void OnBeforeDelete(nint id)
        {
            DisposalCount++;
            onDispose();
        }
        #endregion
    }
    #endregion
}
