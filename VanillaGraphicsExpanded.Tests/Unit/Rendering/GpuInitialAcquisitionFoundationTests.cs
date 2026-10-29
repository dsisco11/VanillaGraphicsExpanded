using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks recorded initial discovery, completion exclusion and original-entry recovery.</summary>
public sealed class GpuInitialAcquisitionFoundationTests
{
    #region Public API
    /// <summary>A missing covered identity grants neither discovery nor recovery permission.</summary>
    [Fact]
    public void MissingIdentityDoesNotAuthorizeDiscoveryOrRecovery()
    {
        using var manager = new RHIResourceManager(static () => (1, 1), static _ => true);
        var reader = new RecursiveReader(manager, false, 0);
        Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Null(manager.RecoverResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Null(manager.FindResource(GpuResourceKind.Buffer, 7));
        Assert.Equal(0, reader.Reads);
    }

    /// <summary>Discovery consumes recorded provenance once; failed discovery preserves the same unavailable original for explicit recovery.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void RecordedInitialReadRetainsOriginalIdentity(bool recover, int outcome)
    {
        using var manager = new RHIResourceManager(static () => (1, 1), static _ => true);
        var original = manager.RecordPreexistingBuffer(7);
        manager.ActivateResourceObservation();
        var reader = new RecursiveReader(manager, recover, outcome);
        if (outcome == 2)
            Assert.Same(reader.Failure, Assert.Throws<InvalidOperationException>(() => manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)));
        else
        {
            var result = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader);
            if (outcome == 0)
            {
                Assert.Same(original, result);
                Assert.True(result.MetadataAvailable);
                Assert.Equal(new GpuBufferMetadata(16, BufferUsage.StaticDraw), result.Metadata);
                Assert.Equal((ulong)1, result.MetadataRevision);
            }
            else Assert.Null(result);
        }
        Assert.Equal(1, reader.Reads);
        Assert.Same(original, reader.ObservedEntry);
        Assert.Same(original, manager.FindResource(GpuResourceKind.Buffer, 7));
        Assert.Equal(GpuResourceRegistrationState.Live, original.State);
        Assert.Equal(GpuInitialDiscovery.None, original.InitialDiscovery);
        if (outcome != 0)
        {
            Assert.False(original.MetadataAvailable);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
            Assert.Equal(1, reader.Reads);
            // Recovery names the original unavailable object; ordinary acquisition cannot retry it.
            reader.Outcome = 0;
            Assert.Same(original, manager.RecoverResource(GpuResourceKind.Buffer, 7, reader));
            Assert.Equal(2, reader.Reads);
        }
        Assert.True(original.MetadataAvailable);
        Assert.Equal(new GpuBufferMetadata(16, BufferUsage.StaticDraw), original.Metadata);
        Assert.Equal((ulong)1, original.MetadataRevision);
        Assert.Same(original, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
        Assert.Equal(outcome == 0 ? 1 : 2, reader.Reads);
    }
    #endregion

    #region Private
    /// <summary>Attempts same-ID discovery from its first native-read callback and supplies a controlled outcome.</summary>
    private sealed class RecursiveReader(RHIResourceManager manager, bool recover, int outcome) : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        internal GpuResourceRegistration? ObservedEntry;
        internal int Outcome = outcome;
        internal readonly InvalidOperationException Failure = new("Injected initial read failure.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata)
        {
            Reads++;
            ObservedEntry = manager.FindResource(GpuResourceKind.Buffer, id);
            Assert.NotNull(ObservedEntry);
            Assert.False(ObservedEntry.MetadataAvailable);
            Assert.Equal(GpuInitialDiscovery.None, ObservedEntry.InitialDiscovery);
            Assert.Null(recover ? manager.RecoverResource(GpuResourceKind.Buffer, id, this)
                : manager.AcquireResource(GpuResourceKind.Buffer, id, this));
            metadata = new(16, BufferUsage.StaticDraw);
            if (Outcome == 2) throw Failure;
            return Outcome == 0;
        }
    }
    #endregion
}
