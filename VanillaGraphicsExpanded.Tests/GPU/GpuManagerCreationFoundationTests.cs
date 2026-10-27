using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using GLFW = OpenTK.Windowing.GraphicsLibraryFramework.GLFW;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies common creation orchestration without migrating resource-family owning APIs.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuManagerCreationFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Successful handoff exposes the committed identity and the same canonical snapshot to registry and recipient.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CreationCommitsIdentityAndSnapshot(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var request = MutableRequest();
            var state = new CreationState();
            var result = GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(
                request, recipient, out var handle, new(state));
            Assert.True(result.HasValue);
            Assert.Equal((nint)state.Id, handle.Id);
            Assert.Equal(handle.Id, recipient.ResourceId);
            Assert.Equal(result.Value, recipient.Snapshot);
            Assert.True(recipient.IsValid);
            Assert.Equal(1, state.Initializations);
            if (registered)
            {
                var manager = GpuResourceManagerSystem.CurrentManager!;
                var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(recipient));
                Assert.Equal(result.Value, entry.Metadata);
                Assert.True(manager.IsResourceMetadataAvailable(recipient));
                Assert.Equal((ulong)1, entry.MetadataRevision);
                Assert.Equal(1, entry.AssociationCount);
            }
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, state.Id);
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int actual);
            Assert.Equal(32, actual);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Nullable failure, initialization exceptions and handoff exceptions retire only the new allocation and clear output.</summary>
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    public void FailedCreationClearsIdentityAndCleansNewObject(bool registered, int failure)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var dependency = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource();
            dependency.Allocate(16);
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var state = new CreationState { Failure = failure };
            if (failure == 2) recipient.InstallationFailure = state.Exception;
            var request = MutableRequest();
            GpuResourceHandle handle = new(GpuResourceKind.Buffer, -1);
            if (failure == 0)
                Assert.Null(GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(
                    request, recipient, out handle, new(state)));
            else
                Assert.Same(state.Exception, Assert.Throws<InvalidOperationException>(() =>
                    GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(
                        request, recipient, out handle, new(state))));
            Assert.Equal(default, handle);
            Assert.Equal((nint)0, recipient.ResourceId);
            Assert.False(recipient.IsValid);
            if (registered)
            {
                var manager = GpuResourceManagerSystem.CurrentManager!;
                manager.OnRenderFrame(0, GpuResourceManager.Stage);
                Assert.Null(manager.FindResource(GpuResourceKind.Buffer, state.Id));
                Assert.Null(manager.GetResourceRegistration(recipient));
            }
            Assert.False(GL.IsBuffer(state.Id));
            Assert.True(dependency.IsValid);
            Assert.True(GL.IsBuffer(dependency.BufferId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Invalid requests and already published recipients fail before another native allocation.</summary>
    [Fact]
    public void InvalidRequestOrRecipientCannotStartCreation()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var state = new CreationState { Failure = -1 };
            var omitted = new GpuBufferInitialization();
            Assert.Throws<ArgumentException>(() =>
                GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(omitted, recipient, out _, new(state)));
            Assert.Equal(0, state.Initializations);
            var request = MutableRequest();
            Assert.NotNull(GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(request, recipient, out _, new(state)));
            Assert.Throws<InvalidOperationException>(() =>
                GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(request, recipient, out _, new(state)));
            Assert.Equal(1, state.Initializations);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>An empty initialization record can successfully publish an empty executable without being mistaken for failure.</summary>
    [Fact]
    public void EmptyProgramCreationReturnsNonNullEmptyFacts()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Declaration<GpuProgramMetadata>(GpuResourceKind.Program);
            var result = GpuResourceManager.CreateResource<GpuProgramInitialization, GpuProgramMetadata, ProgramCreation>(
                default, recipient, out var handle);
            Assert.True(result.HasValue);
            Assert.NotEqual((nint)0, handle.Id);
            Assert.False(result.Value.ExecutableAvailable);
            Assert.Empty(result.Value.Resources);
            Assert.Empty(result.Value.TextureUnits);
            Assert.Equal(result.Value, recipient.Snapshot);
            Assert.True(GL.IsProgram(checked((int)handle.Id)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A metadata schema with no fields still produces a non-null successful creation result.</summary>
    [Fact]
    public void EmptySchemaIsSuccessfulMetadataRatherThanNullableFailure()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Declaration<GpuSamplerMetadata>(GpuResourceKind.Sampler);
            var result = GpuResourceManager.CreateResource<EmptyRequest, GpuSamplerMetadata, SamplerCreation>(default, recipient, out var handle);
            Assert.True(result.HasValue);
            Assert.Equal(default(GpuSamplerMetadata), result.Value);
            Assert.Equal(handle.Id, recipient.ResourceId);
            Assert.True(GL.IsSampler(checked((int)handle.Id)));
            Assert.True(GpuResourceManagerSystem.CurrentManager!.IsResourceMetadataAvailable(recipient));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Retirement during initialization cannot expose a handle or retire its native name in a replacement context.</summary>
    [Fact]
    public void CreationRejectsContextRetirementBeforeHandoff()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var state = new CreationState { AfterInitialize = () => RenderContextRegistry.Retire(fixture) };
        try
        {
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer);
            GpuResourceHandle handle = new(GpuResourceKind.Buffer, -1);
            var request = MutableRequest();
            Assert.Throws<InvalidOperationException>(() =>
                GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(request, recipient, out handle, new(state)));
            Assert.Equal(default, handle);
            Assert.Equal((nint)0, recipient.ResourceId);
            Assert.False(recipient.IsValid);
            // The fixture retains its actual native context; registry retirement must not dispatch cleanup there.
            Assert.True(GL.IsBuffer(state.Id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (GL.IsBuffer(state.Id)) RHICommandContext.Current.OpenGL.DeleteBuffer(state.Id);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Validation cannot replace the caller's already captured request before native initialization consumes it.</summary>
    [Fact]
    public void CreationConsumesOneStableRequestCopy()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var request = MutableRequest();
            var state = new CreationState { DuringValidation = () => request.Capacity = 64 };
            var result = GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(
                in request, recipient, out _, new(state));
            Assert.Equal((nint)64, request.Capacity);
            Assert.Equal((nint)32, result!.Value.Capacity);
            Assert.Equal((nint)32, recipient.Snapshot.Capacity);
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, state.Id);
            GL.GetBufferParameter(BufferTarget.ArrayBuffer, BufferParameterName.BufferSize, out int capacity);
            Assert.Equal(32, capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>The returned result is the reconciled canonical snapshot delivered to the wrapper, including established attachment modes.</summary>
    [Fact]
    public void CreationReturnsCanonicalReconciliationRatherThanBackendCandidate()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var backing = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription { Format = TextureFormat.Rgba8, Width = 4, Height = 4, Samples = 0, Label = null }).RequireResource();
            using var recipient = new Declaration<GpuFramebufferMetadata>(GpuResourceKind.Framebuffer);
            var result = GpuResourceManager.CreateResource<EmptyRequest, GpuFramebufferMetadata, FramebufferCreation>(
                default, recipient, out _, new(backing.RenderbufferId));
            Assert.True(result.HasValue);
            Assert.Equal(GpuFramebufferAttachmentMode.Ordinary, result.Value.Attachments[FramebufferAttachment.ColorAttachment0].Mode);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuFramebufferMetadata>>(manager.GetResourceRegistration(recipient));
            Assert.Equal(entry.Metadata, result.Value);
            Assert.Equal(entry.Metadata, recipient.Snapshot);
            recipient.Dispose();
            Assert.True(GL.IsRenderbuffer(backing.RenderbufferId));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A backend collision cannot transfer or delete an existing owner's registered native object.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void BackendCollisionPreservesExistingOwnerAndEntry(bool nullableFailure)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16);
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer);
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var original = manager.GetResourceRegistration(owner)!;
            var state = new CreationState { CollisionId = owner.BufferId, Failure = nullableFailure ? 0 : -1 };
            var request = MutableRequest();
            GpuResourceHandle handle = new(GpuResourceKind.Buffer, -1);
            if (nullableFailure)
                Assert.Null(GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(request, recipient, out handle, new(state)));
            else
                Assert.Throws<InvalidOperationException>(() =>
                    GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(request, recipient, out handle, new(state)));
            Assert.Equal(default, handle);
            Assert.Equal((nint)0, recipient.ResourceId);
            Assert.Same(original, manager.GetResourceRegistration(owner));
            Assert.Same(original, manager.FindResource(GpuResourceKind.Buffer, owner.BufferId));
            Assert.True(original.MetadataAvailable);
            Assert.True(owner.IsValid);
            Assert.True(GL.IsBuffer(owner.BufferId));
            Assert.Equal(16, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Handoff losing the current context retains queued cleanup identity until the live originating context drains it.</summary>
    [Fact]
    public unsafe void InterruptedHandoffDefersCleanupUntilOriginReturns()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var state = new CreationState();
        try
        {
            using var recipient = new Declaration<GpuBufferMetadata>(GpuResourceKind.Buffer)
                { AfterIdentityInstall = () => GLFW.MakeContextCurrent(null) };
            var request = MutableRequest();
            GpuResourceHandle handle = new(GpuResourceKind.Buffer, -1);
            Assert.Throws<InvalidOperationException>(() =>
                GpuResourceManager.CreateResource<GpuBufferInitialization, GpuBufferMetadata, BufferCreation>(request, recipient, out handle, new(state)));
            Assert.Equal(default, handle);
            Assert.Equal((nint)0, recipient.ResourceId);
            fixture.MakeCurrent();
            var manager = GpuResourceManagerSystem.CurrentManager!;
            var pending = manager.FindResource(GpuResourceKind.Buffer, state.Id)!;
            Assert.NotNull(pending);
            Assert.Equal(GpuResourceRegistrationState.Retiring, pending.State);
            Assert.False(pending.MetadataAvailable);
            Assert.True(GL.IsBuffer(state.Id));
            manager.OnRenderFrame(0, GpuResourceManager.Stage);
            Assert.False(GL.IsBuffer(state.Id));
            Assert.Equal(GpuResourceRegistrationState.Deleted, pending.State);
            Assert.Null(manager.FindResource(GpuResourceKind.Buffer, state.Id));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            fixture.MakeCurrent();
            if (state.Id != 0 && GL.IsBuffer(state.Id)) RHICommandContext.Current.OpenGL.DeleteBuffer(state.Id);
            fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Provides one small mutable allocation with all required intent assigned.</summary>
    private static GpuBufferInitialization MutableRequest() => new()
        { Mode = GpuBufferStorageMode.Mutable, Capacity = 32, Usage = BufferUsageHint.StaticDraw };

    /// <summary>Observes fixture backend calls and carries an exact failure instance across orchestration.</summary>
    private sealed class CreationState
    {
        internal int Id, Initializations, CollisionId, Failure = -1;
        internal Action? AfterInitialize;
        internal Action? DuringValidation;
        internal readonly InvalidOperationException Exception = new("Injected creation failure.");
    }

    /// <summary>Provides an actual native allocation through a value backend without changing existing owning factories.</summary>
    private readonly struct BufferCreation(CreationState state) : IGpuResourceCreation<GpuBufferInitialization, GpuBufferMetadata>
    {
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        public void Validate(in GpuBufferInitialization initialization)
        {
            state.DuringValidation?.Invoke();
            initialization.Validate();
        }
        /// <inheritdoc />
        public GpuBufferMetadata? Initialize(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext execution, in GpuBufferInitialization initialization, out nint id)
        {
            id = state.Id = state.CollisionId != 0 ? state.CollisionId : GL.GenBuffer();
            state.Initializations++;
            if (state.CollisionId != 0)
                return state.Failure == 0 ? null : new(initialization.Capacity, initialization.Usage);
            using var binding = StateCache.Current.BindBufferScope(BufferTarget.ArrayBuffer, state.Id);
            GL.BufferData(BufferTarget.ArrayBuffer, initialization.Capacity, IntPtr.Zero, initialization.Usage);
            state.AfterInitialize?.Invoke();
            if (state.Failure == 1) throw state.Exception;
            return state.Failure == 0 ? null : new(initialization.Capacity, initialization.Usage);
        }
    }

    /// <summary>Publishes the established empty interface of a newly created program without reflection.</summary>
    private readonly struct ProgramCreation : IGpuResourceCreation<GpuProgramInitialization, GpuProgramMetadata>
    {
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Program;
        /// <inheritdoc />
        public void Validate(in GpuProgramInitialization initialization) { }
        /// <inheritdoc />
        public GpuProgramMetadata? Initialize(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext execution, in GpuProgramInitialization initialization, out nint id)
        {
            id = GL.CreateProgram();
            return new(false, ImmutableArray<GpuProgramResourceMetadata>.Empty, ImmutableArray<GpuProgramTextureUnitMetadata>.Empty);
        }
    }

    /// <summary>Supplies an empty fixture request without inventing optional sampler descriptor fields.</summary>
    private readonly record struct EmptyRequest;

    /// <summary>Creates a native object whose canonical schema intentionally contains no facts.</summary>
    private readonly struct SamplerCreation : IGpuResourceCreation<EmptyRequest, GpuSamplerMetadata>
    {
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Sampler;
        /// <inheritdoc />
        public void Validate(in EmptyRequest initialization) { }
        /// <inheritdoc />
        public GpuSamplerMetadata? Initialize(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext execution, in EmptyRequest initialization, out nint id)
        {
            id = GL.GenSampler();
            return default(GpuSamplerMetadata);
        }
    }

    /// <summary>Provides attachment facts whose ordinary renderbuffer mode is reconciled centrally before handoff.</summary>
    private readonly struct FramebufferCreation(int renderbuffer) : IGpuResourceCreation<EmptyRequest, GpuFramebufferMetadata>
    {
        /// <inheritdoc />
        public GpuResourceKind Kind => GpuResourceKind.Framebuffer;
        /// <inheritdoc />
        public void Validate(in EmptyRequest initialization) { }
        /// <inheritdoc />
        public GpuFramebufferMetadata? Initialize(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext execution, in EmptyRequest initialization, out nint id)
        {
            id = GL.GenFramebuffer();
            using var binding = StateCache.Current.BindFramebufferScope(FramebufferTarget.Framebuffer, checked((int)id));
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, RenderbufferTarget.Renderbuffer, renderbuffer);
            var images = ImmutableDictionary<FramebufferAttachment, GpuFramebufferImageMetadata>.Empty.Add(
                FramebufferAttachment.ColorAttachment0, new(GpuResourceKind.Renderbuffer, renderbuffer, 0, 0, 0, false));
            return new(images, 0, 0, 0, 0, false);
        }
    }

    /// <summary>Models a fully initialized wrapper declaration, including a one-shot identity-install failure.</summary>
    private sealed class Declaration<T>(GpuResourceKind kind) : GpuResource<T> where T : struct
    {
        private nint id;
        internal InvalidOperationException? InstallationFailure;
        internal Action? AfterIdentityInstall;
        internal T Snapshot => metadata;
        /// <inheritdoc />
        public override nint ResourceId
        {
            get => id;
            protected set
            {
                id = value;
                if (value != 0 && InstallationFailure is { } failure)
                {
                    InstallationFailure = null;
                    throw failure;
                }
                if (value != 0 && AfterIdentityInstall is { } callback)
                {
                    AfterIdentityInstall = null;
                    callback();
                }
            }
        }
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => kind;
        /// <inheritdoc />
        public override void SetDebugName(string? debugName) { }
    }
    #endregion
}
