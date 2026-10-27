using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks initial provisional acquisition identity before any wrapper or canonical snapshot exists.</summary>
public sealed class GpuInitialAcquisitionFoundationTests
{
    #region Public API
    /// <summary>Initial recovery exposes no partial snapshot or recursive read, and failed admission leaves no registry entry.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void InitialReadUsesOneProvisionalIdentity(bool recover, int outcome)
    {
        using var manager = new GpuResourceManager(static () => (1, 1), static _ => true);
        var reader = new RecursiveReader(manager, recover, outcome);
        if (outcome == 2)
            Assert.Same(reader.Failure, Assert.Throws<InvalidOperationException>(() => manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)));
        else
        {
            var result = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader);
            if (outcome == 0)
            {
                Assert.NotNull(result);
                Assert.True(result.MetadataAvailable);
                Assert.Equal(new GpuBufferMetadata(16, BufferUsage.StaticDraw), result.Metadata);
                Assert.Equal((ulong)1, result.MetadataRevision);
            }
            else Assert.Null(result);
        }
        Assert.Equal(1, reader.Reads);
        Assert.NotNull(reader.Provisional);
        if (outcome == 0) Assert.Same(reader.Provisional, manager.FindResource(GpuResourceKind.Buffer, 7));
        else
        {
            Assert.Null(manager.FindResource(GpuResourceKind.Buffer, 7));
            Assert.False(reader.Provisional.MetadataAvailable);
            Assert.Equal(GpuResourceRegistrationState.Deleted, reader.Provisional.State);
        }
    }
    #endregion

    #region Private
    /// <summary>Attempts same-ID discovery from its first native-read callback and supplies a controlled outcome.</summary>
    private sealed class RecursiveReader(GpuResourceManager manager, bool recover, int outcome) : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        internal int Reads;
        internal GpuResourceRegistration? Provisional;
        internal readonly InvalidOperationException Failure = new("Injected initial read failure.");
        /// <inheritdoc />
        public bool IsResource(nint id) => true;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata)
        {
            Reads++;
            Provisional = manager.FindResource(GpuResourceKind.Buffer, id);
            Assert.NotNull(Provisional);
            Assert.False(Provisional.MetadataAvailable);
            Assert.Null(recover ? manager.RecoverResource(GpuResourceKind.Buffer, id, this)
                : manager.AcquireResource(GpuResourceKind.Buffer, id, this));
            metadata = new(16, BufferUsage.StaticDraw);
            if (outcome == 2) throw Failure;
            return outcome == 0;
        }
    }
    #endregion
}
