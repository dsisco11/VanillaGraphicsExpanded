using System;
using System.Linq;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual completion, request ownership and query-generation behavior of semantic operations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHICompletionTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Readback snapshots copied storage and exposes bytes only after explicit GPU completion.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BufferReadbackOwnsItsSnapshot(bool registered)
    {
        Start(registered);
        try
        {
            var context = RHICommandContext.Current;
            byte[] expected = Enumerable.Range(0, 32).Select(value => (byte)value).ToArray();
            using var source = Buffer(context, expected);
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 4, 16)));
            Assert.Equal(CompletionStatus.Pending, request.Status);
            Assert.Throws<InvalidOperationException>(() => request.MapReadyReadback());
            context.ClearBuffer(source, 0, expected.Length, 99);
            source.Dispose();
            Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
            using (var mapped = request.MapReadyReadback())
            {
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
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = Buffer(context, new byte[16]);
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 0, 16)));
            request.Cancel();
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
                GL.PixelStore(PixelStoreParameter.PackAlignment, 8);
                StateCache.Current.InvalidateAll();
                using var request = context.EnqueueReadback(new RHIReadbackDescription(texture, region, encoding, 12));
                Assert.Equal(8, GL.GetInteger(GetPName.PackAlignment));
                Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
                using var mapped = request.MapReadyReadback();
                Assert.Equal(20, mapped.Bytes.Length);
                Assert.Equal(pixels.AsSpan(0, 8).ToArray(), mapped.Bytes[..8].ToArray());
                Assert.Equal(pixels.AsSpan(8, 8).ToArray(), mapped.Bytes.Slice(12, 8).ToArray());
            }
            finally { GL.PixelStore(PixelStoreParameter.PackAlignment, prior); StateCache.Current.InvalidateAll(); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Timestamp tickets bind one issued generation and cannot silently read a later submission.</summary>
    [Fact]
    public void QueryTicketsRejectReissueAndPreserveCancellation()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            Assert.True(GL.IsQuery(query.QueryId));
            Assert.Throws<InvalidOperationException>(() => context.RequestQueryResult(query));
            context.WriteTimestamp(query);
            var obsolete = context.RequestQueryResult(query);
            var cancelled = context.RequestQueryResult(query);
            cancelled.Cancel();
            context.WriteTimestamp(query);
            Assert.Equal(CompletionStatus.Failed, obsolete.Poll());
            Assert.IsType<InvalidOperationException>(obsolete.Error);
            Assert.Equal(CompletionStatus.Cancelled, cancelled.Wait());
            var current = context.RequestQueryResult(query);
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
        Start();
        int previous = GL.GetInteger((GetPName)All.QueryBufferBinding);
        try
        {
            var context = RHICommandContext.Current;
            using var selected = Buffer(context, new byte[32]);
            using var query = context.CreateQuery(new(QueryKind.Timestamp)).RequireResource();
            GL.BindBuffer(BufferTarget.QueryBuffer, (int)selected.ResourceId);
            StateCache.Current.InvalidateAll();
            context.WriteTimestamp(query);
            var result = context.RequestQueryResult(query);
            _ = result.Poll();
            Assert.Equal((int)selected.ResourceId, GL.GetInteger((GetPName)All.QueryBufferBinding));
            Assert.Equal(CompletionStatus.Ready, result.Wait());
            Assert.Equal((int)selected.ResourceId, GL.GetInteger((GetPName)All.QueryBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.BindBuffer(BufferTarget.QueryBuffer, previous); Finish(); }
    }

    /// <summary>Active accumulation prevents owner retirement and only matching end enables result requests.</summary>
    [Fact]
    public void QueryCaptureGuardsItsOriginalOwner()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var query = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
            context.BeginQuery(query);
            try
            {
                Assert.Throws<InvalidOperationException>(() => query.Dispose());
                Assert.Throws<InvalidOperationException>(() => context.BeginQuery(query));
                Assert.Throws<InvalidOperationException>(() => context.RequestQueryResult(query));
            }
            finally { context.EndQuery(query); }
            var ticket = context.RequestQueryResult(query);
            Assert.Equal(CompletionStatus.Ready, ticket.Wait());
            Assert.Equal(0UL, ticket.Result);
            context.BeginConditionalRender(query, ConditionalRenderPolicy.NoWait);
            try { Assert.Throws<InvalidOperationException>(() => query.Dispose()); }
            finally { context.EndConditionalRender(); }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Explicit GPU waits preserve owner authority and invalid references reject before dispatch.</summary>
    [Fact]
    public void ExplicitFencesRetainOwnershipUntilDisposal()
    {
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var fence = context.InsertFence();
            context.WaitGpu(fence);
            Assert.Equal(CompletionStatus.Ready, context.WaitCpu(fence, TimeSpan.FromSeconds(5)));
            Assert.True(fence.IsValid);
            fence.Dispose();
            Assert.Throws<InvalidOperationException>(() => context.Poll(fence));
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
            RenderContextRegistry.Retire(owner);
            Assert.Equal(CompletionStatus.Cancelled, request.Poll());
            Assert.Equal(CompletionStatus.Cancelled, result.Poll());
            Assert.Throws<InvalidOperationException>(() => context.InsertFence());
        }
        finally
        {
            RenderContextRegistry.Retire(owner);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            StateCache.Current.InvalidateAll();
        }
    }
    /// <summary>Foreign-thread observation and mapping reject without consuming the original request or its completion.</summary>
    [Fact]
    public void ForeignThreadCannotObserveOrMapResults()
    {
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
            Assert.Null(threadFailure);
            Assert.Equal(CompletionStatus.Pending, request.Status);
            Assert.Equal(CompletionStatus.Pending, result.Status);
            Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
            Assert.Equal(CompletionStatus.Ready, result.Wait());
            using var mapping = request.MapReadyReadback();
            threadFailure = null;
            worker = new System.Threading.Thread(() =>
            {
                try { Assert.Throws<InvalidOperationException>(() => { _ = mapping.Bytes.Length; }); }
                catch (Exception error) { threadFailure = error; }
            });
            worker.Start(); worker.Join();
            Assert.Null(threadFailure);
            Assert.Equal(16, mapping.Bytes.Length);
        }
        finally { Finish(); }
    }
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
        StateCache.Current.InvalidateAll();
    }
    /// <summary>Drains owned candidates and withdraws selector knowledge after each test.</summary>
    private void Finish() { fixture.CleanupGpuResources(); StateCache.Current.InvalidateAll(); }
    #endregion
}
