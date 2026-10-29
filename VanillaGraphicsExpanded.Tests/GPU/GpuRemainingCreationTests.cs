using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
    [InlineData(false)] [InlineData(true)]
    public void CreationPreservesSelectionsAndNativeSemantics(bool registered)
    {
        fixture.EnsureContextValid();
        output.WriteLine($"Renderer={fixture.GLRenderer}; DSA={GpuSupport.Graphics.SupportsArbDirectStateAccess}; semaphore={RHICommandContext.Current.Capabilities.ExternalSemaphores}; memory={RHICommandContext.Current.Capabilities.ExternalMemory}");
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var previousVao = NativeResourceSetup.CreateVertexArray();
            using var previousPipeline = NativeResourceSetup.CreateProgramPipeline();
            using var previousSampler = RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource();
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageBindings.BindSampler(OpenGlStateCache.Current, 0, previousSampler.SamplerId);
            using var vaoScope = OpenGlStateCache.Current.BindVertexArrayScope(previousVao.VertexArrayId);
            using var pipelineScope = OpenGlStateCache.Current.BindProgramPipelineScope(previousPipeline.PipelineId);
            using var observation = new CreationQueryObservation();
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var pipeline = NativeResourceSetup.CreateProgramPipeline();
            using var sampler = RHICommandContext.Current.CreateSampler(new RHISamplerDescription {  }).RequireResource();
            using var query = RHICommandContext.Current.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource();
            using var feedback = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
            using var fence = RHICommandContext.Current.InsertFence();
            using var semaphore = RHICommandContext.Current.Capabilities.ExternalSemaphores ? RHICommandContext.Current.CreateSemaphore(new()).RequireResource() : null;
            using var memory = RHICommandContext.Current.Capabilities.ExternalMemory ? RHICommandContext.Current.CreateMemoryObject(new(Dedicated: false)).RequireResource() : null;
            Assert.Equal(0, queries);
            observation.Dispose();
            foreach (var resource in new GpuResource[] { vao, pipeline, sampler, query, feedback, fence })
            {
                Assert.True(resource.IsValid);
                if (registered)
                {
                    var manager = RHIResourceManagerSystem.CurrentManager!;
                    Assert.True(manager.IsResourceMetadataAvailable(resource));
                    Assert.Equal(1UL, manager.GetResourceRegistration(resource)!.MetadataRevision);
                }
            }
            foreach (var resource in new GpuResource?[] { semaphore, memory }.OfType<GpuResource>())
            {
                Assert.True(resource.IsValid);
                if (registered) Assert.Equal(resource.Kind != GpuResourceKind.Semaphore,
                    RHIResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(resource));
            }
            Assert.Equal(previousVao.VertexArrayId, GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(previousPipeline.PipelineId, GL.GetInteger(GetPName.ProgramPipelineBinding));
            GL.GetInteger((GetIndexedPName)GetPName.SamplerBinding, 0, out int selectedSampler);
            Assert.Equal(previousSampler.SamplerId, selectedSampler);
            Assert.True(GL.IsVertexArray(vao.VertexArrayId));
            Assert.True(GL.IsProgramPipeline(pipeline.PipelineId));
            Assert.True(GL.IsSampler(sampler.SamplerId));
            // Core named creation realizes query targets and capture objects before publication.
            Assert.True(GL.IsQuery(query.QueryId));
            Assert.True(GL.IsTransformFeedback(feedback.TransformFeedbackId));
            RHICommandContext.Current.BeginQuery(query);
            RHICommandContext.Current.EndQuery(query);
            Assert.True(GL.IsQuery(query.QueryId));
            Assert.True(GL.IsTransformFeedback(feedback.TransformFeedbackId));
            Assert.True(GL.IsSync(fence.ResourceId));
            Assert.Contains(RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(1)), new[] { CompletionStatus.Ready, CompletionStatus.Ready });
            Assert.True(fence.TryConsumeIfSignaled());
            Assert.Equal((nint)0, fence.ResourceId);
            sampler.SetFilter(SamplerFilter.Nearest, SamplerFilter.Nearest);
            GL.GetSamplerParameter(sampler.SamplerId, SamplerParameterName.TextureMinFilter, out int filter);
            Assert.Equal((int)TextureMinFilter.Nearest, filter);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
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
            var unsupported = new RHICommandContext(new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(OpenGlStateCache.Current));
            Assert.Throws<NotSupportedException>(() => unsupported.CreateSemaphore(new()).RequireResource());
            Assert.Throws<NotSupportedException>(() => unsupported.CreateMemoryObject(new()).RequireResource());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            AccessTools.Field(typeof(GpuSupport), "graphics").SetValue(null, original);
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Rejecting identity delivery retires every supported family through the common cleanup path.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FailedIdentityHandoffClearsEveryFamily(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current;
        using var deletion = new DeletionObservation();
        try
        {
            {
                using var recipient = new FailedRecipient<GpuVertexArrayMetadata>(GpuResourceKind.VertexArray);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateVertexArray(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteVertexArray", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuProgramPipelineMetadata>(GpuResourceKind.ProgramPipeline);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateProgramPipeline(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteProgramPipeline", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuSamplerMetadata>(GpuResourceKind.Sampler);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateSampler(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteSampler", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuQueryMetadata>(GpuResourceKind.Query);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateQuery(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteQuery", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuSyncMetadata>(GpuResourceKind.Sync);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateSync(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteSync", recipient.Attempted), DeletionObservation.Deleted);
            }
            
            {
                using var recipient = new FailedRecipient<GpuTransformFeedbackMetadata>(GpuResourceKind.TransformFeedback);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateTransformFeedback(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteTransformFeedback", recipient.Attempted), DeletionObservation.Deleted);
            }
            if (RHICommandContext.Current.Capabilities.ExternalSemaphores)
            {
                using var recipient = new FailedRecipient<GpuSemaphoreMetadata>(GpuResourceKind.Semaphore);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateSemaphore(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteSemaphore", recipient.Attempted), DeletionObservation.Deleted);
            }
            if (RHICommandContext.Current.Capabilities.ExternalMemory)
            {
                using var recipient = new FailedRecipient<GpuMemoryObjectMetadata>(GpuResourceKind.MemoryObject);
                GpuResourceHandle handle = default;
                var error = Assert.Throws<InvalidOperationException>(() => RHIResourceManager.CreateMemoryObject(default, recipient, out handle));
                Assert.Equal("Injected identity failure.", error.Message);
                Assert.NotEqual((nint)0, recipient.Attempted);
                Assert.Equal((nint)0, recipient.ResourceId);
                Assert.Equal(default, handle);
                Assert.Null(RHIResourceManagerSystem.CurrentManager?.GetResourceRegistration(recipient));
                RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
                Assert.Contains(("DeleteMemoryObject", recipient.Attempted), DeletionObservation.Deleted);
            }
            RHIResourceManagerSystem.CurrentManager?.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion

    #region Private
    /// <summary>Records actual deletion dispatch, including generated names that IsObject cannot distinguish from deleted names.</summary>
    private sealed unsafe class DeletionObservation : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        internal static readonly HashSet<(string, nint)> Deleted = new();
        #region Public API
        /// <summary>Observes array and native-width deletion entrypoints in both binding tables.</summary>
        public DeletionObservation()
        {
            Deleted.Clear();
            foreach (var (symbol, label) in new[] {
                ("glDeleteVertexArrays", "DeleteVertexArray"), ("glDeleteProgramPipelines", "DeleteProgramPipeline"),
                ("glDeleteSamplers", "DeleteSampler"), ("glDeleteQueries", "DeleteQuery"),
                ("glDeleteTransformFeedbacks", "DeleteTransformFeedback"),
                ("glDeleteSemaphoresEXT", "DeleteSemaphore"), ("glDeleteMemoryObjectsEXT", "DeleteMemoryObject") })
            {
                nint original = native.Original(symbol);
                if (original == 0) continue;
                native.Add(symbol, new DeleteMany((count, ids) =>
                {
                    native.Observe(() => Record(label, count, ids));
                    ((delegate* unmanaged[Stdcall]<int, uint*, void>)original)(count, ids);
                }));
            }
            nint sync = native.Original("glDeleteSync");
            native.Add("glDeleteSync", new DeleteSync(id =>
            {
                native.Observe(() => Deleted.Add(("DeleteSync", id)));
                ((delegate* unmanaged[Stdcall]<nint, void>)sync)(id);
            }));
            native.Install();
        }
        /// <summary>Restores ordinary loading after the creation failure assertions.</summary>
        public void Dispose() => native.Dispose();
        #endregion
        #region Private
        /// <summary>Copies each deleted name before the native caller releases its argument storage.</summary>
        private static void Record(string name, int count, uint* ids)
        {
            for (int i = 0; i < count; i++) Deleted.Add((name, unchecked((int)ids[i])));
        }
        /// <summary>Matches native object-array deletion.</summary>
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate void DeleteMany(int count, uint* ids);
        /// <summary>Matches full-width native synchronization deletion.</summary>
        [System.Runtime.InteropServices.UnmanagedFunctionPointer(System.Runtime.InteropServices.CallingConvention.StdCall)]
        private delegate void DeleteSync(nint id);
        #endregion
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
            foreach (var method in new[] { typeof(GL), typeof(OpenTK.Graphics.OpenGL4.GL) }.SelectMany(type => type.GetMethods()).Where(method => names.Contains(method.Name) && !method.ContainsGenericParameters))
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


