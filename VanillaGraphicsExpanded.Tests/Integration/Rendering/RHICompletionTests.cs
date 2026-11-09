using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks actual completion, request ownership and query-generation behavior of semantic operations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHICompletionTests(HeadlessGLFixture fixture)
{
    #region Public API
    #region Buffer readback ownership
    /// <summary>Readback snapshots copied storage and exposes bytes only after explicit GPU completion.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BufferReadbackOwnsItsSnapshot(bool registered)
    {
        // Arrange: queue a slice readback before mutating and retiring its source buffer.
        Start(registered);
        try
        {
            var context = RHICommandContext.Current;
            byte[] expected = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
            using var source = Buffer(context, expected);
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 4, 16)));
            // Act: mutate the source, then wait for the independently owned snapshot.
            Assert.Equal(CompletionStatus.Pending, request.Status);
            Assert.Throws<InvalidOperationException>(() => request.MapReadyReadback());
            context.ClearBuffer(source, 0, expected.Length, 99);
            source.Dispose();
            Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
            using (var mapped = request.MapReadyReadback())
            {
                // Assert: the completed bytes match the pre-mutation range and mapping ownership is enforced.
                Assert.Equal(expected.AsSpan(4, 16).ToArray(), mapped.Bytes.ToArray());
                Assert.Throws<InvalidOperationException>(() => request.MapReadyReadback());
                Assert.Throws<InvalidOperationException>(() => request.Cancel());
            }
            using var secondMapping = request.MapReadyReadback();
            Assert.Equal(expected.AsSpan(4, 16).ToArray(), secondMapping.Bytes.ToArray());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Cancellation never exposes unfinished staging or retires the independently owned source.</summary>
    [Fact]
    public void CancelledReadbackCannotMapOrResume()
    {
        // Arrange: queue a readback against a live source buffer.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = Buffer(context, new byte[16]);
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 0, 16)));
            // Act: cancel before the readback is consumed.
            request.Cancel();
            // Assert: the request cannot expose staging data and its source remains alive.
            Assert.Equal(CompletionStatus.Cancelled, request.Poll());
            Assert.Equal(CompletionStatus.Cancelled, request.Wait(TimeSpan.Zero));
            Assert.Throws<InvalidOperationException>(() => request.MapReadyReadback());
            Assert.True(source.IsValid);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Pitched image readback retains array layer selection and surrounding pack state.</summary>
    [Fact]
    public void LayeredReadbackPreservesPixelSelectors()
    {
        // Arrange: upload one array layer and capture the caller's pack alignment.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var texture = context.CreateTexture(new RHITextureDescription
            {
                Dimension = TextureDimension.TwoArray, Width = 2, Height = 2, Depth = 2,
                Format = TextureFormat.Rgba8, MipLevels = 1, Storage = ResourceStorageMode.Immutable
            }).RequireResource();
            byte[] pixels = Enumerable.Repeat((byte)31, 16).ToArray();
            var region = new RHITextureRegion(new(0, 1), 0, 0, 0, 2, 2, 1);
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
            context.UploadTexture(texture, region, encoding, pixels);
            int prior = GL.GetInteger(GetPName.PackAlignment);
            try
            {
                // Act: read the selected layer with a padded pack row.
                GL.PixelStore(PixelStoreParameter.PackAlignment, 8);
                OpenGlStateCache.Current.InvalidateAll();
                using var request = context.EnqueueReadback(new RHIReadbackDescription(texture, region, encoding, 12));
                Assert.Equal(8, GL.GetInteger(GetPName.PackAlignment));
                Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
                using var mapped = request.MapReadyReadback();
                // Assert: padding and selected pixels occupy the requested offsets.
                Assert.Equal(20, mapped.Bytes.Length);
                Assert.Equal(pixels.AsSpan(0, 8).ToArray(), mapped.Bytes[..8].ToArray());
                Assert.Equal(pixels.AsSpan(8, 8).ToArray(), mapped.Bytes.Slice(12, 8).ToArray());
            }
            finally { GL.PixelStore(PixelStoreParameter.PackAlignment, prior); OpenGlStateCache.Current.InvalidateAll(); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Query generation and accumulation

    /// <summary>Timestamp tickets bind one issued generation and cannot silently read a later submission.</summary>
    [Fact]
    public void QueryTicketsRejectReissueAndPreserveCancellation()
    {
        // Arrange: create one timestamp query whose issued generations can be superseded.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            Assert.True(GL.IsQuery(query.QueryId));
            Assert.Throws<InvalidOperationException>(() => context.RequestQueryResult(query));
            // Act: issue one timestamp, cancel one ticket, then issue a newer generation.
            context.WriteTimestamp(query);
            var obsolete = context.RequestQueryResult(query);
            var cancelled = context.RequestQueryResult(query);
            cancelled.Cancel();
            context.WriteTimestamp(query);
            Assert.Equal(CompletionStatus.Failed, obsolete.Poll());
            Assert.IsType<InvalidOperationException>(obsolete.Error);
            Assert.Equal(CompletionStatus.Cancelled, cancelled.Wait());
            var current = context.RequestQueryResult(query);
            // Assert: the obsolete ticket fails, cancellation is retained, and the current ticket completes.
            Assert.Equal(CompletionStatus.Ready, current.Wait());
            _ = current.Result;
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>CPU result reads temporarily clear a foreign query-buffer selector and restore its original binding.</summary>
    [Fact]
    public void QueryResultsPreserveQueryBufferBinding()
    {
        // Arrange: retain a caller-selected query-buffer binding across CPU result reads.
        Start();
        int previous = GL.GetInteger((GetPName)All.QueryBufferBinding);
        try
        {
            var context = RHICommandContext.Current;
            using var selected = Buffer(context, new byte[32]);
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            GL.BindBuffer(BufferTarget.QueryBuffer, (int)selected.ResourceId);
            OpenGlStateCache.Current.InvalidateAll();
            // Act: issue a timestamp and poll/read its result while another query buffer is bound.
            context.WriteTimestamp(query);
            var result = context.RequestQueryResult(query);
            _ = result.Poll();
            // Assert: polling and waiting retain the caller's query-buffer binding.
            Assert.Equal((int)selected.ResourceId, GL.GetInteger((GetPName)All.QueryBufferBinding));
            Assert.Equal(CompletionStatus.Ready, result.Wait());
            Assert.Equal((int)selected.ResourceId, GL.GetInteger((GetPName)All.QueryBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindBuffer(BufferTarget.QueryBuffer, previous); Finish(); }
    }

    /// <summary>The query owner preserves its interval and only matching end enables result requests.</summary>
    [Fact]
    public void QueryOwnerPreservesAccumulationAndConditionIntervals()
    {
        // Arrange: begin a samples-passed interval and reject a second begin or early result request.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var query = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
            // Act: complete the interval, read its result, and enter conditional rendering.
            context.BeginQuery(query);
            try
            {
                Assert.Throws<InvalidOperationException>(() => context.BeginQuery(query));
                Assert.Throws<InvalidOperationException>(() => context.RequestQueryResult(query));
            }
            finally { context.EndQuery(query); }
            var ticket = context.RequestQueryResult(query);
            // Assert: only the completed matching interval permits results and conditional rendering.
            Assert.Equal(CompletionStatus.Ready, ticket.Wait());
            Assert.Equal(0UL, ticket.Result);
            context.BeginConditionalRender(query, ConditionalRenderPolicy.NoWait);
            try { Assert.True(query.IsValid); }
            finally { context.EndConditionalRender(); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Fence and context lifetime

    /// <summary>Explicit GPU waits preserve owner authority and invalid references reject before dispatch.</summary>
    [Fact]
    public void ExplicitFencesRetainOwnershipUntilDisposal()
    {
        // Arrange: create a fence owner and validate its explicit wait operations.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var fence = context.InsertFence();
            // Act: wait on the live fence, then retire it and probe invalid operations.
            context.WaitGpu(fence);
            // Assert: ownership persists through the wait and disposed or malformed operations reject.
            Assert.Equal(CompletionStatus.Ready, context.WaitCpu(fence, TimeSpan.FromSeconds(5)));
            Assert.True(fence.IsValid);
            fence.Dispose();
#if DEBUG
            Assert.Throws<InvalidOperationException>(() => context.Poll(fence));
#endif
            Assert.Throws<InvalidOperationException>(() => context.WaitGpu(default));
            Assert.Throws<ArgumentOutOfRangeException>(() => context.Barrier(new((ResourceAccess)(1 << 20))));
            context.Barrier(new(ResourceAccess.ShaderStorage | ResourceAccess.BufferUpdate));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Retired context requests become cancelled without invoking a replacement context's native methods.</summary>
    [Fact]
    public void RetiredOriginCancelsPendingResults()
    {
        // Arrange: issue requests against an explicitly registered context lifetime.
        fixture.EnsureContextValid();
        var owner = new object();
        RenderContextRegistry.RegisterCurrent(owner, static _ => true);
        try
        {
            var context = RHICommandContext.Current;
            using var source = Buffer(context, new byte[16]);
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 0, 16)));
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            context.WriteTimestamp(query);
            var result = context.RequestQueryResult(query);
            // Act: retire the origin before observing its pending results.
            RenderContextRegistry.Retire(owner);
            // Assert: pending requests cancel without invoking the retired native context.
            Assert.Equal(CompletionStatus.Cancelled, request.Poll());
            Assert.Equal(CompletionStatus.Cancelled, result.Poll());
            Assert.Throws<InvalidOperationException>(() => context.InsertFence());
        }
        finally
        {
            RenderContextRegistry.Retire(owner);
            fixture.RestoreContextRegistration();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Foreign-thread observation and mapping reject without consuming the original request or its completion.</summary>
    [Fact]
    public void ForeignThreadCannotObserveOrMapResults()
    {
        // Arrange: create pending requests and a worker that attempts foreign-thread observations.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = Buffer(context, new byte[16]);
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 0, 16)));
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            context.WriteTimestamp(query);
            var result = context.RequestQueryResult(query);
            Exception? threadFailure = null;
            var worker = new System.Threading.Thread(() =>
            {
                try
                {
                Assert.Throws<InvalidOperationException>(() => request.Poll());
                Assert.Throws<InvalidOperationException>(() => result.Poll());
                }
                catch (Exception error) { threadFailure = error; }
            });
            worker.Start(); worker.Join();
            // Assert: foreign calls fail, while the original thread retains pending ownership.
            Assert.Null(threadFailure);
            Assert.Equal(CompletionStatus.Pending, request.Status);
            Assert.Equal(CompletionStatus.Pending, result.Status);
            Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(CompletionStatus.Ready, result.Wait());
            using var mapping = request.MapReadyReadback();
            // Act: attempt to read mapped bytes from the worker after completion.
            threadFailure = null;
            worker = new System.Threading.Thread(() =>
            {
                try { Assert.Throws<InvalidOperationException>(() => { _ = mapping.Bytes.Length; }); }
                catch (Exception error) { threadFailure = error; }
            });
            worker.Start(); worker.Join();
            // Assert: the worker is rejected and the original thread can still read the mapping.
            Assert.Null(threadFailure);
            Assert.Equal(16, mapping.Bytes.Length);
        }
        finally { Finish(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Creates initialized staging-independent source storage.</summary>
    private static GpuBufferObject Buffer(RHICommandContext context, byte[] bytes) => context.CreateBuffer(new RHIBufferDescription
    { Role = BufferRole.CopySource, Mode = ResourceStorageMode.Mutable, Capacity = bytes.Length, Usage = BufferUsage.DynamicRead }, new(bytes)).RequireResource();
    /// <summary>Starts an explicit registered or context-local ownership interval.</summary>
    private void Start(bool registered = true)
    {
        fixture.EnsureContextValid();
        if (registered) fixture.InitializeResourceDisposal();
        OpenGlStateCache.Current.InvalidateAll();
    }
    /// <summary>Drains owned candidates and withdraws selector knowledge after each test.</summary>
    private void Finish() { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    #endregion
}
