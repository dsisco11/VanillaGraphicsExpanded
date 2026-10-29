using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies raw query/fence lifetimes and their query-free managed handoff.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuSynchronizationLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both native roots preserve reservation, legal realization and original identity.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void QueryFactoriesAndRealizationReuseOriginalEntry(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        var manager = GpuResourceManagerSystem.CurrentManager!;
        uint name = 0;
        ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glGenQueries"))(1, &name);
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuQueryMetadata>>(manager.FindResource(GpuResourceKind.Query, (int)name));
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Query, (int)name, new ForbiddenReader<GpuQueryMetadata>()));
            Assert.Null(manager.RecoverResource(GpuResourceKind.Query, (int)name, new ForbiddenReader<GpuQueryMetadata>()));
            ((delegate* unmanaged[Stdcall]<int, uint, void>)table.Read("glBeginQuery"))((int)QueryTarget.SamplesPassed, name);
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            ((delegate* unmanaged[Stdcall]<int, void>)table.Read("glEndQuery"))((int)QueryTarget.SamplesPassed);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Query, (int)name, new ForbiddenReader<GpuQueryMetadata>()));
            // Target mismatch and an invalid indexed begin must preserve the original empty snapshot.
            ((delegate* unmanaged[Stdcall]<int, uint, uint, void>)table.Read("glBeginQueryIndexed"))((int)QueryTarget.TimeElapsed, 1, name);
            Assert.NotEqual(ErrorCode.NoError, GL.GetError());
            Assert.Equal(1ul, entry.MetadataRevision);
            ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glDeleteQueries"))(1, &name);
            Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Query, (int)name, new ForbiddenReader<GpuQueryMetadata>()));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery((int)name); fixture.CleanupGpuResources(); }
    }

    /// <summary>Core target factories preserve stale output bytes and native errors on rejection.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectedQueryFactoriesDoNotOverwriteLiveOutputs(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        uint name = 0;
        var create = (delegate* unmanaged[Stdcall]<int, int, uint*, void>)table.Read("glCreateQueries");
        create((int)QueryTarget.Timestamp, 1, &name);
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = manager.FindResource(GpuResourceKind.Query, (int)name)!;
            Assert.True(original.IsRealized); Assert.True(original.IsUsable);
            create((int)QueryTarget.Timestamp, -1, &name);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            create((int)BufferTarget.ArrayBuffer, 1, &name);
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Same(original, manager.FindResource(GpuResourceKind.Query, (int)name));
            Assert.Equal(1ul, original.MetadataRevision);
            ((delegate* unmanaged[Stdcall]<uint, int, void>)table.Read("glQueryCounter"))(name, (int)QueryCounterTarget.Timestamp);
            Assert.Equal(1ul, original.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery((int)name); fixture.CleanupGpuResources(); }
    }

    /// <summary>Occlusion category conflicts cannot realize a second reserved object.</summary>
    [Fact]
    public void ActiveQueryRejectionLeavesReservationUnavailable()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        try
        {
            GL.BeginQuery(QueryTarget.SamplesPassed, first);
            GL.BeginQuery(QueryTarget.AnySamplesPassed, second);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            var entry = GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!;
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            GL.EndQuery(QueryTarget.SamplesPassed);
            GL.BeginQuery(QueryTarget.AnySamplesPassed, second); GL.EndQuery(QueryTarget.AnySamplesPassed);
            Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources(); }
    }

    /// <summary>ARB generation and begin/end aliases deliver through the same canonical lifetime.</summary>
    [Fact]
    public void ArbQueryAliasesRealizeWithoutDiscovery()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int name = GL.Arb.GenQuery();
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = manager.FindResource(GpuResourceKind.Query, name)!;
            GL.Arb.BeginQuery(QueryTarget.SamplesPassed, name); GL.Arb.EndQuery(QueryTarget.SamplesPassed);
            Assert.True(original.IsUsable); Assert.Equal(1ul, original.MetadataRevision);
            Assert.Same(original, manager.AcquireResource(GpuResourceKind.Query, name, new ForbiddenReader<GpuQueryMetadata>()));
            GL.Arb.DeleteQuery(name);
            Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery(name); fixture.CleanupGpuResources(); }
    }

    /// <summary>Fence results retain native width and default metadata without probing status.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FenceCreationAndDeletionTrackReturnedPointer(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        var fence = (delegate* unmanaged[Stdcall]<int, int, nint>)table.Read("glFenceSync");
        nint id = fence((int)SyncCondition.SyncGpuCommandsComplete, 0);
        try
        {
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuSyncMetadata>>(manager.FindResource(GpuResourceKind.Sync, id));
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            Assert.Same(entry, manager.AcquireResource(GpuResourceKind.Sync, id, new ForbiddenReader<GpuSyncMetadata>()));
            Assert.Equal((nint)0, fence((int)SyncCondition.SyncGpuCommandsComplete, 1));
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(1ul, entry.MetadataRevision);
            ((delegate* unmanaged[Stdcall]<nint, void>)table.Read("glDeleteSync"))(id);
            Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State); id = 0;
        }
        finally { if (id != 0) GL.DeleteSync(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Saved-original creation publishes once and external reissue invalidates a result ticket.</summary>
    [Fact]
    public void ManagedHandoffAndExternalReissueHaveSeparateDescriptorAndResultRevisions()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var calls = new SynchronizationCallCapture();
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var fence = context.InsertFence();
        Assert.Equal(1, calls.QueryCreations); Assert.Equal(1, calls.FenceCreations); Assert.Equal(0, calls.IdentityReads);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        Assert.Equal(1ul, manager.GetResourceRegistration(query)!.MetadataRevision);
        Assert.Equal(1ul, manager.GetResourceRegistration(fence)!.MetadataRevision);
        context.WriteTimestamp(query);
        var ticket = context.RequestQueryResult(query);
        GL.QueryCounter(query.QueryId, QueryCounterTarget.Timestamp);
        Assert.Equal(CompletionStatus.Failed, ticket.Wait());
        Assert.Equal(1ul, manager.GetResourceRegistration(query)!.MetadataRevision);
        context.WriteTimestamp(query);
        Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
        Assert.Equal(CompletionStatus.Ready, context.WaitCpu(fence, TimeSpan.FromSeconds(1)));
        query.Dispose(); fence.Dispose(); fixture.CleanupGpuResources();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Missing cached stream limits cannot be mistaken for either rejection or default realization.</summary>
    [Fact]
    public void UncertainIndexedRealizationDoesNotFabricateDefaults()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = GL.GenQuery();
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var entry = manager.FindResource(GpuResourceKind.Query, id)!;
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        bool previous = (bool)initialized.GetValue(null)!;
        try
        {
            initialized.SetValue(null, false);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, id);
            initialized.SetValue(null, previous);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            Assert.False(entry.IsUsable);
            GL.BeginQuery(QueryTarget.TimeElapsed, id);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.False(entry.IsUsable);
            // This is an explicit caller result, not an acquisition-triggered probe.
            Assert.True(GL.IsQuery(id)); Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
        }
        finally { initialized.SetValue(null, previous); GL.DeleteQuery(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>An uncertain accepted interval must still prevent false realization of a conflicting reservation.</summary>
    [Fact]
    public void UncertainBeginRetainsPossiblyOccupiedSlotAcrossCapabilityRestoration()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        bool previous = (bool)initialized.GetValue(null)!;
        try
        {
            initialized.SetValue(null, false);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, first);
            initialized.SetValue(null, previous);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, second);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            var entry = GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!;
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, second);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            initialized.SetValue(null, previous);
            GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Previously established target/index facts are sufficient to observe end without recapturing limits.</summary>
    [Fact]
    public void EstablishedIndexedEndDoesNotRequireCapabilitiesToRemainCached()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        bool previous = (bool)initialized.GetValue(null)!;
        try
        {
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, first);
            initialized.SetValue(null, false);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            initialized.SetValue(null, previous);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, second);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            Assert.True(GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            initialized.SetValue(null, previous);
            GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources();
        }
    }

    /// <summary>External conditional intervals cannot be nested or ended by a different managed owner.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ConditionalRenderingRespectsExternalIntervalsAndReplacement(bool sameQuery)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var first = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        using var second = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        context.BeginQuery(first); context.EndQuery(first);
        context.BeginQuery(second); context.EndQuery(second);
        GL.BeginConditionalRender(first.QueryId, ConditionalRenderType.QueryWait);
        Assert.Throws<InvalidOperationException>(() => context.BeginConditionalRender(second, ConditionalRenderPolicy.Wait));
        Assert.Equal(ErrorCode.NoError, GL.GetError()); GL.EndConditionalRender();
        context.BeginConditionalRender(first, ConditionalRenderPolicy.Wait);
        GL.EndConditionalRender();
        GL.BeginConditionalRender(sameQuery ? first.QueryId : second.QueryId, ConditionalRenderType.QueryWait);
        Assert.Throws<InvalidOperationException>(() => context.EndConditionalRender());
        Assert.True(OpenGlStateCache.Current.HasQueryCondition);
        Assert.Equal(ErrorCode.NoError, GL.GetError()); GL.EndConditionalRender();
        context.BeginConditionalRender(first, ConditionalRenderPolicy.Wait); context.EndConditionalRender();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        first.Dispose(); second.Dispose(); fixture.CleanupGpuResources();
    }

    /// <summary>A raw restart of the same named query is a different interval from the managed begin.</summary>
    [Fact]
    public void ManagedEndCannotTerminateExternalReissueOfTheSameQuery()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var owner = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        context.BeginQuery(owner);
        GL.EndQuery(QueryTarget.SamplesPassed); GL.BeginQuery(QueryTarget.SamplesPassed, owner.QueryId);
        Assert.Throws<InvalidOperationException>(() => context.EndQuery(owner));
        Assert.True(OpenGlStateCache.Current.HasQueryStartConflict((int)QueryTarget.SamplesPassed, owner.QueryId));
        Assert.Equal(ErrorCode.NoError, GL.GetError()); GL.EndQuery(QueryTarget.SamplesPassed);
        context.BeginQuery(owner); context.EndQuery(owner);
        Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(owner).Wait());
        owner.Dispose(); fixture.CleanupGpuResources();
    }

    /// <summary>Supplied local owners need no discovery, but cannot bypass a later manager's missing-identity checks.</summary>
    [Fact]
    public void LocalOwnersCannotBypassSubsequentCanonicalAdmission()
    {
        fixture.EnsureContextValid();
        var context = RHICommandContext.Current;
        fixture.UseContextLocalResources();
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var fence = context.InsertFence();
        Assert.Null(GpuResourceManagerSystem.CurrentManager);
        Assert.True(((GpuQueryRef)query).IsValid); Assert.True(((GpuSyncRef)fence).IsValid);
        context.WriteTimestamp(query);
        Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
        Assert.Equal(CompletionStatus.Ready, context.Poll(fence));
        fixture.InitializeResourceDisposal();
        Assert.False(((GpuQueryRef)query).IsValid); Assert.False(((GpuSyncRef)fence).IsValid);
        Assert.Throws<InvalidOperationException>(() => context.WriteTimestamp(query));
        Assert.Throws<InvalidOperationException>(() => context.Poll(fence));
        query.Dispose(); fence.Dispose(); fixture.CleanupGpuResources();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Deletion retires a name immediately while its active original interval can still be ended.</summary>
    [Fact]
    public void DeletedActiveQueryKeepsItsOriginalIntervalUntilEnd()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var owner = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        var original = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
        context.BeginQuery(owner);
        GL.DeleteQuery(owner.QueryId);
        Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
        Assert.False(owner.IsValid);
        context.EndQuery(owner);
        Assert.Throws<InvalidOperationException>(() => context.RequestQueryResult(owner));
        using var next = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        Assert.NotSame(original, GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(next));
        context.BeginQuery(next); context.EndQuery(next);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        owner.Dispose(); next.Dispose(); fixture.CleanupGpuResources();
    }

    /// <summary>Occlusion end acceptance is checked against the real driver before subsequent realization is certified.</summary>
    [Fact]
    public void CrossModeOcclusionEndPreservesNativeActivitySemantics()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        try
        {
            GL.BeginQuery(QueryTarget.SamplesPassed, first);
            GL.EndQuery(QueryTarget.AnySamplesPassed);
            _ = GL.GetError();
            GL.GetQuery(QueryTarget.SamplesPassed, GetQueryParam.CurrentQuery, out int active);
            GL.BeginQuery(QueryTarget.SamplesPassed, second);
            ErrorCode result = GL.GetError();
            var entry = GpuResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!;
            if (active == first)
            {
                Assert.Equal(ErrorCode.InvalidOperation, result); Assert.False(entry.IsUsable);
                GL.EndQuery(QueryTarget.SamplesPassed);
            }
            else
            {
                Assert.Equal(0, active); Assert.Equal(ErrorCode.NoError, result); Assert.True(entry.IsUsable);
                GL.EndQuery(QueryTarget.SamplesPassed);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources(); }
    }

    /// <summary>Binding reload retains original identities; terminal manager retirement never renews them.</summary>
    [Fact]
    public void ReloadAndRetirementPreserveOriginalSynchronizationLifetimes()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int query = GL.GenQuery();
        nint sync = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        var manager = GpuResourceManagerSystem.CurrentManager!;
        var queryEntry = manager.FindResource(GpuResourceKind.Query, query)!;
        var syncEntry = manager.FindResource(GpuResourceKind.Sync, sync)!;
        try
        {
            GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
            GL.QueryCounter(query, QueryCounterTarget.Timestamp);
            Assert.Same(queryEntry, manager.FindResource(GpuResourceKind.Query, query));
            Assert.Equal(1ul, queryEntry.MetadataRevision); Assert.Equal(1ul, syncEntry.MetadataRevision);
            fixture.CleanupGpuResources();
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, queryEntry.State);
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, syncEntry.State);
            GL.DeleteQuery(query); GL.DeleteSync(sync);
            Assert.False(queryEntry.IsUsable); Assert.False(syncEntry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery(query); fixture.CleanupGpuResources(); }
    }
    #endregion

    #region Private
    /// <summary>Rejects routine descriptor and identity discovery for empty maintained schemas.</summary>
    private sealed class ForbiddenReader<T> : IGpuResourceMetadataReader<T> where T : struct
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Unexpected identity discovery.");
        /// <inheritdoc />
        public bool TryRead(nint id, out T metadata) => throw new InvalidOperationException("Unexpected descriptor discovery.");
    }
    #endregion
}
