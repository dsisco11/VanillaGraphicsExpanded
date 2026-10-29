using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using System.Runtime.InteropServices;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
namespace VanillaGraphicsExpanded.Tests.GPU;
/// <summary>Exercises frozen recording, exclusive lifecycle, ordered failure and independently owned completion on a real context.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHICommandListTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Recording and closing copy CPU input while no native context is current, including retired resources.</summary>
    [Fact]
    public unsafe void RecordingNeedsNoNativeContextOrLiveResource()
    {
        Start(); var context = RHICommandContext.Current;
        using var buffer = Buffer(context, 16);
        GpuBufferRef reference = buffer; buffer.Dispose();
        using var list = context.CreateCommandList();
        var current = GLFW.GetCurrentContext();
        GLFW.MakeContextCurrent(null);
        try { list.UploadBuffer(reference, 0, new byte[16]); list.Close(); }
        finally { GLFW.MakeContextCurrent(current); }
        Assert.Equal(CommandListState.Closed, list.State);
        var result = context.Submit(list);
        Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        Assert.NotNull(result.Error);
    }
    /// <summary>Variable payloads survive caller mutation and reusable pages survive reset without replay authority.</summary>
    [Fact]
    public void FrozenBytesAndOrderedOperationsMatchImmediateResults()
    {
        Start(); var context = RHICommandContext.Current;
        using var source = Buffer(context, 32); using var destination = Buffer(context, 32);
        using var list = context.CreateCommandList();
        byte[] bytes = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
        list.UploadBuffer(source, 0, bytes); Array.Fill(bytes, (byte)99);
        list.CopyBuffer(source, 0, destination, 0, 32); list.ClearBuffer(destination, 8, 8, 77);
        using var readback = list.EnqueueReadback(new(new RHIBufferRange(destination, 0, 32)));
        using var fence = list.InsertFence(); list.Close();
        Assert.Equal(CompletionStatus.Pending, readback.Poll());
        Assert.Throws<InvalidOperationException>(() => readback.MapReadyReadback());
        Assert.Throws<InvalidOperationException>(list.Reset);
        Assert.Throws<InvalidOperationException>(() => list.ClearBuffer(source, 0, 1));
        var result = context.Submit(list); Assert.Null(result.Error); Assert.Equal(CommandListState.Executed, result.State);
        Assert.Throws<InvalidOperationException>(() => context.Submit(list));
        list.Reset(); Assert.Equal(0, list.Count);
        // List reset cannot retire staging or an already-issued completion marker.
        Assert.Equal(CompletionStatus.Ready, readback.Wait(TimeSpan.FromSeconds(5)));
        Assert.Equal(CompletionStatus.Ready, fence.Wait(TimeSpan.FromSeconds(5)));
        using (var mapped = readback.MapReadyReadback())
        {
            var expected = Enumerable.Range(0, 32).Select(x => (byte)(x >= 8 && x < 16 ? 77 : x)).ToArray();
            Assert.Equal(expected, mapped.Bytes.ToArray());
        }
        for (int index = 0; index < 300; index++) list.ClearBuffer(destination, 0, 1, (byte)index);
        list.Close(); Assert.Equal(CommandListState.Executed, context.Submit(list).State);
        using var observed = context.EnqueueReadback(new(new RHIBufferRange(destination, 0, 1)));
        Assert.Equal(CompletionStatus.Ready, observed.Wait(TimeSpan.FromSeconds(5)));
        using var last = observed.MapReadyReadback(); Assert.Equal(unchecked((byte)299), last.Bytes[0]);
    }
    /// <summary>Cancellation leaves native content and CPU dirty state untouched and resolves every unissued request.</summary>
    [Fact]
    public void CancelCreatesNoNativeResultsOrUniformPublication()
    {
        Start(); var context = RHICommandContext.Current;
        using var buffer = Buffer(context, 16); using var uniform = new PackedUniformBuffer(16);
        uniform.SetBytes(Enumerable.Repeat((byte)9, 16).ToArray());
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var list = context.CreateCommandList();
        list.ClearBuffer(buffer, 0, 16, 33); list.BindUniformSnapshot(0, uniform.Capture());
        using var readback = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
        using var fence = list.InsertFence(); var queryResult = list.RequestQueryResult(query);
        list.Close(); list.Cancel();
        Assert.True(uniform.IsDirty); Assert.Equal(0, readback.ByteCount);
        Assert.Equal(CompletionStatus.Cancelled, readback.Poll());
        Assert.Equal(CompletionStatus.Cancelled, fence.Poll()); Assert.Equal(CompletionStatus.Cancelled, queryResult.Poll());
        Assert.Throws<InvalidOperationException>(() => context.Submit(list)); list.Reset(); list.Close();
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
    }
    /// <summary>A failed command stops subsequent writes while earlier readbacks retain independent completion ownership.</summary>
    [Fact]
    public void PartialFailurePreservesFirstErrorAndEnqueuedRequests()
    {
        Start(); var context = RHICommandContext.Current;
        using var buffer = Buffer(context, 16); using var retired = Buffer(context, 16);
        using var list = context.CreateCommandList();
        list.ClearBuffer(buffer, 0, 16, 7);
        using var earlier = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
        list.ClearBuffer(retired, 0, 16, 1); list.ClearBuffer(buffer, 0, 16, 99);
        using var later = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
        using var marker = list.InsertFence(); list.Close(); retired.Dispose();
        var result = context.Submit(list);
        Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(2, result.FailureIndex);
        Assert.NotNull(result.Error); Assert.Same(result.Error, later.Error); Assert.Same(result.Error, marker.Error);
        Assert.Equal(CompletionStatus.Failed, later.Status); Assert.Equal(CompletionStatus.Failed, marker.Status);
        list.Reset();
        Assert.Equal(CompletionStatus.Ready, earlier.Wait(TimeSpan.FromSeconds(5)));
        using var mapped = earlier.MapReadyReadback(); Assert.All(mapped.Bytes.ToArray(), value => Assert.Equal((byte)7, value));
    }
    /// <summary>Timestamp resources preexist recording; result revision and cancellation are resolved only at dispatch.</summary>
    [Fact]
    public void RecordedQueriesUseIssuedGenerationAndBalancedIntervals()
    {
        Start(); var context = RHICommandContext.Current;
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var elapsed = context.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource();
        using var list = context.CreateCommandList();
        list.BeginQuery(elapsed); Assert.Throws<InvalidOperationException>(() => list.Close());
        list.EndQuery(elapsed); list.WriteTimestamp(query); var result = list.RequestQueryResult(query);
        var duration = list.RequestQueryResult(elapsed); list.Close();
        Assert.Equal(CompletionStatus.Pending, result.Poll());
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
        Assert.Equal(CompletionStatus.Ready, result.Wait()); Assert.Equal(CompletionStatus.Ready, duration.Wait());
        Assert.True(result.Result > 0);
        list.Reset(); list.WriteTimestamp(query); list.Close(); Assert.Equal(CommandListState.Executed, context.Submit(list).State);
    }
    /// <summary>Closing balanced passes is mandatory, and a closed list cannot widen an existing boundary before any upload.</summary>
    [Fact]
    public void CompleteFootprintRejectsBeforeFirstCommand()
    {
        Start(); var context = RHICommandContext.Current;
        using var image = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 2, Height = 2,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var buffer = Buffer(context, 16); using var list = context.CreateCommandList();
        list.ClearBuffer(buffer, 0, 16, 77);
        list.BeginRenderPass(new([new RHIColorAttachment(Image: RHIAttachment.FromTexture(image))]));
        Assert.Throws<InvalidOperationException>(() => list.Close()); list.EndRenderPass(); list.Close(true);
        using var boundary = context.EnterBoundary(new("No pass effects", true));
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        Assert.False(context.HasRenderPass);
    }
    /// <summary>Retired origins cancel CPU promises without using the newly registered lifetime of the same physical context.</summary>
    [Fact]
    public void RetiredContextCancelsUnsubmittedWork()
    {
        Start(); var domain = new object(); RenderContextRegistry.RegisterCurrent(domain, static _ => true);
        try
        {
            var context = RHICommandContext.Current; using var list = context.CreateCommandList();
            using var marker = list.InsertFence(); list.Close(); RenderContextRegistry.Retire(domain);
            Assert.Equal(CommandListState.Cancelled, context.Submit(list).State);
            Assert.Equal(CompletionStatus.Cancelled, marker.Poll());
        }
        finally { RenderContextRegistry.RegisterCurrent(fixture, static _ => true); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Uniform bytes execute in the current epoch and an older captured version cannot clear newer dirty data.</summary>
    [Fact]
    public void UniformCaptureAllocatesAtExecutionAndPreservesNewerEdits()
    {
        Start(); var context = RHICommandContext.Current; using var uniform = new PackedUniformBuffer(16);
        uniform.SetBytes(Enumerable.Repeat((byte)7, 16).ToArray());
        using var list = context.CreateCommandList(); list.BindUniformSnapshot(0, uniform.Capture()); list.Close(true);
        uniform.SetBytes(Enumerable.Repeat((byte)9, 16).ToArray());
        TestUniformRing.BeginFrame();
        using (var boundary = context.EnterBoundary(list.Boundary))
        { Assert.Equal(CommandListState.Executed, context.Submit(list).State); }
        Assert.True(uniform.IsDirty);
        list.Reset(); list.BindUniformSnapshot(0, uniform.Capture()); list.Close(true);
        using (var boundary = context.EnterBoundary(list.Boundary))
        { Assert.Equal(CommandListState.Executed, context.Submit(list).State); }
        Assert.False(uniform.IsDirty);
    }
    /// <summary>Fixed executable references reject retirement rather than silently following a newly created pipeline.</summary>
    [Fact]
    public void RetiredPipelineFailsRecordedSelection()
    {
        Start(); var context = RHICommandContext.Current;
        using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        using var list = context.CreateCommandList(); list.SetPipeline(pipeline); list.Close(); pipeline.Dispose();
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        Assert.NotNull(result.Error);
    }
    /// <summary>Image payloads freeze client bytes and current bounds are rechecked after deliberate owner resize.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ImagePayloadAndOwnerFollowingBoundsAreResolvedAtExecution(bool shrink)
    {
        Start(); var context = RHICommandContext.Current;
        using var image = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 2, Height = 2,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var list = context.CreateCommandList();
        byte[] bytes = MemoryMarshal.AsBytes(new float[] { .2f, .4f, .6f, .8f }.AsSpan()).ToArray();
        list.UploadTexture(image, new(new(), 0, 0, 0, 2, 2), new(PixelChannelLayout.Red, PixelComponentType.Float), bytes);
        Array.Clear(bytes); list.Close(); if (shrink) image.Resize(1, 1);
        var result = context.Submit(list);
        if (shrink) { Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex); }
        else { Assert.Equal(CommandListState.Executed, result.State); Assert.Equal(new float[] { .2f, .4f, .6f, .8f }, image.ReadPixels()); }
    }
    /// <summary>Passes consume copied draw arrays and captured bindings while expired borrows fail before native geometry.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GraphicsSnapshotsAndBorrowExpiryPreservePassRestoration(bool expire)
    {
        Start(); var context = RHICommandContext.Current; using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<RasterDepthReductionShaderProgram>();
        var layout = new RHIVertexLayout([new(0, 3, VertexComponentFormat.Float, VertexInterpretation.Floating, 0, 0, 12)]);
        using var geometry = ManagedGraphicsGeometry.Create(layout, PrimitiveTopology.Triangles,
            [new(0, MemoryMarshal.AsBytes(new float[] { -1, -1, 0, 3, -1, 0, -1, 3, 0 }.AsSpan()).ToArray())], [0, 1, 2]);
        using var borrow = new RHIGeometryBorrow(); var inputs = new RHIGeometryInputs(geometry.Inputs, borrow);
        using var source = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 2, Height = 2,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        source.UploadDataImmediate(new float[] { .2f, .4f, .6f, .8f });
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        using var pipeline = context.CreateGraphicsPipeline(new(shader.Shader, layout, new([new(TextureFormat.R32f)]), DynamicPipelineState.Viewport)).RequireResource();
        shader.HzbDepth = source; shader.SrcMip = 0;
        using var list = context.CreateCommandList(); var draws = new RHIDrawIndexed[] { new(0, 3) };
        list.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target)); list.SetPipeline(pipeline);
        list.SetDynamicState(new() { Viewport = new(0, 0, 1, 1) }); list.BindResources(shader.CaptureBindings());
        list.DrawIndexedBatch(inputs, draws); list.EndRenderPass(); draws[0] = new(0, 999); shader.HzbDepth = default;
        list.Close(true); if (expire) borrow.Dispose();
        int prior = GL.GetInteger(GetPName.DrawFramebufferBinding);
        using (var boundary = context.EnterBoundary(list.Boundary))
        {
            var result = context.Submit(list);
            Assert.Equal(expire ? CommandListState.Failed : CommandListState.Executed, result.State);
            if (expire) { Assert.Equal(4, result.FailureIndex); Assert.NotNull(result.Error); }
            else Assert.Null(result.Error);
            Assert.False(context.HasRenderPass);
        }
        Assert.Equal(prior, GL.GetInteger(GetPName.DrawFramebufferBinding));
        if (!expire) Assert.InRange(target[0].ReadPixels()[0], .1999f, .2001f);
    }
    /// <summary>Failures inside an active query release its native interval, and foreign owners cannot mutate recordings.</summary>
    [Fact]
    public void FailureUnwindsQueryAndRejectsForeignThreadMutation()
    {
        Start(); var context = RHICommandContext.Current;
        using var query = context.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource();
        using var buffer = Buffer(context, 16); using var list = context.CreateCommandList();
        list.BeginQuery(query); list.ClearBuffer(buffer, 0, 32); list.EndQuery(query); list.Close();
        Exception? foreign = null; var worker = new System.Threading.Thread(() =>
        { try { list.Cancel(); } catch (Exception error) { foreign = error; } }); worker.Start(); worker.Join();
        Assert.IsType<InvalidOperationException>(foreign); Assert.Equal(CommandListState.Closed, list.State);
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(1, result.FailureIndex);
        Assert.Null(result.CleanupError);
        context.BeginQuery(query); context.EndQuery(query); Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
    }
    /// <summary>An empty recording must not discard already selected immediate pipelines or inputs.</summary>
    [Fact]
    public void EmptyListDoesNotResetImmediateSelection()
    {
        Start(); var context = RHICommandContext.Current;
        using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        context.SetPipeline(pipeline); using var list = context.CreateCommandList(); list.Close();
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
        // The next validation must use the still-selected shader, rather than report that selection was lost.
        var error = Assert.ThrowsAny<Exception>(() => context.BindResources(new RHIResourceBindings(shader)));
        Assert.DoesNotContain("Select a pipeline", error.Message);
    }
    /// <summary>Reported unavailable uniform allocation is list failure and cannot commit a captured CPU version.</summary>
    [Fact]
    public void MissingUniformEpochFailsWithoutClearingDirtyWork()
    {
        Start(); var context = RHICommandContext.Current; using var uniform = new PackedUniformBuffer(16);
        uniform.SetBytes(Enumerable.Repeat((byte)3, 16).ToArray());
        using var list = context.CreateCommandList(); list.BindUniformSnapshot(0, uniform.Capture()); list.Close();
        Assert.True(RHIUniformRingSystem.TryGetCurrent(out var allocator));
        RHIUniformRingSystem.ClearCurrent();
        try { var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.True(uniform.IsDirty); }
        finally { RHIUniformRingSystem.SetCurrent(allocator); }
    }
    /// <summary>Boundary rejection preserves a caller-owned active pass instead of treating it as list cleanup.</summary>
    [Fact]
    public void RejectedSubmissionCannotEndAnExistingPass()
    {
        Start(); var context = RHICommandContext.Current;
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        using var boundary = context.EnterBoundary(new("Original active pass", true, operations: BoundaryOperations.RenderPasses));
        context.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target));
        using var list = context.CreateCommandList(); list.Close();
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State);
        Assert.True(context.HasRenderPass); context.EndRenderPass();
    }
    /// <summary>A disposed texture borrower cannot keep or recreate authority over its still-owned native storage.</summary>
    [Fact]
    public void ExpiredBorrowedTextureFailsAtExecutionWithoutDeletingOwner()
    {
        Start(); var context = RHICommandContext.Current;
        using var image = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 1, Height = 1,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var borrowed = new BorrowedTexture(image.TextureId); using var list = context.CreateCommandList();
        list.ClearTexture(borrowed, new(new(), 0, 0, 0, 1, 1), new(PixelChannelLayout.Red, PixelComponentType.Float), new byte[4]);
        list.Close(); borrowed.Dispose();
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.True(image.IsValid);
    }
    /// <summary>Unissued markers observe context retirement before list submission or cancellation.</summary>
    [Fact]
    public void PendingTicketsObserveRetirementWithoutSubmission()
    {
        Start(); var domain = new object(); RenderContextRegistry.RegisterCurrent(domain, static _ => true);
        try
        {
            var context = RHICommandContext.Current; using var buffer = Buffer(context, 16);
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            using var list = context.CreateCommandList(); using var marker = list.InsertFence();
            using var readback = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16))); var result = list.RequestQueryResult(query);
            list.Close(); RenderContextRegistry.Retire(domain);
            Assert.Equal(CompletionStatus.Cancelled, marker.Poll()); Assert.Equal(CompletionStatus.Cancelled, readback.Poll());
            Assert.Equal(CompletionStatus.Cancelled, result.Poll()); Assert.Equal(CommandListState.Cancelled, list.State);
            Assert.Equal(0, list.Count);
        }
        finally { RenderContextRegistry.RegisterCurrent(fixture, static _ => true); OpenGlStateCache.Current.InvalidateAll(); }
    }
    /// <summary>Copied helper slots use the declared native target and restore the original sampled object.</summary>
    [Fact]
    public void SampledSlotArraysFreezeAndRestoreTheirWholeFootprint()
    {
        Start(); var context = RHICommandContext.Current;
        var description = new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable };
        using var original = context.CreateDynamicTexture2D(description).RequireResource();
        using var desired = context.CreateDynamicTexture2D(description).RequireResource();
        int slot = GL.GetInteger(GetPName.ActiveTexture) - (int)TextureUnit.Texture0;
        context.BindSampledTextures([new(slot, original)]);
        using var list = context.CreateCommandList(); var inputs = new RHISampledTextureSlot[] { new(slot, desired) };
        list.BindSampledTextures(inputs); inputs[0] = new(slot, original); list.Close(true);
        using (var boundary = context.EnterBoundary(list.Boundary))
        {
            Assert.Equal(CommandListState.Executed, context.Submit(list).State);
            Assert.Equal(desired.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        }
        Assert.Equal(original.TextureId, GL.GetInteger(GetPName.TextureBinding2D));
        context.BindSampledTextures([new(slot, default)]);
    }
    /// <summary>Optional semaphore admission remains an execution check even when no native object could be created.</summary>
    [Fact]
    public void OptionalSemaphoreAdmissionRunsAtExecution()
    {
        Start(); var context = RHICommandContext.Current;
        // A private unpublished declaration reaches capability admission without manufacturing a native object.
        using var unavailable = new GpuSemaphore(); using var list = context.CreateCommandList();
        list.SignalSemaphore(unavailable, [], []); list.Close();
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        if (!context.Capabilities.ExternalSemaphores) Assert.IsType<NotSupportedException>(result.Error);
        else Assert.IsType<InvalidOperationException>(result.Error);
    }
    #endregion
    #region Private
    /// <summary>Admits existing fixture lifetimes and starts a current uniform publication epoch.</summary>
    private void Start() { EnsureContextValid(); fixture.InitializeResourceDisposal(); }
    /// <summary>Creates ready owned buffer storage through the final immediate factory.</summary>
    private static GpuBufferObject Buffer(RHICommandContext context, int bytes) => context.CreateBuffer(new()
    { Role = BufferRole.CopyDestination, Mode = ResourceStorageMode.Mutable, Capacity = bytes, Usage = BufferUsage.DynamicDraw }, new(new byte[bytes])).RequireResource();
    #endregion
}
