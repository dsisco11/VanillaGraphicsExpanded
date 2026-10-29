using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using GL4 = OpenTK.Graphics.OpenGL4.GL;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies remaining empty-schema lifetimes and query-free authored geometry through native interception.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuGeometryLifecycleTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Both roots retain original reserved identities and publish kind-specific defaults exactly once.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GeneratedNamesRealizeOnSpecifiedUse(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        int vao = 0, sampler = 0, feedback = 0;
        try
        {
            using var reads = new GeometryDescriptorCapture();
            vao = core ? GL4.GenVertexArray() : GL.GenVertexArray();
            sampler = core ? GL4.GenSampler() : GL.GenSampler();
            feedback = core ? GL4.GenTransformFeedback() : GL.GenTransformFeedback();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var array = manager.FindResource(GpuResourceKind.VertexArray, vao)!;
            var sample = manager.FindResource(GpuResourceKind.Sampler, sampler)!;
            var capture = manager.FindResource(GpuResourceKind.TransformFeedback, feedback)!;
            foreach (var entry in new[] { array, sample, capture })
            { Assert.False(entry.IsRealized); Assert.False(entry.IsUsable); Assert.Equal(0UL, entry.MetadataRevision); }
            if (core)
            {
                GL4.BindVertexArray(vao); Assert.True(GL4.IsSampler(sampler));
                GL4.BindTransformFeedback(OpenTK.Graphics.OpenGL4.TransformFeedbackTarget.TransformFeedback, feedback);
            }
            else
            {
                GL.BindVertexArray(vao); Assert.True(GL.IsSampler(sampler));
                GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback);
            }
            foreach (var entry in new[] { array, sample, capture })
            { Assert.True(entry.IsRealized); Assert.True(entry.IsUsable); Assert.Equal(1UL, entry.MetadataRevision); }
            Assert.Same(array, manager.FindResource(GpuResourceKind.VertexArray, vao));
            Assert.Same(sample, manager.FindResource(GpuResourceKind.Sampler, sampler));
            Assert.Same(capture, manager.FindResource(GpuResourceKind.TransformFeedback, feedback));
            Assert.True(OpenGlStateCache.Current.TryGetFeedbackActivity(out var activity)); Assert.False(activity.Active);
            Assert.Equal(0, reads.Reads); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindVertexArray(0); GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (sampler != 0) GL.DeleteSampler(sampler);
            if (feedback != 0) GL.DeleteTransformFeedback(feedback);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>Core factory outputs publish once and ordinary sampler parameter changes preserve the empty schema.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CreatedNamesAndSamplerMultibindPreserveOriginals(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        int vao = 0, sampler = 0, feedback = 0;
        try
        {
            if (core) { GL4.CreateVertexArrays(1, out vao); GL4.CreateSamplers(1, out sampler); GL4.CreateTransformFeedbacks(1, out feedback); }
            else { GL.CreateVertexArrays(1, out vao); GL.CreateSamplers(1, out sampler); GL.CreateTransformFeedbacks(1, out feedback); }
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entries = new[] { manager.FindResource(GpuResourceKind.VertexArray, vao)!, manager.FindResource(GpuResourceKind.Sampler, sampler)!, manager.FindResource(GpuResourceKind.TransformFeedback, feedback)! };
            foreach (var entry in entries) { Assert.True(entry.IsUsable); Assert.Equal(1UL, entry.MetadataRevision); }
            if (core) GL4.BindSamplers(0, 1, new[] { sampler }); else GL.BindSamplers(0, 1, new[] { sampler });
            GL.SamplerParameter(sampler, SamplerParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.GetSamplerParameter(sampler, SamplerParameterName.TextureMinFilter, out int filter);
            Assert.Equal((int)TextureMinFilter.Nearest, filter);
            Assert.Equal(1UL, entries[1].MetadataRevision);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindSampler(0, 0);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (sampler != 0) GL.DeleteSampler(sampler);
            if (feedback != 0) GL.DeleteTransformFeedback(feedback);
            fixture.CleanupGpuResources();
        }
    }

    /// <summary>Rejected vertex formats preserve receipts; accepted updates expire them without rediscovering layout.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void VertexRevisionsClassifyBeforeInterruption(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var array = NativeResourceSetup.CreateVertexArray();
            using var elements = RHICommandContext.Current.CreateIndexBuffer(new RHIBufferDescription
                { Role = BufferRole.Index, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = manager.GetResourceRegistration(array)!;
            var layout = new RHIVertexLayout([]);
            var indices = new RHIIndexStream(new(elements, 0, 16));
            using var reads = new GeometryDescriptorCapture();
            var receipt = RHIResourceManager.AcquireEngineGeometryConfiguration(array, layout, [], indices);
            ulong revision = entry.MetadataRevision;
            if (core) GL4.VertexArrayAttribFormat(array.VertexArrayId, 0, 5, OpenTK.Graphics.OpenGL4.VertexAttribType.Float, false, 0);
            else GL.VertexArrayAttribFormat(array.VertexArrayId, 0, 5, VertexAttribType.Float, false, 0);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.True(RHIResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            if (core) GL4.VertexArrayAttribFormat(array.VertexArrayId, 0, 3, OpenTK.Graphics.OpenGL4.VertexAttribType.Float, false, 0);
            else GL.VertexArrayAttribFormat(array.VertexArrayId, 0, 3, VertexAttribType.Float, false, 0);
            Assert.Equal(revision + 1, entry.MetadataRevision);
            Assert.False(RHIResourceManager.IsEngineGeometryConfigurationCurrent(receipt));
            GL.VertexArrayElementBuffer(array.VertexArrayId, elements.BufferId);
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, array.VertexArrayId);
            Assert.Equal(elements.BufferId, OpenGlStateCache.Current.GetBoundBuffer(BufferTarget.ElementArrayBuffer));
            Assert.Equal(0, reads.Reads); Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindVertexArray(0); fixture.CleanupGpuResources(); }
    }

    /// <summary>Named output changes revise the correct feedback identity and rejected ranges leave it unchanged.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void FeedbackConfigurationMaintainsRevisions(bool core)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        int feedback = 0;
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
                { Role = BufferRole.ShaderStorage, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            if (core) GL4.CreateTransformFeedbacks(1, out feedback); else GL.CreateTransformFeedbacks(1, out feedback);
            var entry = RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.TransformFeedback, feedback)!;
            if (core) GL4.TransformFeedbackBufferRange(feedback, 0, buffer.BufferId, 0, 16);
            else GL.TransformFeedbackBufferRange(feedback, 0, buffer.BufferId, 0, 16);
            Assert.Equal(2UL, entry.MetadataRevision);
            GL.TransformFeedbackBufferRange(feedback, 0, buffer.BufferId, 1, 16);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError()); Assert.Equal(2UL, entry.MetadataRevision);
            GL.TransformFeedbackBufferRange(feedback, 0, buffer.BufferId, 0, 32);
            Assert.Equal(ErrorCode.NoError, GL.GetError()); Assert.Equal(3UL, entry.MetadataRevision);
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback);
            GL.BindBufferBase(BufferRangeTarget.TransformFeedbackBuffer, 0, buffer.BufferId);
            Assert.Equal(4UL, entry.MetadataRevision); Assert.True(entry.IsUsable);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0);
            if (feedback != 0) GL.DeleteTransformFeedback(feedback);
            fixture.CleanupGpuResources();
        }
    }
    /// <summary>One multi-bind publishes one revision, and named default-object updates refresh executed slots.</summary>
    [Fact]
    public void FeedbackBatchAndDefaultObjectRetainExecutedBindings()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var first = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
                { Role = BufferRole.ShaderStorage, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            using var second = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
                { Role = BufferRole.ShaderStorage, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            using var feedback = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(feedback)!;
            var cache = OpenGlStateCache.Current;
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback.TransformFeedbackId);
            int genericBefore = GL.GetInteger(GetPName.TransformFeedbackBufferBinding);
            GL.BindBuffersBase(BufferRangeTarget.TransformFeedbackBuffer, 0, 2, new[] { first.BufferId, second.BufferId });
            Assert.Equal(genericBefore, GL.GetInteger(GetPName.TransformFeedbackBufferBinding));
            Assert.Equal(2UL, entry.MetadataRevision);
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback.TransformFeedbackId);
            Assert.Equal(2UL, entry.MetadataRevision);
            GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, first.BufferId);
            Assert.False(cache.NeedsIndexedBufferBinding(BufferRangeTarget.TransformFeedbackBuffer, 0, first.BufferId, 0, 0, false));
            GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, second.BufferId);
            Assert.False(cache.NeedsIndexedBufferBinding(BufferRangeTarget.TransformFeedbackBuffer, 1, second.BufferId, 0, 0, false));
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0);
            _ = OpenGLFeedbackBindings.GetExternalActivity(cache);
            GL.TransformFeedbackBufferBase(0, 0, second.BufferId);
            Assert.False(cache.NeedsIndexedBufferBinding(BufferRangeTarget.TransformFeedbackBuffer, 0, second.BufferId, 0, 0, false));
            Assert.Null(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.TransformFeedback, 0));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0); fixture.CleanupGpuResources(); }
    }

    /// <summary>Named unbind classification preserves the actual slot for zero-size or misaligned native rejection.</summary>
    [Theory]
    [InlineData(0, 0)] [InlineData(1, 4)] [InlineData(0, 3)]
    public void NamedFeedbackUnbindRetainsNativeOutcome(int offset, int size)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateShaderStorageBuffer(new RHIBufferDescription
                { Role = BufferRole.ShaderStorage, Capacity = 16, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw }).RequireResource();
            using var feedback = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback.TransformFeedbackId);
            GL.TransformFeedbackBufferBase(feedback.TransformFeedbackId, 0, buffer.BufferId);
            int genericBefore = GL.GetInteger(GetPName.TransformFeedbackBufferBinding);
            GL.TransformFeedbackBufferRange(feedback.TransformFeedbackId, 0, 0, offset, size);
            var error = GL.GetError();
            Assert.Equal(genericBefore, GL.GetInteger(GetPName.TransformFeedbackBufferBinding));
            GL.GetInteger(GetIndexedPName.TransformFeedbackBufferBinding, 0, out int actual);
            bool rejected = offset % 4 != 0 || size % 4 != 0;
            Assert.Equal(rejected ? ErrorCode.InvalidValue : ErrorCode.NoError, error);
            Assert.Equal(rejected ? buffer.BufferId : 0, actual);
            // The managed suppression key includes a separate generic-selector side effect.
            // Align only that selector so this assertion isolates the indexed native outcome.
            GL.BindBuffer(BufferTarget.TransformFeedbackBuffer, actual);
            Assert.False(OpenGlStateCache.Current.NeedsIndexedBufferBinding(BufferRangeTarget.TransformFeedbackBuffer,
                0, actual, 0, 0, false));
        }
        finally { GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0); fixture.CleanupGpuResources(); }
    }

    /// <summary>A known missing core array buffer rejects nonzero pointer setup before certificate interruption.</summary>
    [Theory]
    [InlineData(0)] [InlineData(1)] [InlineData(2)]
    public void MissingArrayBufferRejectsPointerWithoutRevision(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var array = NativeResourceSetup.CreateVertexArray();
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, array.VertexArrayId);
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ArrayBuffer, 0);
            var entry = RHIResourceManagerSystem.CurrentManager!.GetResourceRegistration(array)!;
            ulong revision = entry.MetadataRevision;
            if (variant == 0) GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 12, (nint)4);
            else if (variant == 1) GL.VertexAttribIPointer(0, 3, VertexAttribIntegerType.Int, 12, (nint)4);
            else GL.VertexAttribLPointer(0, 3, VertexAttribDoubleType.Double, 24, (nint)8);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError()); Assert.Equal(revision, entry.MetadataRevision);
            Assert.True(entry.IsUsable);
        }
        finally { GL.BindVertexArray(0); fixture.CleanupGpuResources(); }
    }

    /// <summary>Capability-independent creation, first use and retirement do not disappear before admission facts are cached.</summary>
    [Fact]
    public void EmptyLifetimesDoNotDependOnCapabilitySnapshot()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        _ = RHICommandContext.Current.Capabilities;
        var field = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "cachedRegistration");
        object? cached = field.GetValue(null);
        int vao = 0, sampler = 0, feedback = 0;
        try
        {
            field.SetValue(null, ((nint)0, 0L));
            vao = GL.GenVertexArray(); GL.BindVertexArray(vao);
            sampler = GL.GenSampler(); Assert.True(GL.IsSampler(sampler));
            GL.CreateTransformFeedbacks(1, out feedback);
            GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, feedback);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var array = manager.FindResource(GpuResourceKind.VertexArray, vao)!;
            var sample = manager.FindResource(GpuResourceKind.Sampler, sampler)!;
            var capture = manager.FindResource(GpuResourceKind.TransformFeedback, feedback)!;
            Assert.True(array.IsUsable); Assert.True(sample.IsUsable); Assert.True(capture.IsUsable);
            Assert.False(GpuSupport.HasCachedCapabilitiesForCurrentContext);
            GL.DeleteVertexArray(vao); vao = 0;
            GL.DeleteSampler(sampler); sampler = 0;
            GL.DeleteTransformFeedback(feedback); feedback = 0;
            foreach (var entry in new[] { array, sample, capture }) Assert.Equal(GpuResourceRegistrationState.Deleted, entry.State);
            Assert.False(GpuSupport.HasCachedCapabilitiesForCurrentContext);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            field.SetValue(null, cached);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (sampler != 0) GL.DeleteSampler(sampler);
            if (feedback != 0) GL.DeleteTransformFeedback(feedback);
            GL.BindVertexArray(0); GL.BindTransformFeedback(TransformFeedbackTarget.TransformFeedback, 0);
            fixture.CleanupGpuResources();
        }
    }
    #endregion
}
