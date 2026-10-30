using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native operation completion and originating-thread retirement delivery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ResourceChangeNativeTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A throwing consumer cannot turn committed storage into an uncommitted failed allocation.</summary>
    [Fact]
    public void CallbackFailurePreservesSuccessfulStorageAndReleasesTheOperation()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
                { Role = BufferRole.Vertex, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            using var alias = new Alias(owner.BufferId) { Callback = _ => throw new InvalidOperationException("consumer") };
            manager.AssociateResource(entry, alias);
            ulong storage = entry.StorageRevision;
            Assert.Throws<AggregateException>(() => owner.Allocate(64));
            Assert.True(entry.IsUsable); Assert.True(owner.IsValid);
            Assert.Equal(64, owner.SizeBytes);
            Assert.Equal(storage + 1, entry.StorageRevision);
            Assert.False(entry.OperationInProgress);
            Assert.Single(alias.Changes);
            alias.Callback = null;
            owner.Allocate(96);
            Assert.True(entry.IsUsable); Assert.Equal(96, owner.SizeBytes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Worker disposal closes usability immediately but invokes consumers only on the origin thread, once.</summary>
    [Fact]
    public void WorkerRetirementUsesTheExistingLifecycleHandoff()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var manager = RHIResourceManagerSystem.CurrentManager!;
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription
                { Role = BufferRole.Vertex, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            using var alias = new Alias(owner.BufferId);
            manager.AssociateResource(entry, alias);
            int origin = Environment.CurrentManagedThreadId;
            alias.Callback = _ => Assert.Equal(origin, Environment.CurrentManagedThreadId);
            Exception? failure = null;
            var worker = new Thread(() => { try { owner.Dispose(); } catch (Exception error) { failure = error; } });
            worker.Start(); worker.Join();
            Assert.Null(failure); Assert.False(entry.IsUsable); Assert.Empty(alias.Changes);
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.Single(alias.Changes);
            Assert.True((alias.Changes[0].Kind & ResourceChangeKind.Retired) != 0);
            manager.OnRenderFrame(0, RHIResourceManager.Stage);
            Assert.Single(alias.Changes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }
    /// <summary>Local owner retirement withdraws selected projections without a canonical registry.</summary>
    [Fact]
    public void LocalRetirementPublishesImageUnavailabilityOnce()
    {
        fixture.EnsureContextValid(); fixture.UseContextLocalResources();
        try
        {
            using var depth = RHICommandContext.Current.CreateRenderbuffer(new RHIRenderbufferDescription
                { Format = TextureFormat.DepthComponent24, Width = 2, Height = 2 }).RequireResource();
            using var framebuffer = DynamicGpuFramebuffer.CreateDepthOnly(depth)!;
            int changes = 0;
            framebuffer.AttachmentsChanged += () => changes++;
            depth.Dispose();
            Assert.Equal(1, changes);
            Assert.False(framebuffer.Resize(4, 4));
            Assert.Equal(1, changes);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }

    /// <summary>Controlled assignment keeps its original exclusion through consumer callbacks.</summary>
    [Fact]
    public void AssignmentCallbacksCannotEnterAnotherManagedAssignment()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int shader = GL.CreateShader(ShaderType.ComputeShader), program = GL.CreateProgram();
        try
        {
            GL.ShaderSource(shader, "#version 450\nlayout(local_size_x=1) in; uniform sampler2D source; layout(std430,binding=0) buffer Output { vec4 value; }; void main(){value=texelFetch(source,ivec2(0),0);}");
            GL.CompileShader(shader); GL.AttachShader(program, shader); GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked); Assert.Equal(1, linked);
            int location = GL.GetUniformLocation(program, "source"); Assert.True(location >= 0);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuProgramMetadata>>(manager.FindResource(GpuResourceKind.Program, program));
            using var alias = new ProgramAlias(program);
            manager.AssociateResource(entry, alias);
            alias.Callback = () => Assert.Throws<InvalidOperationException>(() =>
                VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLProgramAssignments.SetUnit(program, location, 4));
            VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLProgramAssignments.SetUnit(program, location, 3);
            Assert.Equal(1, alias.Deliveries);
            Assert.Contains(entry.Metadata.TextureUnits, unit => unit.Location == location && unit.Unit == 3);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteProgram(program); GL.DeleteShader(shader); fixture.CleanupGpuResources(); }
    }

    /// <summary>Local executable replacement rejects a fixed shader even when structural interface facts match.</summary>
    [Fact]
    public void LocalEqualInterfaceReplacementInvalidatesFixedShader()
    {
        fixture.EnsureContextValid(); fixture.UseContextLocalResources();
        try
        {
            var source = RHIShaderInputs.ComputeDescription();
            using var shader = RHICommandContext.Current.CreateShader(new(source.Kind, source.Stages, source.Bindings,
                retrievableBinary: true)).RequireResource();
            var binary = RHICommandContext.Current.ExportShaderBinary(shader, 16 * 1024 * 1024);
            Assert.NotNull(binary);
            var previous = shader.Program.MetadataSnapshot;
            var backend = new VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLBackend(
                VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGlStateCache.Current);
            backend.SubmitProgramExecutable(shader.Program, binary);
            Assert.True(backend.CompleteProgramLink(shader.Program, out string diagnostics, [ShaderStage.Compute]), diagnostics);
            Assert.Equal(previous, shader.Program.MetadataSnapshot);
            Assert.Throws<InvalidOperationException>(() => shader.RequireUsable());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); }
    }
    #endregion

    /// <summary>Observes the canonical executable without taking native ownership.</summary>
    private sealed class ProgramAlias(int id) : GpuResource<GpuProgramMetadata>
    {
        internal Action? Callback;
        internal int Deliveries;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Program;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
        /// <inheritdoc />
        internal override void OnResourceChanged(in RHIResourceChange change)
        {
            Deliveries++;
            if (Deliveries == 1) Callback?.Invoke();
        }
    }

    /// <summary>Observes one original buffer without taking ownership of its native storage.</summary>
    private sealed class Alias(int id) : GpuResource<GpuBufferMetadata>
    {
        internal readonly List<RHIResourceChange> Changes = new();
        internal Action<RHIResourceChange>? Callback;
        /// <inheritdoc />
        public override nint ResourceId { get; protected set; } = id;
        /// <inheritdoc />
        protected override GpuResourceKind ResourceKind => GpuResourceKind.Buffer;
        /// <inheritdoc />
        protected override bool OwnsResource => false;
        /// <inheritdoc />
        public override void SetDebugName(string? name) { }
        /// <inheritdoc />
        internal override void OnResourceChanged(in RHIResourceChange change)
        {
            Changes.Add(change); Callback?.Invoke(change);
        }
    }
}
