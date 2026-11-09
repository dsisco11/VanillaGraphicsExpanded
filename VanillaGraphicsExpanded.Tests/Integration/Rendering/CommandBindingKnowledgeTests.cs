using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies established binding facts independently of canonical resource revisions.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class CommandBindingKnowledgeTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Selector execution
    /// <summary>Completion replaces a selector value rediscovered reentrantly with the certified final native selection.</summary>
    [Fact]
    public unsafe void InterceptedSelectionReplacesReentrantIntermediateKnowledge()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();

        try
        {
            using var before = NativeResourceSetup.CreateVertexArray(); using var after = NativeResourceSetup.CreateVertexArray();
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, before.VertexArrayId);
            int? intermediateVao = null;
            using var native = new NativeDriverCapture();
            nint original = native.Original("glBindVertexArray");
            native.Add("glBindVertexArray", new BindArray(array =>
            {
                native.Observe(() => intermediateVao = OpenGlStateCache.Current.GetCurrentVao());
                ((delegate* unmanaged[Stdcall]<uint, void>)original)(array);
            }));
            native.Install();
            // Act
            SelectExternal(after.VertexArrayId, 0, 0);

            // Assert
            native.RequireSuccessfulObservation();
            Assert.Equal(before.VertexArrayId, intermediateVao);
            Assert.True(OpenGlStateCache.Current.TryGetCachedCurrentVao(out int selected));
            Assert.Equal(after.VertexArrayId, selected);
            Assert.Equal(after.VertexArrayId, OpenGlStateCache.Current.GetCurrentVao());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Mapped signed selectors dispatch every external request and recover actual selection after rejection.</summary>
    [Fact]
    public void InterceptedSelectorsDispatchEqualRequestsAndRecoverRejectedNames()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();

        try
        {
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var pipeline = NativeResourceSetup.CreateProgramPipeline();
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty("ExternalSelector");
            var cache = OpenGlStateCache.Current;
            OpenGLVertexSelection.Bind(cache, vao.VertexArrayId); OpenGLProgramPipelineSelection.Bind(cache, pipeline.PipelineId);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, framebuffer.FboId); OpenGLProgramSelection.Select(cache, 0);
            using var capture = new BindingCallCapture();
            // Act
            SelectExternal(vao.VertexArrayId, pipeline.PipelineId, framebuffer.FboId);
            SelectExternal(vao.VertexArrayId, pipeline.PipelineId, framebuffer.FboId);
            // Assert
            Assert.Equal(2, capture.Calls(nameof(GL.BindVertexArray)));
            Assert.Equal(2, capture.Calls(nameof(GL.BindProgramPipeline)));
            Assert.Equal(2, capture.Calls(nameof(GL.BindFramebuffer)));
            Assert.Equal(2, capture.Calls(nameof(GL.UseProgram)));
            Assert.Equal(vao.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(pipeline.PipelineId, cache.GetCurrentProgramPipeline());
            Assert.Equal(framebuffer.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(0, cache.GetCurrentProgram());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            // Act: rejected native names must leave the observed selections at their prior valid values.
            SelectExternal(int.MaxValue, int.MaxValue, framebuffer.FboId);

            // Assert
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(vao.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(pipeline.PipelineId, cache.GetCurrentProgramPipeline());
            Assert.Equal(0, cache.GetCurrentProgram());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Managed selector changes retain their submitted identities and equal requests issue no calls.</summary>
    [Fact]
    public void ManagedSelectorsRetainKnowledgeWithoutReadback()
    {
        // Arrange
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var pipeline = NativeResourceSetup.CreateProgramPipeline();
            using var module = BuiltShaderFixture.LoadFixture("tests/prepared_binding.csh", ShaderType.ComputeShader);
            using var program = NativeResourceSetup.CreateProgram();
            int executable = program.ProgramId;
            GL.AttachShader(executable, module.ShaderId); GL.LinkProgram(executable);
            GL.GetProgram(executable, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            var cache = OpenGlStateCache.Current;
            OpenGLVertexSelection.Bind(cache, 0); OpenGLProgramPipelineSelection.Bind(cache, 0); OpenGLProgramSelection.Select(cache, 0);
            using var capture = new BindingCallCapture();
            // Calibrate the observed query overload before accepting zero-query evidence.
            GL.GetInteger(GetPName.VertexArrayBinding);
            Assert.Equal(1, capture.Calls(nameof(GL.GetInteger)));
            capture.Reset();

            // Act
            OpenGLVertexSelection.Bind(cache, vao.VertexArrayId);
            OpenGLVertexSelection.Bind(cache, vao.VertexArrayId);
            OpenGLProgramPipelineSelection.Bind(cache, pipeline.PipelineId);
            OpenGLProgramPipelineSelection.Bind(cache, pipeline.PipelineId);
            OpenGLProgramSelection.Select(cache, program.ProgramId);
            OpenGLProgramSelection.Select(cache, program.ProgramId);
            // Assert
            Assert.Equal(1, capture.Calls(nameof(GL.BindVertexArray)));
            Assert.Equal(1, capture.Calls(nameof(GL.BindProgramPipeline)));
            Assert.Equal(1, capture.Calls(nameof(GL.UseProgram)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(vao.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(pipeline.PipelineId, cache.GetCurrentProgramPipeline());
            Assert.Equal(program.ProgramId, cache.GetCurrentProgram());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Binding scopes
    /// <summary>Known independent read/draw selections permit query-free combined temporary binding and restoration.</summary>
    [Fact]
    public void FramebufferScopeUsesEstablishedSplitBindings()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var read = DynamicGpuFramebuffer.CreateEmpty("Read");
            using var draw = DynamicGpuFramebuffer.CreateEmpty("Draw");
            using var temporary = DynamicGpuFramebuffer.CreateEmpty("Temporary");
            var cache = OpenGlStateCache.Current;
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.ReadFramebuffer, read.FboId);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.DrawFramebuffer, draw.FboId);
            // Act
            using var capture = new BindingCallCapture();
            using (cache.BindFramebufferScope(FramebufferTarget.Framebuffer, temporary.FboId)) { }

            // Assert
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(3, capture.Calls(nameof(GL.BindFramebuffer)));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A cold read-only scope queries its one selector and leaves unrelated draw knowledge untouched.</summary>
    [Fact]
    public void ReadOnlyFramebufferScopeRecoversOnlyReadSelection()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var read = DynamicGpuFramebuffer.CreateEmpty("ReadCold");
            using var draw = DynamicGpuFramebuffer.CreateEmpty("DrawCold");
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw.FboId);
            var cache = OpenGlStateCache.Current;
            cache.Invalidate(EPipelineState.Bindings);
            // Act
            using var capture = new BindingCallCapture();
            using (cache.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, 0)) { }

            // Assert
            Assert.Equal(1, capture.Calls(nameof(GL.GetInteger)));
            Assert.False(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out _));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>An EBO scope restores its captured VAO without overwriting a subsequent VAO's element selection.</summary>
    [Fact]
    public void ElementBufferScopeRestoresOriginalOwnerAfterVaoSelectionChanges()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var original = NativeResourceSetup.CreateVertexArray(); using var later = NativeResourceSetup.CreateVertexArray();
            using var first = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); first.Allocate(128);
            using var temporary = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); temporary.Allocate(128);
            using var second = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); second.Allocate(128);
            var cache = OpenGlStateCache.Current;
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, later.VertexArrayId); OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, second.BufferId);
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, original.VertexArrayId); OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, first.BufferId);
            // Act
            using var capture = new BindingCallCapture();
            using (cache.BindBufferScope(BufferTarget.ElementArrayBuffer, temporary.BufferId)) OpenGLVertexSelection.Bind(OpenGlStateCache.Current, later.VertexArrayId);

            // Assert
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(later.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(second.BufferId, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            // Act: restore the original VAO before checking its retained EBO binding.
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, original.VertexArrayId);

            // Assert
            Assert.Equal(first.BufferId, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(first.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Vertex and buffer knowledge
    /// <summary>VAO notifications preserve independently known EBO identity and equal-bind suppression.</summary>
    [Fact]
    public void VertexRevisionDoesNotDiscardElementBufferKnowledge()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, vao.VertexArrayId);
            var cache = OpenGlStateCache.Current;
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, buffer.BufferId);
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong revision = entry.MetadataRevision;
            ulong configuration = cache.GetVertexConfigurationRevision(vao.VertexArrayId);
            using var capture = new BindingCallCapture();
            // Act
            GL.EnableVertexAttribArray(0);
            OpenGLBufferBindings.BindBuffer(cache, BufferTarget.ElementArrayBuffer, buffer.BufferId);

            // Assert
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.True(cache.GetVertexConfigurationRevision(vao.VertexArrayId) > configuration);
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(0, capture.Calls(nameof(GL.BindBuffer)));
            Assert.Equal(buffer.BufferId, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Direct vertex binding helpers publish authored fields without querying them before dispatch.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectVertexBindingHelpersPublishAuthoredStateWithoutPrequeries(bool registered)
    {
        // Arrange
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(true);
            using var vao = NativeResourceSetup.CreateVertexArray();
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(256);
            using var capture = new BindingCallCapture();
            var before = OpenGlStateCache.Current.GetVertexConfigurationRevision(vao.VertexArrayId);
            // Act
            GL.VertexArrayVertexBuffer(vao.VertexArrayId, 2, buffer.BufferId, (IntPtr)32, 24);
            GL.VertexArrayBindingDivisor(vao.VertexArrayId, 2, 3);
            // Assert
            Assert.NotEqual(before, OpenGlStateCache.Current.GetVertexConfigurationRevision(vao.VertexArrayId));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed64)));
            GL.GetVertexArrayIndexed(vao.VertexArrayId, 2, (VertexArrayIndexedParameter)All.VertexBindingBuffer, out int selected);
            GL.GetVertexArrayIndexed(vao.VertexArrayId, 2, (VertexArrayIndexedParameter)All.VertexBindingStride, out int stride);
            GL.GetVertexArrayIndexed(vao.VertexArrayId, 2, (VertexArrayIndexedParameter)All.VertexBindingDivisor, out int divisor);
            Assert.Equal(buffer.BufferId, selected); Assert.Equal(24, stride); Assert.Equal(3, divisor);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Recovery of an unknown atomic generic selector reads its binding rather than an indexed range start.</summary>
    [Fact]
    public void UnknownAtomicCounterBindingQueriesTheBindingToken()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription { Role = BufferRole.ShaderStorage, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.DynamicDraw, Label = null }).RequireResource(); buffer.Allocate(128);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, buffer.BufferId);
            OpenGlStateCache.Current.Invalidate(EPipelineState.Bindings);
            // Act
            int selected = OpenGlStateCache.Current.GetBoundBuffer(BufferTarget.AtomicCounterBuffer);

            // Assert
            Assert.Equal(buffer.BufferId, selected);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Matches the native array-selection ABI for contained selector observation.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate void BindArray(uint array);

    /// <summary>Executes signed OpenTK selectors through the production native interception boundary.</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SelectExternal(int vao, int pipeline, int readFramebuffer)
    {
        GL.BindVertexArray(vao);
        GL.BindProgramPipeline(pipeline);
        GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, readFramebuffer);
        GL.UseProgram(0);
    }
    #endregion
}
