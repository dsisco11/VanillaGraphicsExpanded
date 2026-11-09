using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Qualifies executable initial-discovery provenance independently of native allocation numbering.</summary>
public sealed class GpuExecutableDiscoveryTests
{
    #region Public API
    /// <summary>Only recorded pre-install identities receive one initial attempt, and failure cannot be retried by association.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgramDiscoveryRequiresRecordedCause(bool success)
    {
        // Arrange the original identity and a reader with a controlled metadata outcome.
        using var manager = new RHIResourceManager(static () => (123, 1), static _ => true);
        var reader = new ProgramReader(success);
        var original = manager.RecordPreexistingExecutable<GpuProgramMetadata>(GpuResourceKind.Program, 17);

        // Act and assert that an unrecorded native ID cannot trigger metadata discovery.
        manager.ActivateResourceObservation();
        Assert.Null(manager.AcquireResource(GpuResourceKind.Program, 18, reader));
        Assert.Equal(0, reader.Reads);

        // Act on the original recorded ID, then assert it receives only one initial read.
        var acquired = manager.AcquireResource(GpuResourceKind.Program, 17, reader);

        // Assert the recorded identity gets one read and later associations do not retry it.
        Assert.Equal(success, acquired is not null);
        Assert.Equal(1, reader.Reads);
        Assert.Same(original, manager.FindResource(GpuResourceKind.Program, 17));
        Assert.Equal(GpuInitialDiscovery.None, original.InitialDiscovery);
        Assert.Equal(success, manager.AcquireResource(GpuResourceKind.Program, 17, reader) is not null);
        Assert.Equal(1, reader.Reads);
        Assert.Throws<InvalidOperationException>(() => manager.RecordPreexistingExecutable<GpuProgramMetadata>(GpuResourceKind.Program, 19));
    }
    #endregion

    #region Private
    /// <summary>Provides an explicit initial executable output without adding an external validity probe.</summary>
    private sealed class ProgramReader(bool success) : IGpuResourceMetadataReader<GpuProgramMetadata>
    {
        #region Public API
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuProgramMetadata metadata)
        {
            Reads++;
            metadata = new(false, ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
            return success;
        }
        #endregion

        #region Internal
        internal int Reads;
        #endregion
    }
    #endregion
}
