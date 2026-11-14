using TestUniformRing = VanillaGraphicsExpanded.Tests.Support.GPU.Native.TestUniformRing;
using VanillaGraphicsExpanded.Tests.Support.Rendering.Uniforms;
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


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;
/// <summary>Exercises frozen recording, exclusive lifecycle, ordered failure and independently owned completion on a real context.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHICommandListTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Frozen input and cancellation
    /// <summary>Variable payloads survive caller mutation and reusable pages survive reset without replay authority.</summary>
    [Fact]
    public void FrozenBytesAndOrderedOperationsMatchImmediateResults()
    {
        // Arrange: prepare caller-owned bytes, two destinations, and pending completion requests.
        Start(); var context = RHICommandContext.Current;
        using var source = Buffer(context, 32); using var destination = Buffer(context, 32);
        using var list = context.CreateCommandList();
        byte[] bytes = Enumerable.Range(0, 32).Select(x => (byte)x).ToArray();
        list.UploadBuffer(source, 0, bytes); Array.Fill(bytes, (byte)99);
        list.CopyBuffer(source, 0, destination, 0, 32); list.ClearBuffer(destination, 8, 8, 77);
        using var readback = list.EnqueueReadback(new(new RHIBufferRange(destination, 0, 32)));
        using var fence = list.InsertFence(); list.Close();
        // Assert: closing a list does not execute it or expose its pending readback.
        Assert.Equal(CompletionStatus.Pending, readback.Poll());
        Assert.Throws<InvalidOperationException>(() => readback.MapReadyReadback());
        Assert.Throws<InvalidOperationException>(list.Reset);
        Assert.Throws<InvalidOperationException>(() => list.ClearBuffer(source, 0, 1));
        // Act: execute the frozen operations, reset the list, and reuse its recording storage.
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
    /// <summary>Cancellation resolves unissued requests and leaves captured uniform data dirty for a later submission.</summary>
    [Fact]
    public void CancelResolvesPendingRequestsWithoutUniformPublication()
    {
        // Arrange: capture dirty uniform bytes and record buffer, readback, fence, and query work.
        Start(); var context = RHICommandContext.Current;
        using var buffer = Buffer(context, 16); using var uniform = new PackedUniformBuffer(16);
        uniform.SetPayload(Enumerable.Repeat((byte)9, 16).ToArray());
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var list = context.CreateCommandList();
        list.ClearBuffer(buffer, 0, 16, 33); list.BindUniformSnapshot(0, uniform.Capture());
        using var readback = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
        using var fence = list.InsertFence(); var queryResult = list.RequestQueryResult(query);
        // Act: cancel the closed list before any submission.
        list.Close(); list.Cancel();
        // Assert: requests cancel and the CPU uniform remains dirty; no mapped-content claim is made.
        Assert.True(uniform.IsDirty); Assert.Equal(0, readback.ByteCount);
        Assert.Equal(CompletionStatus.Cancelled, readback.Poll());
        Assert.Equal(CompletionStatus.Cancelled, fence.Poll()); Assert.Equal(CompletionStatus.Cancelled, queryResult.Poll());
        Assert.Throws<InvalidOperationException>(() => context.Submit(list));
        // Act: reset and submit an empty recording after cancellation.
        list.Reset(); list.Close();
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
    }
    #endregion

    #region Submission ordering and query completion
    /// <summary>A failed command stops subsequent writes while earlier readbacks retain independent completion ownership.</summary>
    [Fact]
    public void PartialFailurePreservesFirstErrorAndEnqueuedRequests()
    {
        // Arrange: enqueue one readback before a retired resource and another after it.
        Start(); var context = RHICommandContext.Current;
        using var buffer = Buffer(context, 16); using var retired = Buffer(context, 16);
        using var list = context.CreateCommandList();
        list.ClearBuffer(buffer, 0, 16, 7);
        using var earlier = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
        list.ClearBuffer(retired, 0, 16, 1); list.ClearBuffer(buffer, 0, 16, 99);
        using var later = list.EnqueueReadback(new(new RHIBufferRange(buffer, 0, 16)));
        using var marker = list.InsertFence(); list.Close(); retired.Dispose();
        // Act: submit until the retired resource fails and then release list-owned recording storage.
        var result = context.Submit(list);
        // Assert: later requests share the first failure while the earlier readback remains readable.
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
        // Arrange: create timestamp and elapsed-time queries before recording their operations.
        Start(); var context = RHICommandContext.Current;
        using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
        using var elapsed = context.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource();
        using var list = context.CreateCommandList();
        // Act: reject an unbalanced recording, then balance and submit both query types.
        list.BeginQuery(elapsed); Assert.Throws<InvalidOperationException>(() => list.Close());
        list.EndQuery(elapsed); list.WriteTimestamp(query); var result = list.RequestQueryResult(query);
        var duration = list.RequestQueryResult(elapsed); list.Close();
        // Assert: invalid balancing rejects closure and ready results retain their issued query generation.
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
        // Arrange: record a clear before an incomplete render-pass footprint.
        Start(); var context = RHICommandContext.Current;
        using var image = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 2, Height = 2,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var buffer = Buffer(context, 16); using var list = context.CreateCommandList();
        list.ClearBuffer(buffer, 0, 16, 77);
        // Act: reject closing the incomplete list, complete it, then submit inside a narrower boundary.
        list.BeginRenderPass(new([new RHIColorAttachment(Image: RHIAttachment.FromTexture(image))]));
        Assert.Throws<InvalidOperationException>(() => list.Close()); list.EndRenderPass(); list.Close(true);
        using var boundary = context.EnterBoundary(new("No pass effects", true));
        // Assert: the incomplete footprint rejects before the recorded clear can execute.
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        Assert.False(context.HasRenderPass);
    }
    #endregion

    #region Resource snapshots and pipeline lifetimes
    /// <summary>Uniform bytes execute in the current epoch and an older captured version cannot clear newer dirty data.</summary>
    [Fact]
    public void UniformCaptureAllocatesAtExecutionAndPreservesNewerEdits()
    {
        // Arrange: capture one dirty uniform revision and then publish newer bytes before dispatch.
        Start(); var context = RHICommandContext.Current; using var uniform = new PackedUniformBuffer(16);
        uniform.SetPayload(Enumerable.Repeat((byte)7, 16).ToArray());
        using var list = context.CreateCommandList(); list.BindUniformSnapshot(0, uniform.Capture()); list.Close(true);
        uniform.SetPayload(Enumerable.Repeat((byte)9, 16).ToArray());
        TestUniformRing.BeginFrame();
        // Act: submit the older capture and verify only its captured revision is committed.
        using (var boundary = context.EnterBoundary(list.Boundary))
        { Assert.Equal(CommandListState.Executed, context.Submit(list).State); }
        // Assert: the older capture leaves newer CPU data dirty.
        Assert.True(uniform.IsDirty);
        // Act: record and submit the newer revision in a fresh recording.
        list.Reset(); list.BindUniformSnapshot(0, uniform.Capture()); list.Close(true);
        using (var boundary = context.EnterBoundary(list.Boundary))
        { Assert.Equal(CommandListState.Executed, context.Submit(list).State); }
        // Assert: publishing the newer snapshot clears its dirty state.
        Assert.False(uniform.IsDirty);
    }
    /// <summary>The recording owner executes or discards commands before retiring their fixed pipeline; diagnostics cover deliberate misuse.</summary>
    [Fact]
    public void RecordedPipelineOwnerExecutesOrDiscardsBeforeRetirement()
    {
        // Arrange: retain a fixed shader and record a pipeline reference.
        Start(); var context = RHICommandContext.Current;
        using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        using var list = context.CreateCommandList(); list.SetPipeline(pipeline); list.Close();
        // Act: execute one recording and cancel a second before retiring its pipeline.
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
        using var discardedPipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        using var discarded = context.CreateCommandList(); discarded.SetPipeline(discardedPipeline); discarded.Close();
        discarded.Cancel(); discardedPipeline.Dispose();
        // Assert: the recording resolves before pipeline retirement and does not retire its shader owner.
        Assert.Equal(CommandListState.Cancelled, discarded.State);
#if DEBUG
        using var misuse = context.CreateCommandList(); misuse.SetPipeline(pipeline); misuse.Close(); pipeline.Dispose();
        var result = context.Submit(misuse); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        Assert.NotNull(result.Error);
#else
        pipeline.Dispose();
#endif
        Assert.False(shader.IsDisposed);
    }
    /// <summary>Image payloads freeze client bytes and current bounds are rechecked after deliberate owner resize.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ImagePayloadAndOwnerFollowingBoundsAreResolvedAtExecution(bool shrink)
    {
        // Arrange: record an upload from caller-owned bytes before optionally shrinking its owner.
        Start(); var context = RHICommandContext.Current;
        using var image = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 2, Height = 2,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var list = context.CreateCommandList();
        byte[] bytes = MemoryMarshal.AsBytes(new float[] { .2f, .4f, .6f, .8f }.AsSpan()).ToArray();
        // Act: record copied bytes, mutate the caller array, and submit against current owner bounds.
        list.UploadTexture(image, new(new(), 0, 0, 0, 2, 2), new(PixelChannelLayout.Red, PixelComponentType.Float), bytes);
        Array.Clear(bytes); list.Close(); if (shrink) image.Resize(1, 1);
        var result = context.Submit(list);
        // Assert: submitted bytes are frozen, while owner-following bounds are checked at execution.
        if (shrink) { Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex); }
        else { Assert.Equal(CommandListState.Executed, result.State); Assert.Equal(new float[] { .2f, .4f, .6f, .8f }, image.ReadPixels()); }
    }
    /// <summary>Passes consume copied draw arrays and captured bindings while expired borrows fail before native geometry.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void GraphicsSnapshotsAndBorrowExpiryPreservePassRestoration(bool expire)
    {
        // Arrange: capture a real graphics pass, draw array, and borrowed geometry lifetime.
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
        // Act: record the pass and draw, mutate caller data, then execute or discard after borrow expiry.
        list.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target)); list.SetPipeline(pipeline);
        list.SetDynamicState(new() { Viewport = new(0, 0, 1, 1) }); list.BindResources(shader.CaptureBindings());
        list.DrawIndexedBatch(inputs, draws); list.EndRenderPass(); draws[0] = new(0, 999); shader.HzbDepth = default;
        list.Close(true);
