using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Gives destructive registered-context lifecycle checks a private native fixture.</summary>
[CollectionDefinition("OperationContext", DisableParallelization = true)]
public sealed class OperationContextCollection : ICollectionFixture<HeadlessGLFixture> { }

/// <summary>Checks operation guards across context registration lifetimes independently of shared rendering fixtures.</summary>
[Collection("OperationContext")]
[Trait("Category", "GPU")]
public sealed class GpuOperationContextFoundationTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Distinct registered context generations isolate guards sharing a numeric ID without native resource work.</summary>
    [Fact]
    public void GuardAdmissionAndDeletionAreContextScoped()
    {
        fixture.EnsureContextValid();
        var origin = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        var temporaryOwner = new object();
        IRHIResourceOperation? next = null;
        try
        {
            // Re-register the same native fixture with a new lifetime; this checks guard identity, not a second native context.
            RenderContextRegistry.Retire(fixture);
            RenderContextRegistry.RegisterCurrent(temporaryOwner, static _ => true);
            next = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Buffer, 123);
            Assert.True(next.Deleted);
            Assert.False(origin.Deleted);
            Assert.False(origin.Cancelled);
        }
        finally
        {
            if (next is not null) next.Dispose();
            origin.Dispose();
            RenderContextRegistry.Retire(temporaryOwner);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Out-of-order cleanup and repeated disposal release only the captured exclusion, without publication or admission.</summary>
    [Fact]
    public void SemanticGuardCleanupKeepsUnrelatedOperationAndRejectsReentry()
    {
        fixture.EnsureContextValid();
        var first = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, 123));
        using var second = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Texture, 123));
        try
        {
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, 123));
            RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, 123, first);
            first.Interrupt();
            Assert.True(first.Cancelled);
            Assert.False(first.Deleted);
            first.Dispose();
            first.Dispose();
            RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, 123);
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Texture, 123));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Texture, 123);
            Assert.True(second.Cancelled);
            Assert.True(second.Deleted);
            using var replacement = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Texture, 123));
            Assert.False(replacement.Cancelled);
            Assert.False(replacement.Deleted);
        }
        finally { first.Dispose(); }
    }

    /// <summary>Physical local namespaces isolate same-name guards and reject completion from another local context.</summary>
    [Fact]
    public unsafe void LocalContextsKeepOriginalOperationIdentity()
    {
        fixture.EnsureContextValid();
        var original = GLFW.GetCurrentContext();
        GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        var firstContext = GLFW.CreateWindow(8, 8, "Operation local A", null, null);
        var secondContext = GLFW.CreateWindow(8, 8, "Operation local B", null, null);
        GpuBufferObject? owner = null;
        RHIResourceManager.MetadataOperation<GpuBufferMetadata>? operation = null;
        IRHIResourceOperation? second = null;
        try
        {
            Assert.True(firstContext != null && secondContext != null);
            GLFW.MakeContextCurrent(firstContext);
            owner = RHICommandContext.Current.CreateBuffer(new RHIBufferDescription
            {
                Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable,
                Capacity = 16, Usage = BufferUsage.StaticDraw
            }).RequireResource();
            operation = new(owner, GpuMetadataChange.Storage);
            int id = (int)owner.ResourceId;
            Assert.Throws<InvalidOperationException>(() => RHICommandContext.RequireResourceOperationAvailable(GpuResourceKind.Buffer, id));

            // The second namespace may use the same numeric name, but cannot cancel or complete A's transaction.
            GLFW.MakeContextCurrent(secondContext);
            _ = RHICommandContext.Current;
            second = RHICommandContext.EnterResourceOperation(new(GpuResourceKind.Buffer, id));
            RHICommandContext.CancelResourceOperation(GpuResourceKind.Buffer, id);
            Assert.True(second.Deleted);
            Assert.Throws<InvalidOperationException>(() => operation.RequireLive());
            second.Dispose();
            second = null;

            GLFW.MakeContextCurrent(firstContext);
            _ = RHICommandContext.Current;
            operation.RequireLive();
            operation.Commit(new GpuBufferMetadata(16, BufferUsage.StaticDraw));
            Assert.Equal((nint)16, owner.SizeBytes);
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            second?.Dispose();
            if (firstContext != null)
            {
                GLFW.MakeContextCurrent(firstContext);
                operation?.Dispose();
                owner?.Dispose();
                GLFW.DestroyWindow(firstContext);
            }
            if (secondContext != null) GLFW.DestroyWindow(secondContext);
            GLFW.MakeContextCurrent(original);
            GpuSupport.Initialize();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Disposing old selector scopes cannot purge or restore names into another admitted physical context.</summary>
    [Fact]
    public unsafe void ForeignSelectorScopesPreserveAdmittedContextKnowledge()
    {
        fixture.EnsureContextValid();
        var original = GLFW.GetCurrentContext();
        GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        var first = GLFW.CreateWindow(8, 8, "Selector scope A", null, null);
        var second = GLFW.CreateWindow(8, 8, "Selector scope B", null, null);
        int[] textures = new int[2], samplers = new int[2], renderbuffers = new int[2];
        int[] vaos = new int[2], pipelines = new int[2], framebuffers = new int[2];
        try
        {
            Assert.True(first != null && second != null);
            GLFW.MakeContextCurrent(first);
            _ = RHICommandContext.Current;
            var cache = OpenGlStateCache.Current;
            GL.CreateTextures(TextureTarget.Texture2D, 1, out textures[0]);
            GL.CreateSamplers(1, out samplers[0]); GL.CreateRenderbuffers(1, out renderbuffers[0]);
            GL.CreateVertexArrays(1, out vaos[0]); GL.CreateProgramPipelines(1, out pipelines[0]);
            GL.CreateFramebuffers(1, out framebuffers[0]);
            var texture = cache.BindTextureScope(TextureTarget.Texture2D, 0, textures[0]);
            var sampler = cache.BindSamplerScope(0, samplers[0]);
            var renderbuffer = cache.BindRenderbufferScope(renderbuffers[0]);
            var vao = cache.BindVertexArrayScope(vaos[0]);
            var pipeline = cache.BindProgramPipelineScope(pipelines[0]);
            var framebuffer = cache.BindFramebufferScope(FramebufferTarget.Framebuffer, framebuffers[0]);

            // Admit B into the same thread-local store, then establish independent selector facts.
            GLFW.MakeContextCurrent(second);
            _ = RHICommandContext.Current;
            Assert.Same(cache, OpenGlStateCache.Current);
            GL.CreateTextures(TextureTarget.Texture2D, 1, out textures[1]);
            GL.CreateSamplers(1, out samplers[1]); GL.CreateRenderbuffers(1, out renderbuffers[1]);
            GL.CreateVertexArrays(1, out vaos[1]); GL.CreateProgramPipelines(1, out pipelines[1]);
            GL.CreateFramebuffers(1, out framebuffers[1]);
            OpenGLImageBindings.BindTexture(cache, TextureTarget.Texture2D, 0, textures[1]);
            OpenGLImageBindings.BindSampler(cache, 0, samplers[1]);
            OpenGLImageBindings.BindRenderbuffer(cache, renderbuffers[1]);
            OpenGLVertexSelection.Bind(cache, vaos[1]);
            OpenGLProgramPipelineSelection.Bind(cache, pipelines[1]);
            OpenGLFramebufferSelection.Bind(cache, FramebufferTarget.Framebuffer, framebuffers[1]);
            using (var capture = new BindingCallCapture())
            {
                framebuffer.Dispose(); pipeline.Dispose(); vao.Dispose();
                renderbuffer.Dispose(); sampler.Dispose(); texture.Dispose();
                Assert.True(cache.TryGetCachedBoundTexture(TextureTarget.Texture2D, 0, out int selectedTexture));
                Assert.Equal(textures[1], selectedTexture);
                Assert.True(cache.TryGetCachedBoundSampler(0, out int selectedSampler));
                Assert.Equal(samplers[1], selectedSampler);
                Assert.Equal(renderbuffers[1], cache.RenderbufferKnowledge);
                Assert.True(cache.TryGetCachedCurrentVao(out int selectedVao)); Assert.Equal(vaos[1], selectedVao);
                Assert.Equal(pipelines[1], cache.GetCurrentProgramPipeline());
                Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.ReadFramebuffer, out int selectedRead));
                Assert.True(cache.TryGetCachedCurrentFramebuffer(FramebufferTarget.DrawFramebuffer, out int selectedDraw));
                Assert.Equal(framebuffers[1], selectedRead); Assert.Equal(framebuffers[1], selectedDraw);
                Assert.Equal(0, capture.Calls(nameof(GL.GetInteger)));
                Assert.Equal(0, capture.Calls(nameof(GL.BindVertexArray)));
                Assert.Equal(0, capture.Calls(nameof(GL.BindProgramPipeline)));
                Assert.Equal(0, capture.Calls(nameof(GL.BindFramebuffer)));
            }
            // Independent native observations catch restoration through an uncaptured native overload.
            Assert.Equal(textures[1], GL.GetInteger(GetPName.TextureBinding2D));
            GL.GetInteger((GetIndexedPName)All.SamplerBinding, 0, out int actualSampler);
            Assert.Equal(samplers[1], actualSampler);
            Assert.Equal(renderbuffers[1], GL.GetInteger(GetPName.RenderbufferBinding));
            Assert.Equal(vaos[1], GL.GetInteger(GetPName.VertexArrayBinding));
            Assert.Equal(pipelines[1], GL.GetInteger(GetPName.ProgramPipelineBinding));
            Assert.Equal(framebuffers[1], GL.GetInteger(GetPName.ReadFramebufferBinding));
            Assert.Equal(framebuffers[1], GL.GetInteger(GetPName.DrawFramebufferBinding));
            Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            // Raw fixture allocations belong to their original windows, independent of manager ownership.
            for (int index = 0; index < 2; index++)
            {
                var window = index == 0 ? first : second;
                if (window == null) continue;
                GLFW.MakeContextCurrent(window);
                if (textures[index] != 0) GL.DeleteTexture(textures[index]);
                if (samplers[index] != 0) GL.DeleteSampler(samplers[index]);
                if (renderbuffers[index] != 0) GL.DeleteRenderbuffer(renderbuffers[index]);
                if (vaos[index] != 0) GL.DeleteVertexArray(vaos[index]);
                if (pipelines[index] != 0) GL.DeleteProgramPipeline(pipelines[index]);
                if (framebuffers[index] != 0) GL.DeleteFramebuffer(framebuffers[index]);
                GLFW.DestroyWindow(window);
            }
            GLFW.MakeContextCurrent(original);
            GpuSupport.Initialize();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    /// <summary>Context retirement prevents publication and does not issue native cleanup in the wrong lifetime.</summary>
    [Fact]
    public void ContextRetirementCancelsOperation()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int id = 0;
        try
        {
            using var owner = RHICommandContext.Current.CreateVertexBuffer(new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = 0, Usage = BufferUsage.StaticDraw, Label = null }).RequireResource(); owner.Allocate(16); id = owner.BufferId;
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.GetResourceRegistration(owner));
            using (var operation = new RHIResourceManager.MetadataOperation<GpuBufferMetadata>(owner, GpuMetadataChange.Storage))
            {
                RenderContextRegistry.Retire(fixture);
                Assert.Throws<InvalidOperationException>(() => operation.Commit(new(64, BufferUsage.StaticDraw)));
            }
            Assert.Equal(GpuResourceRegistrationState.ContextRetired, entry.State);
            Assert.False(entry.MetadataAvailable);
            Assert.True(GL.IsBuffer(id));
        }
        finally
        {
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            if (id != 0 && GL.IsBuffer(id)) RHICommandContext.Current.RetireResource(new(VanillaGraphicsExpanded.Rendering.GpuResourceKind.Buffer, id));
            fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
}
