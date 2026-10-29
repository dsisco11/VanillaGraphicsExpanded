using VanillaGraphicsExpanded.Rendering.RHI;
using System.Runtime.CompilerServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Exercises bounded weak association retention across repeated registry lifetimes and name replacement.</summary>
public sealed class GpuResourceAssociationStressTests
{
    #region Public API
    /// <summary>Collected and explicitly released aliases cannot accumulate or regain validity after numeric reuse.</summary>
    [Fact]
    public void RepeatedLifetimesPruneAliasesAndRejectRetiredEntries()
    {
        for (int lifetime = 1; lifetime <= 12; lifetime++)
        {
            using var manager = new RHIResourceManager(() => (1, lifetime), static _ => true);
            var reader = new Reader();
            for (int cycle = 0; cycle < 32; cycle++)
            {
                if (manager.FindResource(GpuResourceKind.Buffer, 7) is null) manager.RecordPreexistingBuffer(7);
                var entry = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
                var abandoned = AssociateBatch(manager, entry);
                for (int attempt = 0; attempt < 5 && abandoned.Any(value => value.IsAlive); attempt++)
                { GC.Collect(); GC.WaitForPendingFinalizers(); }
                Assert.All(abandoned, value => Assert.False(value.IsAlive));
                Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Buffer, 7, reader));
                Assert.Equal(0, entry.AssociationCount);
                using var alias = new Alias();
                manager.AssociateResource(entry, alias);
                Assert.Equal(1, entry.AssociationCount);
                manager.UnregisterResource(entry, alias);
                Assert.Equal(0, entry.AssociationCount);
                manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
                manager.ObserveBufferCreation(7, true);
                var replacement = manager.AcquireResource(GpuResourceKind.Buffer, 7, reader)!;
                Assert.NotSame(entry, replacement);
                Assert.Throws<InvalidOperationException>(() => manager.PublishMetadata(entry, default, GpuMetadataChange.Metadata));
                Assert.Same(replacement, manager.FindResource(GpuResourceKind.Buffer, 7));
            }
            var last = manager.FindResource(GpuResourceKind.Buffer, 7)!;
            manager.Dispose();
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, last.State);
            Assert.Throws<ObjectDisposedException>(() => manager.FindResource(GpuResourceKind.Buffer, 7));
        }
    }
    #endregion

    #region Private
    /// <summary>Ends strong local references before collection without relying on JIT local lifetime decisions.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference[] AssociateBatch(RHIResourceManager manager, GpuResourceMetadataEntry<GpuBufferMetadata> entry)
    {
        var values = new WeakReference[16];
        for (int index = 0; index < values.Length; index++)
        {
            var alias = new Alias();
            manager.AssociateResource(entry, alias);
            values[index] = new(alias);
        }
        return values;
    }
    /// <summary>Supplies deterministic buffer facts without native queries.</summary>
    private sealed class Reader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => id == 7;
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata data) { data = new(16, BufferUsage.StaticDraw); return IsResource(id); }
    }
    /// <summary>Models a non-owning recipient so collection never retires native storage.</summary>
    private sealed class Alias : GpuResource<GpuBufferMetadata>
    {
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = 7;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}
