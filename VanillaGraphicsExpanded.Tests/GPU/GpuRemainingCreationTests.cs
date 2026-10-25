using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks empty-schema creation, native realization, and ownership handoff for remaining resource families.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuRemainingCreationTests(HeadlessGLFixture fixture, Xunit.ITestOutputHelper output)
{
    private static int queries;
    #region Public API
    /// <summary>Every supported family publishes an empty snapshot in both manager and context-local operation.</summary>
    [Theory]
    [InlineData(false, false)] [InlineData(false, true)]
    [InlineData(true, false)] [InlineData(true, true)]
    public void CreationPreservesSelectionsAndNativeSemantics(bool registered, bool direct)
    {
        fixture.EnsureContextValid();
        output.WriteLine($"Renderer={fixture.GLRenderer}; DSA={GpuSupport.Graphics.SupportsArbDirectStateAccess}; semaphore={GpuSemaphore.IsSupported}; memory={GpuMemoryObject.IsSupported}");
        if (registered) fixture.InitializeResourceDisposal();
        using var capability = new DirectStateAccessCapabilityScope(direct);
        try
        {
            using var previousVao = GpuVao.Create();
            using var previousPipeline = GpuProgramPipeline.Create();
            using var previousSampler = GpuSampler.Create();
            previousSampler.Bind(0);
            using var vaoScope = previousVao.BindScope();
            using var pipelineScope = StateCache.Current.BindProgramPipelineScope(previousPipeline.PipelineId);
            using var observation = new CreationQueryObservation();
            using var vao = GpuVao.Create();
            using var pipeline = GpuProgramPipeline.Create();
            using var sampler = GpuSampler.Create();
            using var query = GpuQuery.Create();
            using var feedback = GpuTransformFeedback.Create();
            using var fence = GpuFence.Insert();
            using var semaphore = GpuSemaphore.IsSupported ? GpuSemaphore.Create() : null;
            using var memory = GpuMemoryObject.IsSupported ? GpuMemoryObject.Create() : null;
            Assert.Equal(0, queries);
            observation.Dispose();
            foreach (var resource in new GpuResource[] { vao, pipeline, sampler, query, feedback, fence })
            {
                Assert.True(resource.IsValid);
                if (registered)
                {
                    var manager = GpuResourceManagerSystem.CurrentManager!;
                    Assert.True(manager.IsResourceMetadataAvailable(resource));
                    Assert.Equal(1UL, manager.GetResourceRegistration(resource)!.MetadataRevision);
                }
            }
            foreach (var resource in new GpuResource?[] { semaphore, memory }.OfType<GpuResource>())
            {
                Assert.True(resource.IsValid);
                if (registered) Assert.True(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(resource));
            }
            Assert.Equal(previousVao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(previousPipeline.PipelineId, GL.GetInteger(GetPName.ProgramPipelineBinding));
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 0, out int selectedSampler);
            Assert.Equal(previousSampler.SamplerId, selectedSampler);
            Assert.True(GL.IsVertexArray(vao.VertexArrayId));
            Assert.True(GL.IsProgramPipeline(pipeline.PipelineId));
            Assert.True(GL.IsSampler(sampler.SamplerId));
            // Generated queries and transform feedback names retain their deferred realization contract.
            Assert.False(GL.IsQuery(query.QueryId));
            Assert.False(GL.IsTransformFeedback(feedback.TransformFeedbackId));
            GL.BeginQuery(QueryTarget.TimeElapsed, query.QueryId);
            GL.EndQuery(QueryTarget.TimeElapsed);
            Assert.True(GL.IsQuery(query.QueryId));
            using (feedback.BindScope()) Assert.True(GL.IsTransformFeedback(feedback.TransformFeedbackId));
            Assert.True(GL.IsSync(fence.ResourceId));
            Assert.Contains(fence.Wait(TimeSpan.FromSeconds(1)), new[] { WaitSyncStatus.AlreadySignaled, WaitSyncStatus.ConditionSatisfied });
            Assert.True(fence.TryConsumeIfSignaled());
            Assert.Equal((nint)0, fence.ResourceId);
            sampler.SetFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
            GL.GetSamplerParameter(sampler.SamplerId, SamplerParameterName.TextureMinFilter, out int filter);
            Assert.Equal((int)TextureMinFilter.Nearest, filter);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Unsupported extension requests reject before allocating or publishing an identity.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void UnsupportedExtensionsRejectCreation(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        var original = GpuSupport.Graphics;
        try
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null,
                original with { SupportsExtSemaphore = false, SupportsExtMemoryObject = false });
            Assert.Throws<NotSupportedException>(() => GpuSemaphore.Create());
            Assert.Throws<NotSupportedException>(() => GpuMemoryObject.Create());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Rejecting identity delivery retires every supported family through the common cleanup path.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedIdentityHandoffClearsEveryFamily(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        using var deletion = new DeletionObservation();
        try
        {
            {
                using var recipient = new FailedRecipient<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateVertexArray(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteVertexArray", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuProgramPipelineMetadata>(GpuResourceKind.ProgramPipeline);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateProgramPipeline(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteProgramPipeline", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuSamplerMetadata>(GpuResourceKind.Sampler);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateSampler(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteSampler", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuQueryMetadata>(GpuResourceKind.Query);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateQuery(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteQuery", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuSyncMetadata>(GpuResourceKind.Sync);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateSync(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteSync", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuTransformFeedbackMetadata>(GpuResourceKind.TransformFeedback);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateTransformFeedback(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteTransformFeedback", recipient.Attempted), DeletionObservation.Deleted);
            }
            if (GpuSemaphore.IsSupported)
            {
                using var recipient = new FailedRecipient<GpuSemaphoreMetadata>(GpuResourceKind.Semaphore);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateSemaphore(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteSemaphore", recipient.Attempted), DeletionObservation.Deleted);
            }
            if (GpuMemoryObject.IsSupported)
            {
                using var recipient = new FailedRecipient<GpuMemoryObjectMetadata>(GpuResourceKind.MemoryObject);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => GpuResourceManager.CreateMemoryObject(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(GpuResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Contains(("DeleteMemoryObject", recipient.Attempted), DeletionObservation.Deleted);
            }
            GpuResourceManagerSystem.CurrentManager?.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Records actual deletion dispatch, including generated names that IsObject cannot distinguish from deleted names.</summary>
    private sealed class DeletionObservation : IDisposable
    {
        private readonly Harmony harmony = new("Tests.RemainingCreationDeletion." + Guid.NewGuid());
        internal static readonly HashSet<(string, nint)> Deleted = new();
        /// <summary>Observes each family native deletion entry point.</summary>
        public DeletionObservation()
        {
            Deleted.Clear();
            foreach (string name in new[] { "DeleteVertexArray", "DeleteProgramPipeline", "DeleteSampler", "DeleteQuery", "DeleteTransformFeedback" })
                harmony.Patch(AccessTools.Method(typeof(GL), name, [typeof(int)]), prefix: new HarmonyMethod(typeof(DeletionObservation), nameof(CountInteger)));
            harmony.Patch(AccessTools.Method(typeof(GL), "DeleteSync", [typeof(IntPtr)]), prefix: new HarmonyMethod(typeof(DeletionObservation), nameof(CountPointer)));
            foreach (string name in new[] { "DeleteSemaphore", "DeleteMemoryObject" })
                harmony.Patch(AccessTools.Method(typeof(GL.Ext), name, [typeof(int)]), prefix: new HarmonyMethod(typeof(DeletionObservation), nameof(CountInteger)));
        }
        /// <summary>Records integer native identity without altering execution.</summary>
        private static void CountInteger(System.Reflection.MethodBase __originalMethod, int __0) => Deleted.Add((__originalMethod.Name, __0));
        /// <summary>Records full-width synchronization identity without narrowing.</summary>
        private static void CountPointer(System.Reflection.MethodBase __originalMethod, IntPtr __0) => Deleted.Add((__originalMethod.Name, __0));
        /// <summary>Restores native methods after observation.</summary>
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }

    /// <summary>Observes descriptor and existence queries only during controlled creation.</summary>
    private sealed class CreationQueryObservation : IDisposable
    {
        private readonly Harmony harmony = new("Tests.RemainingCreationQueries." + Guid.NewGuid());
        /// <summary>Installs observation on the native query overloads without changing dispatch.</summary>
        public CreationQueryObservation()
        {
            queries = 0;
            string[] names = ["GetSamplerParameter", "GetQuery", "GetQueryObject", "GetSync", "GetProgramPipeline", "GetVertexArray", "IsVertexArray", "IsProgramPipeline", "IsSampler", "IsQuery", "IsTransformFeedback", "IsSync"];
            foreach (var method in typeof(GL).GetMethods().Where(method => names.Contains(method.Name) && !method.ContainsGenericParameters))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(CreationQueryObservation), nameof(Count)));
            foreach (var method in typeof(GL.Ext).GetMethods().Where(method => new[] { "IsSemaphore", "IsMemoryObject", "GetSemaphoreParameter", "GetMemoryObjectParameter" }.Contains(method.Name) && !method.ContainsGenericParameters))
                harmony.Patch(method, prefix: new HarmonyMethod(typeof(CreationQueryObservation), nameof(Count)));
        }
        /// <summary>Counts the observed native call.</summary>
        private static void Count() => queries++;
        /// <summary>Removes all hooks owned by this observation.</summary>
        public void Dispose() => harmony.UnpatchAll(harmony.Id);
    }

    /// <summary>Injects failure after native allocation while retaining pointer-width identity evidence.</summary>
    private sealed class FailedRecipient<T>(GpuResourceKind kind) : GpuResource<T> where T : struct
    {
        private nint id;
        internal nint Attempted;
        /// <inheritdoc />
        public override nint ResourceId
        {
            get => id;
            protected set
            {
                id = value;
                if (value != 0) { Attempted = value; throw new InvalidOperationException("Injected identity failure."); }
            }
        }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
    }
    #endregion
}


