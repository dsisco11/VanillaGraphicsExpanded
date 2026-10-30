using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Exercises manager completion semantics with controlled contexts and no native graphics calls.</summary>
public sealed class ResourceChangePublicationTests
{
    #region Public API
    /// <summary>Canonical collection values ignore allocation and insertion order while preserving unavailable facts.</summary>
    [Fact]
    public void CanonicalCollectionsUseStructuralEqualityAndHashing()
    {
        var first = new GpuTextureLevelMetadata(0, 0, 8, 4, 1, TextureFormat.Rgba8, 0, true);
        var second = first with { Level = 1, Width = 4, Height = 2 };
        var texture = new GpuTextureMetadata(TextureDimension.Two, [first, second], true, 2, 0, 1);
        var reordered = texture with { Levels = [second, first] };
        Assert.Equal(texture, reordered);
        Assert.Equal(texture.GetHashCode(), reordered.GetHashCode());
        Assert.NotEqual(texture with { Levels = default }, texture with { Levels = [] });
        Assert.NotEqual(texture, texture with { BaseLevel = 1 });

        var selection = new GpuFramebufferImageMetadata(GpuResourceKind.Texture, 7, 0, 0, 0, false);
        var attachments = ImmutableDictionary<FramebufferSlot, GpuFramebufferImageMetadata>.Empty
            .Add(FramebufferSlot.Color0, selection).Add(FramebufferSlot.Color1, selection with { Id = 8 });
        var framebuffer = new GpuFramebufferMetadata(attachments, 0, 0, 0, 0, true);
        var rebuilt = framebuffer with { Attachments = attachments.Reverse().ToImmutableDictionary() };
        Assert.Equal(framebuffer, rebuilt);
        Assert.Equal(framebuffer.GetHashCode(), rebuilt.GetHashCode());
        Assert.NotEqual(framebuffer, framebuffer with { DefaultWidth = 1 });
        using var manager = Manager();
        var original = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 7);
        var sameName = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        Assert.NotEqual(selection with { OriginalBacking = original }, selection with { OriginalBacking = sameName });

