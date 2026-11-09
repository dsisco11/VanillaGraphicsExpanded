using System.Collections.Immutable;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Exercises manager completion semantics with controlled contexts and no native graphics calls.</summary>
[Collection("CPU state")] // Recipient construction captures the process-wide lifecycle service.
public sealed class ResourceChangePublicationTests
{
    #region Public API
    #region Snapshot publication and callbacks
    /// <summary>Equal publications assign late recipients but cause no transition or revision.</summary>
    [Fact]
    public void EqualFactsDoNotNotifyAndLateRecipientsStillReceiveSnapshots()
    {
        // Arrange: Associate one recipient with a reserved buffer and prepare its first facts.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var first = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first);
        var facts = new GpuBufferMetadata(64, BufferUsage.DynamicDraw);
        // Act / Assert: Initial publication notifies the existing recipient once.
        manager.PublishMetadata(entry, facts, GpuMetadataChange.None);
        Assert.Equal(ResourceChangeKind.Metadata | ResourceChangeKind.Availability, first.Changes.Single().Kind);
        // Act / Assert: Equal facts do not notify or advance revision.
        manager.PublishMetadata(entry, facts, GpuMetadataChange.Metadata);
        Assert.Single(first.Changes);
        Assert.Equal(1ul, entry.MetadataRevision);
        using var late = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        // Act / Assert: A late recipient receives the current snapshot before a storage replacement.
        manager.AssociateResource(entry, late);
        Assert.Equal(facts, late.PublishedMetadata);
        Assert.Empty(late.Changes);
        // Act / Assert: Storage replacement notifies both recipients and advances only storage revision.
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
        // Arrange: Associate one recipient with an initially unavailable vertex array.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, 7);
        using var recipient = new Recipient<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray, 7);
        manager.AssociateResource(entry, recipient);
        // Act / Assert: Initial publication and temporary refresh exclusion produce no availability pair.
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        entry.BeginRefresh(GpuMetadataChange.Metadata);
        manager.PublishMetadata(entry, default, GpuMetadataChange.Metadata);
        Assert.Single(recipient.Changes);
        Assert.Equal(1ul, entry.MetadataRevision);
        // Act / Assert: Withdrawal and recovery each produce one availability transition.
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
        // Arrange: Publish one program snapshot and retain its recipient.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuProgramMetadata>(GpuResourceKind.Program, 7);
        using var recipient = new Recipient<GpuProgramMetadata>(GpuResourceKind.Program, 7);
        manager.AssociateResource(entry, recipient);
        var facts = new GpuProgramMetadata(true, [], []);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.None);
        // Act / Assert: Explicit executable replacement remains visible despite equal metadata.
        entry.Withdraw(GpuMetadataChange.Executable);
        manager.PublishMetadata(entry, facts, GpuMetadataChange.Metadata);
        Assert.Equal(ResourceChangeKind.Executable | ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
        Assert.Equal(3ul, entry.MetadataRevision);
    }

    /// <summary>Every snapshot and nested completion is committed before the first callback executes.</summary>
    [Fact]
    public void BatchCommitsAllFactsBeforeCallbacksAndAttemptsRemainingRecipients()
    {
        // Arrange: Attach two recipients and make the first callback inspect committed state before throwing.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var first = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        using var second = new Recipient<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.AssociateResource(entry, first); manager.AssociateResource(entry, second);
        var facts = new GpuBufferMetadata(96, BufferUsage.StaticDraw);
        first.Callback = change => { Assert.True(entry.IsUsable); Assert.Equal(facts, second.PublishedMetadata); throw new InvalidOperationException("consumer"); };
        // Act / Assert: Publication commits both snapshots and aggregates callback failure after attempting both.
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
        // Arrange: Queue an initial publication and withdrawal in one explicit delivery batch.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        using var recipient = new Recipient<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.AssociateResource(entry, recipient);
        // Act: Complete the deferred batch, retire the old name, and publish a replacement.
        using (var batch = manager.BeginChangeDelivery())
        {
            manager.PublishMetadata(entry, default, GpuMetadataChange.None);
            entry.Withdraw();
            batch.Complete();
        }
        // Assert: The deferred publication is superseded by withdrawal.
        Assert.Single(recipient.Changes);
        Assert.False(recipient.Changes.Single().Available);
        // Act: Delete the retired name once.
        manager.ObserveDeletion(GpuResourceKind.Query, 7);
        // Assert: The first deletion retires the recipient exactly once.
        Assert.Equal(ResourceChangeKind.Retired, recipient.Changes.Last().Kind);
        Assert.Equal(2, recipient.Changes.Count);
        // Act: Repeat deletion and publish a replacement under the same numeric name.
        manager.ObserveDeletion(GpuResourceKind.Query, 7);
        var replacement = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.PublishMetadata(replacement, default, GpuMetadataChange.None);
        // Assert: Reuse creates distinct registration identity without notifying the old recipient.
        Assert.NotSame(entry, replacement);
        Assert.Equal(2, recipient.Changes.Count);
    }
    /// <summary>Recovery restores source-only withdrawals but cannot certify an independently uncertain association.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BackingRecoveryPreservesIndependentChildUncertainty(bool independentChange)
    {
        // Arrange: Link a texture to a buffer and remember its storage revision.
        using var manager = Manager();
        var buffer = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(buffer, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        var texture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        var facts = new GpuTextureMetadata(TextureDimension.Buffer, [], false, 0, 0, 0,
            new(7, TextureFormat.R32f, 0, 64, false));
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage);
        ulong storage = texture.StorageRevision;
        // Act / Assert: Source withdrawal invalidates the texture; optional child withdrawal remains independent.
        buffer.Withdraw(GpuMetadataChange.Storage);
        Assert.False(texture.IsUsable);
        if (independentChange) texture.Withdraw(GpuMetadataChange.Storage);
        // Act / Assert: Recovering only the source restores the child unless it was independently changed.
        manager.PublishMetadata(buffer, buffer.Metadata, GpuMetadataChange.Metadata);
        Assert.Equal(!independentChange, texture.IsUsable);
        Assert.Equal(storage + (independentChange ? 0ul : 1ul), texture.StorageRevision);
    }

    /// <summary>Reused numeric names change backing metadata identity even if the represented association values match.</summary>
    [Fact]
    public void RenewedOriginalBackingReportsMetadataAndStorage()
    {
        // Arrange: Link a texture to one buffer and observe its initial association publication.
        using var manager = Manager();
        var buffer = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(buffer, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        var texture = manager.RegisterCreatedResource<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        var facts = new GpuTextureMetadata(TextureDimension.Buffer, [], false, 0, 0, 0,
            new(7, TextureFormat.R32f, 0, 64, false));
        using var recipient = new Recipient<GpuTextureMetadata>(GpuResourceKind.Texture, 8);
        manager.AssociateResource(texture, recipient);
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage);
        // Act: Renew the same backing association and verify it reports storage replacement.
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage, renewTextureAssociation: true);
        Assert.Equal(ResourceChangeKind.Storage, recipient.Changes.Last().Kind);
        // Act: Delete and recreate the backing with the same numeric name, then renew the association again.
        manager.ObserveDeletion(GpuResourceKind.Buffer, 7);
        var replacement = manager.RegisterCreatedResource<GpuBufferMetadata>(GpuResourceKind.Buffer, 7);
        manager.PublishMetadata(replacement, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        manager.PublishMetadata(texture, facts, GpuMetadataChange.Storage, renewTextureAssociation: true);
        // Assert: Reusing the numeric name reports new backing metadata and availability.
        Assert.Equal(ResourceChangeKind.Metadata | ResourceChangeKind.Storage | ResourceChangeKind.Availability, recipient.Changes.Last().Kind);
    }

    /// <summary>Detached associations stop receiving availability changes from their former backing.</summary>
    [Fact]
    public void DetachedBufferTextureDoesNotFollowItsFormerBacking()
    {
        // Arrange: Publish a buffer texture with its original buffer association.
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
        // Act / Assert: Withdrawing the former backing cannot change a detached texture.
        buffer.Withdraw();
        Assert.True(texture.IsUsable);
        Assert.Equal(revision, texture.MetadataRevision);
    }

    /// <summary>A callback mutation supersedes the outer completion without delivering stale facts to remaining aliases.</summary>
    [Fact]
    public void CallbackReentryStopsObsoleteDelivery()
    {
        // Arrange: Attach two recipients whose callback publishes newer storage once.
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
        // Act / Assert: An outer publication is superseded before stale facts reach either recipient.
        manager.PublishMetadata(entry, new(64, BufferUsage.StaticDraw), GpuMetadataChange.None);
        Assert.Equal(2ul, first.Changes.Last().MetadataRevision);
        Assert.Equal(2ul, second.Changes.Last().MetadataRevision);
    }
    #endregion

    #region Thread ownership and lifecycle
    /// <summary>Context teardown completes a queued original retirement on the originating thread exactly once.</summary>
    [Fact]
    public void ContextRetirementCannotLoseQueuedTerminalDelivery()
    {
        // Arrange: Publish one query snapshot and record the owner thread for callbacks.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        using var recipient = new Recipient<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.AssociateResource(entry, recipient);
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        int origin = Environment.CurrentManagedThreadId;
        recipient.Callback = change => Assert.Equal(origin, Environment.CurrentManagedThreadId);
        // Act: Queue retirement from a worker, capture failures, and join before manager disposal.
        Exception? workerFailure = null;
        var worker = new Thread(() =>
        {
            try { manager.RetireResource(entry); }
            catch (Exception error) { workerFailure = error; }
        });
        worker.Start();
        try { worker.Join(); }
        finally { if (worker.IsAlive) worker.Join(); }
        // Assert: Worker retirement queued delivery without invoking the callback off-context.
        Assert.Null(workerFailure);
        Assert.Single(recipient.Changes);
        // Act: Complete the queued retirement on the owner thread.
        manager.Dispose();
        // Assert: Owner-context disposal delivers the terminal notification once.
        Assert.Equal(2, recipient.Changes.Count);
        // Act: Replay the captured retirement callback.
        entry.CaptureRetirement()();
        // Assert: Replaying the already-completed retirement cannot duplicate notification.
        Assert.Equal(2, recipient.Changes.Count);
        Assert.True((recipient.Changes.Last().Kind & ResourceChangeKind.Retired) != 0);
    }

    /// <summary>Worker retirement cannot own or steal a context-thread notification batch.</summary>
    [Fact]
    public void CompletionBatchRejectsForeignThreadsWhileRetirementStillQueues()
    {
        // Arrange: Create a recipient and capture both worker-thread outcomes.
        using var manager = Manager();
        var entry = manager.RegisterCreatedResource<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        using var recipient = new Recipient<GpuQueryMetadata>(GpuResourceKind.Query, 7);
        manager.AssociateResource(entry, recipient);
        manager.PublishMetadata(entry, default, GpuMetadataChange.None);
        Exception? foreignFailure = null, retirementFailure = null;
        // Act / Assert: The worker cannot own delivery but can queue retirement for the context thread.
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
        // Arrange / Act: Apply the lifecycle helper to every canonical metadata family.
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
        // Assert: CheckFamily verifies the same reserved, published, and retired transitions for every kind.
    }
    #endregion
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
        #region Public API
        public override nint ResourceId { get; protected set; } = id;

        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
        #endregion

        #region Protected/Internal
        internal readonly List<RHIResourceChange> Changes = new();
        internal Action<RHIResourceChange>? Callback;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        internal override void OnResourceChanged(in RHIResourceChange change)
        {
            Changes.Add(change);
            Callback?.Invoke(change);
        }
        #endregion
    }
    #endregion
}
