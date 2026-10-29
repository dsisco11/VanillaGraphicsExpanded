using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

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
        using var manager = new RHIResourceManager(static () => (123, 1), static _ => true);
        var reader = new ProgramReader(success);
        var original = manager.RecordPreexistingExecutable<GpuProgramMetadata>(GpuResourceKind.Program, 17);
        manager.ActivateResourceObservation();
        Assert.Null(manager.AcquireResource(GpuResourceKind.Program, 18, reader));
        Assert.Equal(0, reader.Reads);
        var acquired = manager.AcquireResource(GpuResourceKind.Program, 17, reader);
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
        internal int Reads;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuProgramMetadata metadata)
        {
            Reads++;
            metadata = new(false, ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
            return success;
        }
    }
    #endregion
}