        var resource = new GpuProgramResourceMetadata(ProgramResourceKind.Uniform, 0, "source");
        var program = new GpuProgramMetadata(true, [resource, resource with { Index = 1 }], [new(2, 3), new(4, 5)]);
        var equivalent = program with { Resources = [resource with { Index = 1 }, resource], TextureUnits = [new(4, 5), new(2, 3)] };
        Assert.Equal(program, equivalent);
        Assert.Equal(program.GetHashCode(), equivalent.GetHashCode());
        Assert.NotEqual(program with { Resources = default }, program with { Resources = [] });
    }

    /// <summary>Equal publications assign late recipients but cause no transition or revision.</summary>
    [Fact]
    public void EqualFactsDoNotNotifyAndLateRecipientsStillReceiveSnapshots()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var first = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first);
        var facts = new GpuBufferMetadata(64, BufferUsage.DynamicDraw);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.None);
        Assert.Equal(ResourceChangeKind.Metadata | ResourceChangeKind.Availability, first.Changes.Single().Kind);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.Metadata);
        Assert.Single(first.Changes);
        Assert.Equal(1ul, entry.MetadataRevision);
        using var late = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, late);
        Assert.Equal(facts, late.PublishedMetadata);
        Assert.Empty(late.Changes);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.Storage);
        Assert.Equal(ResourceChangeKind.Storage, first.Changes.Last().Kind);
        Assert.Equal(2ul, entry.MetadataRevision);
        Assert.Equal(1ul, entry.StorageRevision);
        Assert.Single(late.Changes);
    }

    /// <summary>Temporary exclusion does not create availability pairs; actual withdrawal and recovery do.</summary>
    [Fact]
    public void ExclusionWithdrawalAndRecoveryHaveDistinctRevisionSemantics()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, 7);
        using var recipient = new Recipient<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, 7);
        manager.AssociateResource(entry, recipient);
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        entry.BeginRefresh(GpuMetadataChange.Metadata);
        manager.PublishMetadata(entry, default, GpuMetadataChange.Metadata);
        Assert.Single(recipient.Changes);
        Assert.Equal(1ul, entry.MetadataRevision);
        entry.Withdraw();
        entry.Withdraw();
        Assert.Equal(ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
        Assert.Equal(2ul, entry.MetadataRevision);
        manager.PublishMetadata(entry, default, GpuMetadataChange.Metadata);
        Assert.Equal(ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
        Assert.Equal(3ul, entry.MetadataRevision);
    }

    /// <summary>Explicit executable replacement remains observable when its interface is identical.</summary>
    [Fact]
    public void EqualExecutableReplacementRetainsItsEffect()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuProgramMetadata>(GpuResourceKind.Program, 7);
        using var recipient = new Recipient<GpuProgramMetadata>(GpuResourceKind.Program, 7);
        manager.AssociateResource(entry, recipient);
        var facts = new GpuProgramMetadata(true, [], []);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.None);
        entry.Withdraw(GpuMetadataChange.Executable);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.Metadata);
        Assert.Equal(ResourceChangeKind.Executable | ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
        Assert.Equal(3ul, entry.MetadataRevision);
    }

    /// <summary>Every snapshot and nested completion is committed before the first callback executes.</summary>
    [Fact]
    public void BatchCommitsAllFactsBeforeCallbacksAndAttemptsRemainingRecipients()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var first = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var second = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first); manager.AssociateResource(entry, second);
        var facts = new GpuBufferMetadata(96, BufferUsage.StaticDraw);
        first.Callback = change => { Assert.True(entry.IsUsable); Assert.Equal(facts, second.PublishedMetadata); throw new InvalidOperationException("consumer"); };
        Assert.Throws<AggregateException>(() => manager.PublishMetadata(entry, facts, GpuMetadataChange.Storage));
        Assert.Single(first.Changes); Assert.Single(second.Changes);
        Assert.True(entry.IsUsable);
        Assert.Equal(facts, entry.Metadata);
        Assert.Equal(1ul, entry.StorageRevision);
    }

    /// <summary>A deferred obsolete completion cannot overwrite a newer transition or a reused name.</summary>
    [Fact]
    public void ReentrySupersedesQueuedDeliveryAndRetirementIsTerminalOnce()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        using var recipient = new Recipient<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.AssociateResource(entry, recipient);
        using (var batch = manager.BeginChangeDelivery())
        {
            manager.PublishMetadata(entry, default, GpuMetadataChange.None);
            entry.Withdraw();
            batch.Complete();
        }
        Assert.Single(recipient.Changes);
        Assert.False(recipient.Changes.Single().Available);
        manager.ObserveDeletion(GpuResourceKind.Query, 7);
        Assert.Equal(ResourceChangeKind.Retired, recipient.Changes.Last().Kind);
        Assert.Equal(2, recipient.Changes.Count);
        manager.ObserveDeletion(GpuResourceKind.Query, 7);
        var replacement = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.PublishMetadata(replacement, default, GpuMetadataChange.None);
        Assert.NotSame(entry, replacement);
        Assert.Equal(2, recipient.Changes.Count);
    }
    /// <summary>Recovery restores source-only withdrawals but cannot certify an independently uncertain association.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BackingRecoveryPreservesIndependentChildUncertainty(bool independentChange)
    {
        using var manager = Manager();
        var buffer = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(buffer, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        var texture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        var facts = new GpuTextureMetadata(TextureDimension.Buffer, [], false, 0, 0, 0,
            new(7, TextureFormat.R32f, 0, 64, false));
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage);
        ulong storage = texture.StorageRevision;
        buffer.Withdraw(GpuMetadataChange.Storage);
        Assert.False(texture.IsUsable);
        if (independentChange) texture.Withdraw(GpuMetadataChange.Storage);
        manager.PublishMetadata(buffer, buffer.Metadata, GpuMetadataChange.Metadata);
        Assert.Equal(!independentChange, texture.IsUsable);
        Assert.Equal(storage + (independentChange ? 0ul : 1ul), texture.StorageRevision);
    }

    /// <summary>Reused numeric names change backing metadata identity even if the represented association values match.</summary>
    [Fact]
    public void RenewedOriginalBackingReportsMetadataAndStorage()
    {
        using var manager = Manager();
        var buffer = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(buffer, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        var texture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        var facts = new GpuTextureMetadata(TextureDimension.Buffer, [], false, 0, 0, 0,
            new(7, TextureFormat.R32f, 0, 64, false));
        using var recipient = new Recipient<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        manager.AssociateResource(texture, recipient);
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage);
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage, renewTextureAssociation: true);
        Assert.Equal(ResourceChangeKind.Storage, recipient.Changes.Last().Kind);
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        var replacement = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(replacement, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage, renewTextureAssociation: true);
        Assert.Equal(ResourceChangeKind.Metadata | ResourceChangeKind.Storage | ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
    }

    /// <summary>Detached associations stop receiving availability changes from their former backing.</summary>
    [Fact]
    public void DetachedBufferTextureDoesNotFollowItsFormerBacking()
    {
        using var manager = Manager();
        var buffer = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(buffer, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        var texture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        var facts = new GpuTextureMetadata(TextureDimension.Buffer, [], false, 0, 0, 0,
            new(7, TextureFormat.R32f, 0, 64, false));
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage);
        manager.PublishMetadata(texture, facts with { Buffer = facts.Buffer!.Value with { BufferId = 0 } },
            GpuMetadataChange.Storage, renewTextureAssociation: true);
        ulong revision = texture.MetadataRevision;
        buffer.Withdraw();
        Assert.True(texture.IsUsable);
        Assert.Equal(revision, texture.MetadataRevision);
    }

    /// <summary>A callback mutation supersedes the outer completion without delivering stale facts to remaining aliases.</summary>
    [Fact]
    public void CallbackReentryStopsObsoleteDelivery()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var first = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var second = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first); manager.AssociateResource(entry, second);
        bool mutated = false;
        Action<RHIResourceChange> callback = change =>
        {
            Assert.Equal(entry.MetadataRevision, change.MetadataRevision);
            Assert.Equal(entry.Metadata, first.PublishedMetadata); Assert.Equal(entry.Metadata, second.PublishedMetadata);
            if (!mutated)
            {
                mutated = true;
                manager.PublishMetadata(entry, new(128, BufferUsage.DynamicDraw), GpuMetadataChange.Storage);
            }
        };
        first.Callback = second.Callback = callback;
        manager.PublishMetadata(entry, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        Assert.Equal(2ul, first.Changes.Last().MetadataRevision);
        Assert.Equal(2ul, second.Changes.Last().MetadataRevision);
    }

    /// <summary>Context teardown completes a queued original retirement on the originating thread exactly once.</summary>
    [Fact]
    public void ContextRetirementCannotLoseQueuedTerminalDelivery()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        using var recipient = new Recipient<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.AssociateResource(entry, recipient);
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        int origin = Environment.CurrentManagedThreadId;
        recipient.Callback = change => Assert.Equal(origin, Environment.CurrentManagedThreadId);
        var worker = new Thread(() => manager.RetireResource(entry));
        worker.Start(); worker.Join();
        Assert.Single(recipient.Changes);
        manager.Dispose();
        Assert.Equal(2, recipient.Changes.Count);
        entry.CaptureRetirement()();
        Assert.Equal(2, recipient.Changes.Count);
        Assert.True((recipient.Changes.Last().Kind & ResourceChangeKind.Retired) != 0);
    }

    /// <summary>Worker retirement cannot own or steal a context-thread notification batch.</summary>
    [Fact]
    public void CompletionBatchRejectsForeignThreadsWhileRetirementStillQueues()
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        using var recipient = new Recipient<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.AssociateResource(entry, recipient);
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        Exception? foreignFailure = null, retirementFailure = null;
        var worker = new Thread(() =>
        {
            try { using var invalid = manager.BeginChangeDelivery(); } catch (Exception error) { foreignFailure = error; }
            try { Assert.True(manager.TryScheduleResourceRetirement(recipient, 7, (1, 1))); } catch (Exception error) { retirementFailure = error; }
        });
        worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(foreignFailure);
        Assert.Null(retirementFailure);
        Assert.Single(recipient.Changes);
        int origin = Environment.CurrentManagedThreadId;
        recipient.Callback = _ => Assert.Equal(origin, Environment.CurrentManagedThreadId);
        manager.Dispose();
        Assert.Equal(2, recipient.Changes.Count);
    }

    /// <summary>Every canonical family shares initial publication and terminal lifetime semantics.</summary>
    [Fact]
    public void EveryFamilyUsesTheSameLifecycleContract()
    {
        CheckFamily<GpuBufferMetadata>(GpuResourceKind.Buffer);
        CheckFamily<GpuTextureMetadata>(GpuResourceKind.Texture);
        CheckFamily<GpuRenderbufferMetadata>(GpuResourceKind.Renderbuffer);
        CheckFamily<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
        CheckFamily<GpuShaderMetadata>(GpuResourceKind.Shader);
        CheckFamily<GpuProgramMetadata>(GpuResourceKind.Program);
        CheckFamily<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray);
        CheckFamily<GpuTransformFeedbackMetadata>(GpuResourceKind.TransformFeedback);
        CheckFamily<GpuSamplerMetadata>(GpuResourceKind.Sampler);
        CheckFamily<GpuProgramPipelineMetadata>(GpuResourceKind.ProgramPipeline);
        CheckFamily<GpuQueryMetadata>(GpuResourceKind.Query);
        CheckFamily<GpuSyncMetadata>(GpuResourceKind.Sync);
        CheckFamily<GpuSemaphoreMetadata>(GpuResourceKind.Semaphore);
        CheckFamily<GpuMemoryObjectMetadata>(GpuResourceKind.MemoryObject);
    }
    #endregion

    #region Private
    /// <summary>Checks reserved, available, equal and retired outcomes without discovering native descriptors.</summary>
    private static void CheckFamily<T>(GpuResourceKind kind) where T : struct
    {
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<T>(kind, 7);
        using var recipient = new Recipient<T>(kind, 7);
        manager.AssociateResource(entry, recipient);
        Assert.False(entry.IsUsable);
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        manager.PublishMetadata(entry, default, GpuMetadataChange.Metadata);
        Assert.Single(recipient.Changes);
        Assert.True(entry.IsUsable);
        manager.ObserveDeletion(kind, 7);
        Assert.Equal(2, recipient.Changes.Count);
        Assert.Equal(ResourceChangeKind.Retired | ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
        Assert.Equal(2ul, entry.MetadataRevision);
        Assert.False(entry.IsUsable);
    }

    /// <summary>Supplies a deterministic originating context without loading a driver.</summary>
    private static RHIResourceManager Manager() => new(static () => (1, 1), static _ => true);

    /// <summary>Records internal changes without owning or deleting the synthetic native name.</summary>
    private sealed class Recipient<T>(GpuResourceKind kind, nint id) : GpuResource<T> where T : struct
    {
        internal readonly List<RHIResourceChange> Changes = new();
        internal Action<RHIResourceChange>? Callback;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        /// <inheritdoc />
        internal override void OnResourceChanged(in RHIResourceChange change)
        {
            Changes.Add(change);
            Callback?.Invoke(change);
        }
    }
    #endregion
}
