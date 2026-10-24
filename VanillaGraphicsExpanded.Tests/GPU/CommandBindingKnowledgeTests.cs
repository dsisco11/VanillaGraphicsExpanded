using OpenTK.Graphics.OpenGL;
using System.Runtime.CompilerServices;
using HarmonyLib;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies established binding facts independently of canonical resource revisions.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class CommandBindingKnowledgeTests(HeadlessGLFixture fixture)
{
    [ThreadStatic] private static int? intermediateVao;

    #region Public API
    #region Selector execution
    /// <summary>Completion withdraws a selector value rediscovered reentrantly before native selection takes effect.</summary>
    [Fact]
    public void InterceptedSelectionClearsReentrantIntermediateKnowledge()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.CommandBindingReentrantSelector");
        harmony.Patch(AccessTools.Method(typeof(CommandBindingKnowledgeTests), nameof(SelectExternal)),
            transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        try
        {
            using var before = GpuVao.Create(); using var after = GpuVao.Create();
            before.Bind();
            harmony.Patch(((Action<int>)GL.BindVertexArray).Method,
                prefix: new HarmonyMethod(typeof(CommandBindingKnowledgeTests), nameof(ReadIntermediateVao)));
            intermediateVao = null;
            SelectExternal(after.VertexArrayId, 0, 0);
            Assert.Equal(before.VertexArrayId, intermediateVao);
            Assert.False(StateCache.Current.TryGetCachedCurrentVao(out _));
            Assert.Equal(after.VertexArrayId, StateCache.Current.GetCurrentVao());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); intermediateVao = null; fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Mapped signed selectors dispatch every external request and recover actual selection after rejection.</summary>
    [Fact]
    public void InterceptedSelectorsDispatchEqualRequestsAndRecoverRejectedNames()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.CommandBindingExternalSelectors");
        harmony.Patch(AccessTools.Method(typeof(CommandBindingKnowledgeTests), nameof(SelectExternal)),
            transpiler: new HarmonyMethod(typeof(EngineStateSwitchingHook), nameof(EngineStateSwitchingHook.Transpiler)));
        try
        {
            using var vao = GpuVao.Create();
            using var pipeline = GpuProgramPipeline.Create();
            using var framebuffer = DynamicGpuFramebuffer.CreateEmpty("ExternalSelector");
            var cache = StateCache.Current;
            cache.BindVertexArray(vao.VertexArrayId); cache.BindProgramPipeline(pipeline.PipelineId);
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer, framebuffer.FboId); cache.UseProgram(0);
            using var capture = new BindingCallCapture();
            SelectExternal(vao.VertexArrayId, pipeline.PipelineId, framebuffer.FboId);
            SelectExternal(vao.VertexArrayId, pipeline.PipelineId, framebuffer.FboId);
            Assert.Equal(2, capture.Calls(nameof(GL.BindVertexArray)));
            Assert.Equal(2, capture.Calls(nameof(GL.BindProgramPipeline)));
            Assert.Equal(2, capture.Calls(nameof(GL.BindFramebuffer)));
            Assert.Equal(2, capture.Calls(nameof(GL.UseProgram)));
            Assert.Equal(vao.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(pipeline.PipelineId, cache.GetCurrentProgramPipeline());
            Assert.Equal(framebuffer.FboId, cache.GetCurrentFramebuffer(FramebufferTarget.ReadFramebuffer));
            Assert.Equal(0, cache.GetCurrentProgram());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            SelectExternal(int.MaxValue, int.MaxValue, framebuffer.FboId);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            Assert.Equal(vao.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(pipeline.PipelineId, cache.GetCurrentProgramPipeline());
            Assert.Equal(0, cache.GetCurrentProgram());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Managed selector changes retain their submitted identities and equal requests issue no calls.</summary>
    [Fact]
    public void ManagedSelectorsRetainKnowledgeWithoutReadback()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao = GpuVao.Create();
            using var pipeline = GpuProgramPipeline.Create();
            int module = BuiltShaderFixture.LoadFixture("tests/prepared_binding.csh", ShaderType.ComputeShader);
            int executable = GL.CreateProgram();
            GL.AttachShader(executable, module); GL.LinkProgram(executable);
            GL.GetProgram(executable, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            GL.DetachShader(executable, module); GL.DeleteShader(module);
            using var program = GpuProgramObject.Adopt(executable);
            var cache = StateCache.Current;
            cache.BindVertexArray(0); cache.BindProgramPipeline(0); cache.UseProgram(0);
            using var capture = new BindingCallCapture();
            // Calibrate the observed query overload before accepting zero-query evidence.
            GL.GetInteger(GetPName.VertexArrayBinding);
            Assert.Equal(1, capture.Calls(nameof(GL.GetInteger)));
            capture.Reset();
            cache.BindVertexArray(vao.VertexArrayId);
            cache.BindVertexArray(vao.VertexArrayId);
            cache.BindProgramPipeline(pipeline.PipelineId);
            cache.BindProgramPipeline(pipeline.PipelineId);
            cache.UseProgram(program.ProgramId);
            cache.UseProgram(program.ProgramId);
            Assert.Equal(1, capture.Calls(nameof(GL.BindVertexArray)));
            Assert.Equal(1, capture.Calls(nameof(GL.BindProgramPipeline)));
            Assert.Equal(1, capture.Calls(nameof(GL.UseProgram)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(vao.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(pipeline.PipelineId, cache.GetCurrentProgramPipeline());
            Assert.Equal(program.ProgramId, cache.GetCurrentProgram());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Binding scopes
    /// <summary>Known independent read/draw selections permit query-free combined temporary binding and restoration.</summary>
    [Fact]
    public void FramebufferScopeUsesEstablishedSplitBindings()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var read = DynamicGpuFramebuffer.CreateEmpty("Read");
            using var draw = DynamicGpuFramebuffer.CreateEmpty("Draw");
            using var temporary = DynamicGpuFramebuffer.CreateEmpty("Temporary");
            var cache = StateCache.Current;
            cache.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
            cache.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw.FboId);
            using var capture = new BindingCallCapture();
            using (cache.BindFramebufferScope(FramebufferTarget.Framebuffer, temporary.FboId)) { }
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(3, capture.Calls(nameof(GL.BindFramebuffer)));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>A cold read-only scope queries its one selector and leaves unrelated draw knowledge untouched.</summary>
    [Fact]
    public void ReadOnlyFramebufferScopeRecoversOnlyReadSelection()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var read = DynamicGpuFramebuffer.CreateEmpty("ReadCold");
            using var draw = DynamicGpuFramebuffer.CreateEmpty("DrawCold");
            GL.BindFramebuffer(FramebufferTarget.ReadFramebuffer, read.FboId);
            GL.BindFramebuffer(FramebufferTarget.DrawFramebuffer, draw.FboId);
            var cache = StateCache.Current;
            cache.Invalidate(EPipelineState.Bindings);
            using var capture = new BindingCallCapture();
            using (cache.BindFramebufferScope(FramebufferTarget.ReadFramebuffer, 0)) { }
            Assert.Equal(1, capture.Calls(nameof(GL.GetInteger)));
            Assert.False(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out _));
            Assert.Equal(read.FboId, GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(draw.FboId, GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>An EBO scope restores its captured VAO without overwriting a subsequent VAO's element selection.</summary>
    [Fact]
    public void ElementBufferScopeRestoresOriginalOwnerAfterVaoSelectionChanges()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var original = GpuVao.Create(); using var later = GpuVao.Create();
            using var first = GpuShaderStorageBuffer.Create(); first.Allocate(128);
            using var temporary = GpuShaderStorageBuffer.Create(); temporary.Allocate(128);
            using var second = GpuShaderStorageBuffer.Create(); second.Allocate(128);
            var cache = StateCache.Current;
            later.Bind(); cache.BindBuffer(BufferTarget.ElementArrayBuffer, second.BufferId);
            original.Bind(); cache.BindBuffer(BufferTarget.ElementArrayBuffer, first.BufferId);
            using var capture = new BindingCallCapture();
            using (cache.BindBufferScope(BufferTarget.ElementArrayBuffer, temporary.BufferId)) later.Bind();
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(later.VertexArrayId, cache.GetCurrentVao());
            Assert.Equal(second.BufferId, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            original.Bind();
            Assert.Equal(first.BufferId, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(first.BufferId, GL.GetInteger(GetPName.ElementArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Vertex and buffer knowledge
    /// <summary>VAO notifications preserve independently known EBO identity and equal-bind suppression.</summary>
    [Fact]
    public void VertexRevisionDoesNotDiscardElementBufferKnowledge()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var vao = GpuVao.Create();
            using var buffer = GpuShaderStorageBuffer.Create(); buffer.Allocate(128);
            vao.Bind();
            var cache = StateCache.Current;
            cache.BindBuffer(BufferTarget.ElementArrayBuffer, buffer.BufferId);
            var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(vao)!;
            ulong revision = entry.MetadataRevision;
            using var capture = new BindingCallCapture();
            cache.EnableVertexAttribArray(0);
            cache.BindBuffer(BufferTarget.ElementArrayBuffer, buffer.BufferId);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
            Assert.Equal(0, capture.Calls(nameof(GL.BindBuffer)));
            Assert.Equal(buffer.BufferId, cache.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Authored DSA changes retain supplied fields; an independently mutated copy recovers native facts once.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectHelperPreservesAuthoredFactsAndRecoversStaleCopies(bool registered)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        try
        {
            using var capability = new DirectStateAccessCapabilityScope(true);
            using var vao = GpuVao.Create();
            using var buffer = GpuShaderStorageBuffer.Create(); buffer.Allocate(256);
            var helper = vao.GetBinding(2);
            var copied = helper;
            using var capture = new BindingCallCapture();
            helper = helper.BindVertexBuffer(buffer.BufferId, 32, 24).SetDivisor(3);
            Assert.Equal(buffer.BufferId, helper.BufferId);
            Assert.Equal(32, helper.BaseOffsetBytes);
            Assert.Equal(24, helper.StrideBytes);
            Assert.Equal(3, helper.Divisor);
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed64)));
            Assert.Equal(buffer.BufferId, copied.BufferId);
            Assert.Equal(1, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed64)));
            capture.Reset();
            Assert.Equal(3, copied.Divisor);
            Assert.Equal(1, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            capture.Reset();
            Assert.Equal(3, copied.Divisor);
            Assert.Equal(0, capture.Calls(nameof(GL.GetVertexArrayIndexed)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }

    /// <summary>Recovery of an unknown atomic generic selector reads its binding rather than an indexed range start.</summary>
    [Fact]
    public void UnknownAtomicCounterBindingQueriesTheBindingToken()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = GpuShaderStorageBuffer.Create(); buffer.Allocate(128);
            GL.BindBuffer(BufferTarget.AtomicCounterBuffer, buffer.BufferId);
            StateCache.Current.Invalidate(EPipelineState.Bindings);
            Assert.Equal(buffer.BufferId, StateCache.Current.GetBoundBuffer(BufferTarget.AtomicCounterBuffer));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Models a reentrant consumer querying selection immediately before the original native operation.</summary>
    private static void ReadIntermediateVao() => intermediateVao = StateCache.Current.GetCurrentVao();

    /// <summary>Keeps concrete signed native selector overloads visible to the production call-site transpiler.</summary>
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
