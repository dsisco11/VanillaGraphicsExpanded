using System.Collections.Immutable;
using System.Reflection;
using Graffs.Implementations;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks original dependency lifetimes and deletion outcomes without native driver execution.</summary>
[Trait("Category", "Unit")]
public sealed class ResourceDependencyLookupTests
{
    #region Public API
    /// <summary>Repeated attachment slots retain one edge until the final slot is detached.</summary>
    [Fact]
    public void SharedAttachmentSourceSurvivesOneSlotDetachAndRetirementUntilNativeDeletion()
    {
        // Arrange: Attach two framebuffer slots to one canonical texture source.
        using var manager = Manager();
        var source = Texture(manager, 1);
        var framebuffer = manager.RegisterCreatedResource<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, 2);
        var image = new GpuFramebufferImageMetadata(GpuResourceKind.Texture, 1, 0, 0, 0, false) { OriginalBacking = source };
        var facts = new GpuFramebufferMetadata(ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Empty
            .Add(FramebufferSlot.Color0, image).Add(FramebufferSlot.Color1, image), 0, 0, 0, 0, true);
        manager.PublishMetadata(framebuffer, facts, GpuMetadataChange.None);
        Assert.Single(Graph(manager).GetDependents(source));
        // Act: Remove one slot, retire the source, then report native deletion.
        manager.PublishMetadata(framebuffer, facts with { Attachments = facts.Attachments.Remove(FramebufferSlot.Color0) }, GpuMetadataChange.Metadata);
        Assert.Single(Graph(manager).GetDependents(source));
        Assert.True(manager.RetireResource(source));
        Assert.Single(Graph(manager).GetDependents(source));
        manager.ObserveDeletion(GpuResourceKind.Texture, 1, drawFramebuffer: 2);
        // Assert: Final deletion clears the matching attachment and releases graph retention.
        Assert.Empty(framebuffer.Metadata.Attachments);
        Assert.Equal(0, Graph(manager).Count);
    }

    /// <summary>Retargeting removes the old edge, and source recovery cannot erase independent child uncertainty.</summary>
    [Fact]
    public void RetargetAndIndependentFailureRespectCurrentSource()
    {
        // Arrange: Publish a texture backed by one of two independent buffers.
        using var manager = Manager();
        var first = Buffer(manager, 1);
        var second = Buffer(manager, 2);
        var texture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 3);
        var facts = new GpuTextureMetadata(TextureDimension.Buffer, [], false, 0, 0, 0, new(1, TextureFormat.R32f, 0, 64, false));
        manager.PublishMetadata(texture, facts, GpuMetadataChange.None);
        // Act: Retarget the texture to the second buffer.
        manager.PublishMetadata(texture, facts with { Buffer = facts.Buffer!.Value with { BufferId = 2 } },
            GpuMetadataChange.Metadata, renewTextureAssociation: true);
        // Assert: Retargeting removes the old dependency edge.
        Assert.Empty(Graph(manager).GetDependents(first));
        // Act: Withdraw the former source.
        first.Withdraw();
        // Assert: The texture remains usable through its current backing.
        Assert.True(texture.IsUsable);
        // Act: Withdraw the active source.
        second.Withdraw();
        Assert.False(texture.IsUsable);
        // Act: Independently withdraw the texture before recovering its source.
        texture.Withdraw();
        // Act: Recover the source, then recover the texture explicitly.
        manager.PublishMetadata(second, second.Metadata, GpuMetadataChange.None);
        Assert.False(texture.IsUsable);
        manager.PublishMetadata(texture, texture.Metadata, GpuMetadataChange.None);
        // Act: Delete the former source, then confirm the texture still follows its active source.
        manager.ObserveDeletion(GpuResourceKind.Buffer, 1);
        Assert.True(texture.IsUsable);
        // Act: Detach the texture's current buffer association.
        manager.PublishMetadata(texture, texture.Metadata with { Buffer = null }, GpuMetadataChange.Metadata);
        // Assert: Detaching the backing clears graph retention.
        Assert.Equal(0, Graph(manager).Count);
    }

    /// <summary>Nested views recover from source withdrawal but never from replacement of their fixed storage.</summary>
    [Fact]
    public void NestedViewsRetainFixedOriginalStorageAndClearOnTeardown()
    {
        // Arrange: Link two nested native texture views to one original texture.
        using var manager = Manager();
        var source = Texture(manager, 1);
        var first = Texture(manager, 2);
        var nested = Texture(manager, 3);
        manager.RecordNativeTextureView(first, source);
        manager.RecordNativeTextureView(nested, first);
        // Act: Withdraw the original source.
        source.Withdraw();
        // Assert: Both nested views become unusable.
        Assert.False(first.IsUsable);
        Assert.False(nested.IsUsable);
        // Act: Restore the original source facts.
        manager.PublishMetadata(source, source.Metadata, GpuMetadataChange.None);
        // Assert: Both views recover while their original storage remains unchanged.
        Assert.True(first.IsUsable);
        Assert.True(nested.IsUsable);
        // Act: Replace the original source storage.
        manager.PublishMetadata(source, source.Metadata, GpuMetadataChange.Storage);
        // Assert: Both views are permanently invalidated by the storage replacement.
        Assert.False(first.IsUsable);
        Assert.False(nested.IsUsable);
        // Act: Republish source metadata after the replacement.
        manager.PublishMetadata(source, source.Metadata, GpuMetadataChange.None);
        // Assert: The original views cannot follow replacement storage.
        Assert.False(first.IsUsable);
        Assert.False(nested.IsUsable);
        // Act: Tear down the manager and release its dependency edges.
        manager.Dispose();
        // Assert: No graph dependencies remain.
        Assert.Equal(0, Graph(manager).Count);
    }

    /// <summary>An unresolved attachment stays conservative; a deleted original never follows numeric name reuse.</summary>
    [Fact]
    public void UnresolvedAndOriginalAttachmentsKeepDistinctDeletionRules()
    {
        // Arrange: Publish one unresolved attachment and one attachment with an original backing entry.
        using var manager = Manager();
        var unknown = manager.RegisterCreatedResource<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, 2);
        var image = new GpuFramebufferImageMetadata(GpuResourceKind.Texture, 1, 0, 0, 0, false);
        var facts = new GpuFramebufferMetadata(ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Empty
            .Add(FramebufferSlot.Color0, image), 0, 0, 0, 0, true);
        manager.PublishMetadata(unknown, facts, GpuMetadataChange.None);
        var source = Texture(manager, 1);
        Assert.Empty(Graph(manager).GetDependents(source));
        var known = manager.RegisterCreatedResource<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer, 3);
        manager.PublishMetadata(known, facts, GpuMetadataChange.None);
        // Act: Delete the texture name without framebuffer ownership context.
        manager.ObserveDeletion(GpuResourceKind.Texture, 1);
        // Assert: Unresolved facts become unusable while the known attachment retains its original backing identity.
        Assert.False(unknown.IsUsable);
        Assert.Same(source, known.Metadata.Attachments[FramebufferSlot.Color0].OriginalBacking);
        var replacement = Texture(manager, 1);
        Assert.Empty(Graph(manager).GetDependents(replacement));
        // Act: Attribute deletion to the known framebuffer and inspect the still-published attachment.
        manager.ObserveDeletion(GpuResourceKind.Texture, 1, drawFramebuffer: 3);
        // Assert: The known attachment remains present with its original backing identity.
        Assert.Single(known.Metadata.Attachments);
        // Act: Clear the unresolved framebuffer's attachment, then report texture deletion again.
        manager.PublishMetadata(unknown, facts with { Attachments = facts.Attachments.Clear() }, GpuMetadataChange.Metadata);
        manager.ObserveDeletion(GpuResourceKind.Texture, 1);
        // Assert: Clearing unresolved facts lets that framebuffer remain usable.
        Assert.True(unknown.IsUsable);
    }

    /// <summary>Retiring a dependent releases its incoming edges without retiring its backing.</summary>
    [Fact]
    public void DependentRetirementRemovesLookupRetention()
    {
        // Arrange: Record one view-to-source dependency.
        using var manager = Manager();
        var source = Texture(manager, 1);
        var view = Texture(manager, 2);
        manager.RecordNativeTextureView(view, source);
        // Act / Assert: Retiring the dependent removes its incoming edge but keeps the source usable.
        Assert.True(manager.RetireResource(view));
        Assert.True(source.IsUsable);
        Assert.Equal(0, Graph(manager).Count);
    }
    #endregion

    #region Private
    /// <summary>Supplies a deterministic live context without a graphics driver.</summary>
    private static RHIResourceManager Manager() => new(static () => (1, 1), static _ => true);

    /// <summary>Creates canonical image facts without native allocation.</summary>
    private static GpuResourceMetadataEntry<GpuTextureMetadata> Texture(RHIResourceManager manager, int id)
    {
        var entry = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, id);
        manager.PublishMetadata(entry, new(TextureDimension.Two, [], true, 1, 0, 0), GpuMetadataChange.None);
        return entry;
    }

    /// <summary>Creates canonical buffer facts without native allocation.</summary>
    private static GpuResourceMetadataEntry<GpuBufferMetadata> Buffer(RHIResourceManager manager, int id)
    {
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, id);
        manager.PublishMetadata(entry, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        return entry;
    }

    /// <summary>Inspects private derived lookup retention without adding production diagnostics.</summary>
    private static MutableDirectedGraph<GpuResourceRegistration> Graph(RHIResourceManager manager) =>
        (MutableDirectedGraph<GpuResourceRegistration>)typeof(RHIResourceManager)
            .GetField("resourceDependencies", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(manager)!;
    #endregion
}