#if !DEBUG
        if (expire)
        {
            // The recording owner discards the consumer before ending the engine borrowing interval.
            list.Cancel(); borrow.Dispose();
            Assert.Equal(CommandListState.Cancelled, list.State);
            Assert.False(context.HasRenderPass);
            return;
        }
#endif
        if (expire) borrow.Dispose();
        int prior = GL.GetInteger(GetPName.DrawFramebufferBinding);
        using (var boundary = context.EnterBoundary(list.Boundary))
        {
            var result = context.Submit(list);
            // Assert: expiration rejects before draw dispatch, and either path restores pass ownership.
            Assert.Equal(expire ? CommandListState.Failed : CommandListState.Executed, result.State);
            if (expire) { Assert.Equal(4, result.FailureIndex); Assert.NotNull(result.Error); }
            else Assert.Null(result.Error);
            Assert.False(context.HasRenderPass);
        }
        Assert.Equal(prior, GL.GetInteger(GetPName.DrawFramebufferBinding));
        if (!expire) Assert.InRange(target[0].ReadTexel()[0], .1999f, .2001f);
    }
    #endregion

    #region Failure, retirement, and boundary ownership
    /// <summary>Failures inside an active query release its native interval, and foreign owners cannot mutate recordings.</summary>
    [Fact]
    public void FailureUnwindsQueryAndRejectsForeignThreadMutation()
    {
        // Arrange: begin a query and record a command guaranteed to fail range validation.
        Start(); var context = RHICommandContext.Current;
        using var query = context.CreateQuery(new(QueryKind.TimeElapsed)).RequireResource();
        using var buffer = Buffer(context, 16); using var list = context.CreateCommandList();
        list.BeginQuery(query); list.ClearBuffer(buffer, 0, 32); list.EndQuery(query); list.Close();
        Exception? foreign = null; var worker = new System.Threading.Thread(() =>
        { try { list.Cancel(); } catch (Exception error) { foreign = error; } }); worker.Start(); worker.Join();
        // Assert: foreign-thread mutation is rejected without changing the closed recording.
        Assert.IsType<InvalidOperationException>(foreign); Assert.Equal(CommandListState.Closed, list.State);
        // Act: submit the invalid list, then begin a fresh interval to confirm cleanup released the query.
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(1, result.FailureIndex);
        Assert.Null(result.CleanupError);
        context.BeginQuery(query); context.EndQuery(query); Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
    }
    /// <summary>An empty recording must not discard already selected immediate pipelines or inputs.</summary>
    [Fact]
    public void EmptyListDoesNotResetImmediateSelection()
    {
        // Arrange: select a live compute pipeline before creating an empty command list.
        Start(); var context = RHICommandContext.Current;
        using var shader = context.CreateShader(RHIShaderInputs.ComputeDescription()).RequireResource();
        using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
        context.SetPipeline(pipeline); using var list = context.CreateCommandList(); list.Close();
        // Act: submit no recorded operations, then validate that immediate shader selection remains active.
        Assert.Equal(CommandListState.Executed, context.Submit(list).State);
        // Assert: validation sees the still-selected shader rather than reporting selection loss.
        var error = Assert.ThrowsAny<Exception>(() => context.BindResources(new RHIResourceBindings(shader)));
        Assert.DoesNotContain("Select a pipeline", error.Message);
    }
    /// <summary>Reported unavailable uniform allocation is list failure and cannot commit a captured CPU version.</summary>
    [Fact]
    public void MissingUniformEpochFailsWithoutClearingDirtyWork()
    {
        // Arrange: record a uniform snapshot and retain the allocator that owns its publication epoch.
        Start(); var context = RHICommandContext.Current; using var uniform = new PackedUniformBuffer(16);
        uniform.SetPayload(Enumerable.Repeat((byte)3, 16).ToArray());
        using var list = context.CreateCommandList(); list.BindUniformSnapshot(0, uniform.Capture()); list.Close();
        Assert.True(RHIUniformRingSystem.TryGetCurrent(out var allocator));
        RHIUniformRingSystem.ClearCurrent();
        // Act: remove the allocator epoch and submit the recorded snapshot.
        // Assert: submission fails without clearing the CPU dirty state.
        try { var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.True(uniform.IsDirty); }
        finally { RHIUniformRingSystem.SetCurrent(allocator); }
    }
    /// <summary>Boundary rejection preserves a caller-owned active pass instead of treating it as list cleanup.</summary>
    [Fact]
    public void RejectedSubmissionCannotEndAnExistingPass()
    {
        // Arrange: keep a caller-owned render pass active while preparing an empty list.
        Start(); var context = RHICommandContext.Current;
        using var target = CreateRenderTarget(1, 1, TextureFormat.R32f);
        using var boundary = context.EnterBoundary(new("Original active pass", true, operations: BoundaryOperations.RenderPasses));
        context.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(target));
        using var list = context.CreateCommandList(); list.Close();
        // Act: attempt submission while the caller's pass is already active.
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State);
        // Assert: rejection leaves the caller-owned pass active for explicit closure.
        Assert.True(context.HasRenderPass); context.EndRenderPass();
    }
    /// <summary>A disposed texture borrower cannot keep or recreate authority over its still-owned native storage.</summary>
    [Fact]
    public void ExpiredBorrowedTextureFailsAtExecutionWithoutDeletingOwner()
    {
        // Arrange: record a clear through a borrower of a still-owned image.
        Start(); var context = RHICommandContext.Current;
        using var image = context.CreateDynamicTexture2D(new() { Dimension = TextureDimension.Two, Width = 1, Height = 1,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
        using var borrowed = new BorrowedTexture(image.TextureId); using var list = context.CreateCommandList();
        list.ClearTexture(borrowed, new(new(), 0, 0, 0, 1, 1), new(PixelChannelLayout.Red, PixelComponentType.Float), new byte[4]);
        // Act: retire only the borrower before executing the recording.
        list.Close(); borrowed.Dispose();
        // Assert: execution rejects the borrower while the owning image remains valid.
        var result = context.Submit(list); Assert.Equal(CommandListState.Failed, result.State); Assert.True(image.IsValid);
    }
    #endregion

    #region Sampled-slot and optional capability submission
    /// <summary>Copied helper slots use the declared native target and restore the original sampled object.</summary>
    [Fact]
    public void SampledSlotArraysFreezeAndRestoreTheirWholeFootprint()
    {
        // Arrange: bind an original sampled image and capture a replacement slot array.
        Start(); var context = RHICommandContext.Current;
        var description = new RHITextureDescription { Dimension = TextureDimension.Two, Width = 1, Height = 1,
            Format = TextureFormat.R32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable };
        using var original = context.CreateDynamicTexture2D(description).RequireResource();
        using var desired = context.CreateDynamicTexture2D(description).RequireResource();
        int slot = GL.GetInteger(GetPName.ActiveTexture) - (int)TextureUnit.Texture0;
        context.BindSampledTextures([new(slot, original)]);
        using var list = context.CreateCommandList(); var inputs = new RHISampledTextureSlot[] { new(slot, desired) };
        list.BindSampledTextures(inputs); inputs[0] = new(slot, original); list.Close(true);
        // Act: execute the frozen replacement and leave the boundary.
        using (var boundary = context.EnterBoundary(list.Boundary))
        {
            // Assert: the recorded copy uses its frozen slot during execution.
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
        // Arrange: record a signal request for a private semaphore declaration with no published native owner.
        Start(); var context = RHICommandContext.Current;
        // A private unpublished declaration reaches capability admission without manufacturing a native object.
        using var unavailable = new GpuSemaphore(); using var list = context.CreateCommandList();
        list.SignalSemaphore(unavailable, [], []); list.Close();
        // Act: submit the closed recording so optional capability admission occurs at execution.
        var result = context.Submit(list);
        // Assert: unsupported or unpublished semaphore admission fails at its recorded operation.
        Assert.Equal(CommandListState.Failed, result.State); Assert.Equal(0, result.FailureIndex);
        if (!context.Capabilities.ExternalSemaphores) Assert.IsType<NotSupportedException>(result.Error);
        else Assert.IsType<InvalidOperationException>(result.Error);
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Admits existing fixture lifetimes and starts a current uniform publication epoch.</summary>
    private void Start() { EnsureContextValid(); fixture.InitializeResourceDisposal(); }
    /// <summary>Creates ready owned buffer storage through the final immediate factory.</summary>
    private static GpuBufferObject Buffer(RHICommandContext context, int bytes) => context.CreateBuffer(new()
    { Role = BufferRole.CopyDestination, Mode = ResourceStorageMode.Mutable, Capacity = bytes, Usage = BufferUsage.DynamicDraw }, new(new byte[bytes])).RequireResource();
    #endregion
}
