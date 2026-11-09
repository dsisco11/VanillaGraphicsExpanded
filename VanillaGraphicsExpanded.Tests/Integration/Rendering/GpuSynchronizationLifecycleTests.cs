using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Qualifies raw query/fence lifetimes and their query-free managed handoff.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed unsafe class GpuSynchronizationLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both native roots preserve reservation, legal realization and original identity.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void QueryFactoriesAndRealizationReuseOriginalEntry(bool core)
    {
        // Arrange: reserve one native name through either binding root.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        var manager = RHIResourceManagerSystem.CurrentManager!;
        uint name = 0;
        ((delegate* unmanaged[Stdcall]<int, uint*, void>)table.Read("glGenQueries"))(1, &name);
        try
        {
            // Act: realize the name, end its legal interval, then submit invalid target operations.
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuQueryMetadata>>(manager.FindResource(GpuResourceKind.Query, (int)name));
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            Assert.Null(manager.AcquireResource(GpuResourceKind.Query, (int)name, new ForbiddenReader<GpuQueryMetadata>()));
            Assert.Null(manager.RecoverResource(GpuResourceKind.Query, (int)name, new ForbiddenReader<GpuQueryMetadata>()));
            ((delegate* unmanaged[Stdcall]<int, uint, void>)table.Read("glBeginQuery"))((int)QueryTarget.SamplesPassed, name);
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            ((delegate* unmanaged[Stdcall]<int, void>)table.Read("glEndQuery"))((int)QueryTarget.SamplesPassed);
            // Assert: the legal begin realized the reservation and invalid begins left it unchanged.
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
        finally { EndQueryIfActive(QueryTarget.SamplesPassed, (int)name); GL.DeleteQuery((int)name); fixture.CleanupGpuResources(); }
    }

    /// <summary>Core target factories preserve stale output bytes and native errors on rejection.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void RejectedQueryFactoriesDoNotOverwriteLiveOutputs(bool core)
    {
        // Arrange: create one timestamp query and retain its original native identity for cleanup.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        uint name = 0;
        var create = (delegate* unmanaged[Stdcall]<int, int, uint*, void>)table.Read("glCreateQueries");
        create((int)QueryTarget.Timestamp, 1, &name);
        uint originalName = name;
        try
        {
            // Act: invalid factory requests must leave both the live identity and output unchanged.
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = manager.FindResource(GpuResourceKind.Query, (int)name)!;
            Assert.True(original.IsRealized); Assert.True(original.IsUsable);
            create((int)QueryTarget.Timestamp, -1, &name);
            Assert.Equal(originalName, name);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            create((int)BufferTarget.ArrayBuffer, 1, &name);
            Assert.Equal(originalName, name);
            // Assert: rejected factories preserve the original registration and output bytes.
            Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            Assert.Same(original, manager.FindResource(GpuResourceKind.Query, (int)name));
            Assert.Equal(1ul, original.MetadataRevision);
            ((delegate* unmanaged[Stdcall]<uint, int, void>)table.Read("glQueryCounter"))(originalName, (int)QueryCounterTarget.Timestamp);
            Assert.Equal(1ul, original.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteQuery((int)originalName); fixture.CleanupGpuResources(); }
    }

    /// <summary>Occlusion category conflicts cannot realize a second reserved object.</summary>
    [Fact]
    public void ActiveQueryRejectionLeavesReservationUnavailable()
    {
        // Arrange: create two names whose occlusion queries conflict on the same active slot.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        bool queryActive = false;
        try
        {
            // Act: reject the second begin while the first interval owns the slot.
            GL.BeginQuery(QueryTarget.SamplesPassed, first);
            queryActive = true;
            GL.BeginQuery(QueryTarget.AnySamplesPassed, second);
            // Assert: the conflicting begin is rejected without realizing the second reservation.
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            var entry = RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!;
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            GL.EndQuery(QueryTarget.SamplesPassed);
            queryActive = false;
            GL.BeginQuery(QueryTarget.AnySamplesPassed, second); GL.EndQuery(QueryTarget.AnySamplesPassed);
            Assert.True(entry.IsUsable); Assert.Equal(1ul, entry.MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (queryActive) GL.EndQuery(QueryTarget.SamplesPassed);
            GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources();
        }
    }

    /// <summary>ARB generation and begin/end aliases deliver through the same canonical lifetime.</summary>
    [Fact]
    public void ArbQueryAliasesRealizeWithoutDiscovery()
    {
        // Arrange: reserve a query through the ARB alias surface.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int name = GL.Arb.GenQuery();
        try
        {
            // Act: exercise the ARB begin/end/delete aliases against that same registration.
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var original = manager.FindResource(GpuResourceKind.Query, name)!;
            GL.Arb.BeginQuery(QueryTarget.SamplesPassed, name); GL.Arb.EndQuery(QueryTarget.SamplesPassed);
            // Assert: the alias path publishes to the existing canonical registration.
            Assert.True(original.IsUsable); Assert.Equal(1ul, original.MetadataRevision);
            Assert.Same(original, manager.AcquireResource(GpuResourceKind.Query, name, new ForbiddenReader<GpuQueryMetadata>()));
            GL.Arb.DeleteQuery(name);
            Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { EndQueryIfActive(QueryTarget.SamplesPassed, name); GL.DeleteQuery(name); fixture.CleanupGpuResources(); }
    }

    /// <summary>Fence results retain native width and default metadata without probing status.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FenceCreationAndDeletionTrackReturnedPointer(bool core)
    {
        // Arrange: select one native binding root and resolve its sync creation function.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var table = OpenGLBindingTable.For(core ? typeof(OpenTK.Graphics.OpenGL4.GL) : typeof(GL));
        var fence = (delegate* unmanaged[Stdcall]<int, int, nint>)table.Read("glFenceSync");
        // Act: create, validate, and delete the sync through its native-width pointer.
        nint id = fence((int)SyncCondition.SyncGpuCommandsComplete, 0);
        try
        {
            // Assert: the returned pointer owns one usable registration and invalid flags are rejected.
            var manager = RHIResourceManagerSystem.CurrentManager!;
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
        // Arrange: observe the managed creation hooks and acquire one query and fence owner.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var calls = new SynchronizationCallCapture();
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var fence = context.InsertFence();
        Assert.Equal(1, calls.QueryCreations); Assert.Equal(1, calls.FenceCreations); Assert.Equal(0, calls.IdentityReads);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        Assert.Equal(1ul, manager.GetResourceRegistration(query)!.MetadataRevision);
        Assert.Equal(1ul, manager.GetResourceRegistration(fence)!.MetadataRevision);
        // Act: publish a timestamp, externally reissue it, then use the managed owner again.
        context.WriteTimestamp(query);
        var ticket = context.RequestQueryResult(query);
        GL.QueryCounter(query.QueryId, QueryCounterTarget.Timestamp);
        Assert.Equal(CompletionStatus.Failed, ticket.Wait());
        Assert.Equal(1ul, manager.GetResourceRegistration(query)!.MetadataRevision);
        context.WriteTimestamp(query);
        Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
        Assert.Equal(CompletionStatus.Ready, context.WaitCpu(fence, TimeSpan.FromSeconds(1)));
        // Assert: external reissue invalidates only the result ticket, not descriptor identity.
        query.Dispose(); fence.Dispose(); fixture.CleanupGpuResources();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Missing cached stream limits cannot be mistaken for either rejection or default realization.</summary>
    [Fact]
    public void UncertainIndexedRealizationDoesNotFabricateDefaults()
    {
        // Arrange: suppress cached capability facts for the first indexed begin.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = GL.GenQuery();
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var entry = manager.FindResource(GpuResourceKind.Query, id)!;
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        bool previous = (bool)initialized.GetValue(null)!;
        bool indexedQueryActive = false;
        try
        {
            // Act: record and end the uncertain interval, then explicitly ask the driver for its identity result.
            initialized.SetValue(null, false);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, id);
            indexedQueryActive = true;
            initialized.SetValue(null, previous);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            indexedQueryActive = false;
            // Assert: uncertainty is retained until an explicit driver identity query succeeds.
            Assert.False(entry.IsUsable);
            GL.BeginQuery(QueryTarget.TimeElapsed, id);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.False(entry.IsUsable);
            // This is an explicit caller result, not an acquisition-triggered probe.
            Assert.True(GL.IsQuery(id)); Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
        }
        finally
        {
            initialized.SetValue(null, previous);
            if (indexedQueryActive) GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            GL.DeleteQuery(id); fixture.CleanupGpuResources();
        }
    }

    /// <summary>An uncertain accepted interval must still prevent false realization of a conflicting reservation.</summary>
    [Fact]
    public void UncertainBeginRetainsPossiblyOccupiedSlotAcrossCapabilityRestoration()
    {
        // Arrange: reserve two indexed query names and temporarily withdraw cached stream limits.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        bool previous = (bool)initialized.GetValue(null)!;
        bool indexedQueryActive = false;
        try
        {
            // Act: verify the uncertain first begin blocks the second until the slot is ended.
            initialized.SetValue(null, false);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, first);
            indexedQueryActive = true;
            initialized.SetValue(null, previous);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, second);
            // Assert: the uncertain first interval still occupies the indexed slot.
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            var entry = RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!;
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            indexedQueryActive = false;
            // Assert: the previously established target and index permit normal end and realization.
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, second);
            indexedQueryActive = true;
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            indexedQueryActive = false;
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            initialized.SetValue(null, previous);
            if (indexedQueryActive) GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Previously established target/index facts are sufficient to observe end without recapturing limits.</summary>
    [Fact]
    public void EstablishedIndexedEndDoesNotRequireCapabilitiesToRemainCached()
    {
        // Arrange: establish an indexed interval while stream limits are cached.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        bool previous = (bool)initialized.GetValue(null)!;
        bool indexedQueryActive = false;
        try
        {
            // Act: withdraw capability cache state only after begin, then complete both intervals.
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, first);
            indexedQueryActive = true;
            initialized.SetValue(null, false);
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            indexedQueryActive = false;
            initialized.SetValue(null, previous);
            GL.BeginQueryIndexed(QueryTarget.PrimitivesGenerated, 1, second);
            indexedQueryActive = true;
            GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
            indexedQueryActive = false;
            // Assert: the second interval is realized using the target/index facts established before cache withdrawal.
            Assert.True(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            initialized.SetValue(null, previous);
            if (indexedQueryActive) GL.EndQueryIndexed(QueryTarget.PrimitivesGenerated, 1);
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
        bool conditionalRenderActive = false;
        try
        {
            // Arrange: both query results are ready before the raw external interval is opened.
            context.BeginQuery(first); context.EndQuery(first);
            context.BeginQuery(second); context.EndQuery(second);
            // Act: alternate raw intervals with managed attempts to begin or end another owner's interval.
            GL.BeginConditionalRender(first.QueryId, ConditionalRenderType.QueryWait);
            conditionalRenderActive = true;
            // Assert: conflicts are rejected while native conditional state remains valid.
            Assert.Throws<InvalidOperationException>(() => context.BeginConditionalRender(second, ConditionalRenderPolicy.Wait));
            Assert.Equal(ErrorCode.NoError, GL.GetError()); GL.EndConditionalRender(); conditionalRenderActive = false;
            context.BeginConditionalRender(first, ConditionalRenderPolicy.Wait);
            conditionalRenderActive = true;
            GL.EndConditionalRender();
            conditionalRenderActive = false;
            GL.BeginConditionalRender(sameQuery ? first.QueryId : second.QueryId, ConditionalRenderType.QueryWait);
            conditionalRenderActive = true;
            Assert.Throws<InvalidOperationException>(() => context.EndConditionalRender());
            Assert.True(OpenGlStateCache.Current.HasQueryCondition);
            Assert.Equal(ErrorCode.NoError, GL.GetError()); GL.EndConditionalRender(); conditionalRenderActive = false;
            context.BeginConditionalRender(first, ConditionalRenderPolicy.Wait); conditionalRenderActive = true;
            context.EndConditionalRender(); conditionalRenderActive = false;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            // Close a raw conditional interval left active by a failed assertion before disposing its owners.
            if (conditionalRenderActive) GL.EndConditionalRender();
            first.Dispose(); second.Dispose(); fixture.CleanupGpuResources();
        }
    }

    /// <summary>A raw restart of the same named query is a different interval from the managed begin.</summary>
    [Fact]
    public void ManagedEndCannotTerminateExternalReissueOfTheSameQuery()
    {
        // Arrange: start through the managed owner before restarting the same name natively.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var owner = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        bool queryActive = false;
        try
        {
            // Act: reject managed end for the externally restarted interval, then resume managed ownership.
            context.BeginQuery(owner);
            queryActive = true;
            GL.EndQuery(QueryTarget.SamplesPassed);
            queryActive = false;
            GL.BeginQuery(QueryTarget.SamplesPassed, owner.QueryId);
            // Assert: a managed end cannot terminate the externally restarted interval.
            queryActive = true;
            Assert.Throws<InvalidOperationException>(() => context.EndQuery(owner));
            Assert.True(OpenGlStateCache.Current.HasQueryStartConflict((int)QueryTarget.SamplesPassed, owner.QueryId));
            Assert.Equal(ErrorCode.NoError, GL.GetError()); GL.EndQuery(QueryTarget.SamplesPassed); queryActive = false;
            context.BeginQuery(owner); queryActive = true;
            context.EndQuery(owner); queryActive = false;
            Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(owner).Wait());
        }
        finally
        {
            if (queryActive) GL.EndQuery(QueryTarget.SamplesPassed);
            owner.Dispose(); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Supplied local owners need no discovery, but cannot bypass a later manager's missing-identity checks.</summary>
    [Fact]
    public void LocalOwnersCannotBypassSubsequentCanonicalAdmission()
    {
        // Arrange: retire canonical services before creating owners backed only by local associations.
        fixture.EnsureContextValid();
        var context = RHICommandContext.Current;
        fixture.RetireResourceServices();
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var fence = context.InsertFence();
        // Act: submit with local owners, then restore canonical services and retry validity checks.
        Assert.Null(RHIResourceManagerSystem.CurrentManager);
        Assert.True(((GpuQueryRef)query).IsValid); Assert.True(((GpuSyncRef)fence).IsValid);
        context.WriteTimestamp(query);
        Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
        Assert.Equal(CompletionStatus.Ready, context.Poll(fence));
        fixture.InitializeResourceDisposal();
        // Assert: canonical admission withdraws the former local associations.
        Assert.False(((GpuQueryRef)query).IsValid); Assert.False(((GpuSyncRef)fence).IsValid);
#if DEBUG
        // Owners must not submit after canonical admission withdraws their local association.
        // Release retains operational safety checks without repeating this misuse diagnostic.
        Assert.Throws<InvalidOperationException>(() => context.WriteTimestamp(query));
        Assert.Throws<InvalidOperationException>(() => context.Poll(fence));
#endif
        query.Dispose(); fence.Dispose(); fixture.CleanupGpuResources();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Deletion retires a name immediately while its active original interval can still be ended.</summary>
    [Fact]
    public void DeletedActiveQueryKeepsItsOriginalIntervalUntilEnd()
    {
        // Arrange: create the managed owner and preserve its original registration for identity checks.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var context = RHICommandContext.Current;
        using var owner = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
        var original = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(owner)!;
        bool queryActive = false;
        try
        {
            // Act: delete during the active interval, end that interval, then create a replacement name.
            context.BeginQuery(owner);
            queryActive = true;
            GL.DeleteQuery(owner.QueryId);
            Assert.Equal(GpuResourceRegistrationState.Deleted, original.State);
            Assert.False(owner.IsValid);
            context.EndQuery(owner); queryActive = false;
            // Assert: the deleted registration is retired, its interval was endable, and the new name gets a distinct registration.
            Assert.Throws<InvalidOperationException>(() => context.RequestQueryResult(owner));
            using var next = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
            Assert.NotSame(original, RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(next));
            context.BeginQuery(next); queryActive = true;
            context.EndQuery(next); queryActive = false;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (queryActive) GL.EndQuery(QueryTarget.SamplesPassed);
            owner.Dispose(); fixture.CleanupGpuResources();
        }
    }

    /// <summary>Occlusion end acceptance is checked against the real driver before subsequent realization is certified.</summary>
    [Fact]
    public void CrossModeOcclusionEndPreservesNativeActivitySemantics()
    {
        // Arrange: keep both raw query names available for the driver-dependent end-mode outcome.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int first = GL.GenQuery(), second = GL.GenQuery();
        try
        {
            // Act: observe whether the mismatched end ended the native interval before starting the second query.
            GL.BeginQuery(QueryTarget.SamplesPassed, first);
            GL.EndQuery(QueryTarget.AnySamplesPassed);
            _ = GL.GetError();
            GL.GetQuery(QueryTarget.SamplesPassed, GetQueryParam.CurrentQuery, out int active);
            GL.BeginQuery(QueryTarget.SamplesPassed, second);
            // Assert: the driver's observed active query determines which outcome is valid.
            ErrorCode result = GL.GetError();
            var entry = RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Query, second)!;
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
        finally { EndQueryIfActive(QueryTarget.SamplesPassed, first, second); GL.DeleteQuery(first); GL.DeleteQuery(second); fixture.CleanupGpuResources(); }
    }

    /// <summary>Binding reload retains original identities; terminal manager retirement never renews them.</summary>
    [Fact]
    public void ReloadAndRetirementPreserveOriginalSynchronizationLifetimes()
    {
        // Arrange: register one query and one fence before reloading both binding roots.
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int query = GL.GenQuery();
        nint sync = GL.FenceSync(SyncCondition.SyncGpuCommandsComplete, WaitSyncFlags.None);
        var manager = RHIResourceManagerSystem.CurrentManager!;
        var queryEntry = manager.FindResource(GpuResourceKind.Query, query)!;
        var syncEntry = manager.FindResource(GpuResourceKind.Sync, sync)!;
        try
        {
            // Act: reload bindings, publish query metadata, then retire the owning resource manager.
            GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
            OpenTK.Graphics.OpenGL4.GL.LoadBindings(new OpenTK.Windowing.GraphicsLibraryFramework.GLFWBindingsContext());
            GL.QueryCounter(query, QueryCounterTarget.Timestamp);
            Assert.Same(queryEntry, manager.FindResource(GpuResourceKind.Query, query));
            Assert.Equal(1ul, queryEntry.MetadataRevision); Assert.Equal(1ul, syncEntry.MetadataRevision);
            fixture.RetireResourceServices();
            // Assert: binding reload preserves identities and retirement is terminal.
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, queryEntry.State);
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, syncEntry.State);
            GL.DeleteQuery(query); query = 0;
            GL.DeleteSync(sync); sync = 0;
            Assert.False(queryEntry.IsUsable); Assert.False(syncEntry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (query != 0) GL.DeleteQuery(query);
            if (sync != 0) GL.DeleteSync(sync);
            fixture.CleanupGpuResources();
        }
    }
    #endregion

    #region Private
    /// <summary>Ends a non-indexed native interval only when its current name belongs to this test.</summary>
    /// <param name="target">The target whose current native query must be checked.</param>
    /// <param name="ownedQueryIds">Native query names this test is allowed to end.</param>
    private static void EndQueryIfActive(QueryTarget target, params int[] ownedQueryIds)
    {
        GL.GetQuery(target, GetQueryParam.CurrentQuery, out int activeQuery);
        if (activeQuery != 0 && System.Array.IndexOf(ownedQueryIds, activeQuery) >= 0) GL.EndQuery(target);
    }

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
