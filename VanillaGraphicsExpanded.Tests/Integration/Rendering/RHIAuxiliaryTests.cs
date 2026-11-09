using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Exercises capture effects, completion failure and optional resource rejection.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIAuxiliaryTests(HeadlessGLFixture fixture, Xunit.ITestOutputHelper outputLog)
{
    private static WaitSyncStatus injectedWait;
    private static int waitCalls;
    private static int unmapCalls;
    #region Public API
    #region Semaphore admission
    /// <summary>A local generated semaphore stays disposable but nonconsumable until a successful payload import.</summary>
    [Fact]
    public void LocalSemaphoreReservationCannotWaitOrSignal()
    {
        // Arrange: query semaphore capability before creating a local-only reservation.
        fixture.EnsureContextValid();
        fixture.RetireResourceServices();
        var context = RHICommandContext.Current;
        if (!context.Capabilities.ExternalSemaphores)
        {
            // Assert: unsupported creation fails at the semantic capability boundary.
            Assert.Throws<NotSupportedException>(() => context.CreateSemaphore(new()));
            return;
        }
        using var semaphore = context.CreateSemaphore(new()).RequireResource();
        // Act: attempt wait and signal through a semaphore without imported native ownership.
        Assert.False(semaphore.HasMetadataSnapshot);
        // Assert: local reservation state never reaches native semaphore operations.
        Assert.Throws<InvalidOperationException>(() => context.WaitSemaphore(semaphore, [], []));
        Assert.Throws<InvalidOperationException>(() => context.SignalSemaphore(semaphore, [], []));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Query state transitions
    /// <summary>Native callbacks cannot end or replace a query before its begin transition is committed.</summary>
    [Fact]
    public void QueryBeginRejectsReentrantTransitions()
    {
        // Arrange: install a saved-original callback that attempts nested query transitions.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var query = context.CreateQuery(new(QueryKind.SamplesPassed)).RequireResource();
            using var observation = new QueryBeginObservation(() =>
            {
                Assert.Throws<InvalidOperationException>(() => context.EndQuery(query));
                Assert.Throws<InvalidOperationException>(() => context.BeginQuery(query));
            });
            // Act: begin the query while the observer attempts to end or begin it again.
            context.BeginQuery(query);
            // Assert: only the outer native begin is dispatched and the interval completes.
            Assert.Equal(1, observation.Calls);
            context.EndQuery(query);
            Assert.Equal(CompletionStatus.Ready, context.RequestQueryResult(query).Wait());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion

    #region Buffer mapping
    /// <summary>Reported unmap corruption fails the result and prevents a synchronous consumer from accepting bytes.</summary>
    [Fact]
    public void CorruptUnmapFailsReadbackAndReleasesMappingOnce()
    {
        // Arrange: complete a readback and intercept its native unmap operation.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = context.CreateBuffer(new RHIBufferDescription
            { Role = BufferRole.CopySource, Mode = ResourceStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.DynamicRead }).RequireResource();
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 0, 16)));
            Assert.Equal(CompletionStatus.Ready, request.Wait(TimeSpan.FromSeconds(5)));
            using var observation = new UnmapObservation();
            // Act: invalidate the mapped result when its owner attempts to release it.
            var mapped = request.MapReadyReadback();
            Assert.Throws<InvalidOperationException>(() => mapped.Dispose());
            // Assert: the request fails, the mapping is released once, and remapping is denied.
            Assert.Equal(CompletionStatus.Failed, request.Status);
            Assert.NotNull(request.Error);
            mapped.Dispose();
            Assert.Equal(1, unmapCalls);
            Assert.Throws<InvalidOperationException>(() => request.MapReadyReadback());
        }
        finally { Finish(); }
    }
    #endregion

    #region Sparse image residency
    /// <summary>Configured sparse-image integration uses typed residency without disturbing the incoming selector.</summary>
    [Fact]
    public void SparseImageResidencyPreservesSelectionAndContents()
    {
        // Arrange: query the native page dimensions and create an externally allocated sparse image.
        Start();
        int name = 0;
        try
        {
            var context = RHICommandContext.Current;
            Assert.SkipWhen(!context.Capabilities.SparseTextures, "Native sparse-image residency is unavailable on this context.");
            // This fixture acts as the external sparse allocator; production commitment does not rediscover these facts.
            GL.GetInternalformat(ImageTarget.Texture2D, SizedInternalFormat.Rgba8,
                (InternalFormatParameter)ArbSparseTexture.VirtualPageSizeXArb, 1, out int width);
            GL.GetInternalformat(ImageTarget.Texture2D, SizedInternalFormat.Rgba8,
                (InternalFormatParameter)ArbSparseTexture.VirtualPageSizeYArb, 1, out int height);
            Assert.True(width > 0 && height > 0);
            GL.CreateTextures(TextureTarget.Texture2D, 1, out name);
            GL.TextureParameter(name, (TextureParameterName)ArbSparseTexture.TextureSparseArb, 1);
            GL.TextureStorage2D(name, 1, SizedInternalFormat.Rgba8, width, height);
            using var borrowed = new BorrowedTexture(name);
            int previous = GL.GetInteger(GetPName.TextureBinding2D);
            OpenGlStateCache.Current.InvalidateAll();
            var region = new RHITextureRegion(new(0), 0, 0, 0, width, height, 1);
            // Act: commit, upload and read the page through the typed RHI operations.
            context.CommitTexturePages(borrowed, region, true);
            // Assert: commitment restores the prior selector before the page is uploaded and read back.
            // Assert: commitment restores the prior selector and readback reproduces uploaded page bytes.
            Assert.Equal(previous, GL.GetInteger(GetPName.TextureBinding2D));
            var bytes = new byte[checked(width * height * 4)];
            Array.Fill(bytes, (byte)23);
            var encoding = new RHIPixelEncoding(PixelChannelLayout.Rgba, PixelComponentType.UnsignedByte);
            context.UploadTexture(borrowed, region, encoding, bytes);
            using (var result = context.EnqueueReadback(new RHIReadbackDescription(borrowed, region, encoding)))
            {
                Assert.Equal(CompletionStatus.Ready, result.Wait(TimeSpan.FromSeconds(5)));
                using var mapped = result.MapReadyReadback();
                Assert.Equal(bytes, mapped.Bytes.ToArray());
            }
            context.CommitTexturePages(borrowed, region, false);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (name != 0) GL.DeleteTexture(name);
            Finish();
        }
    }
    #endregion

    #region Transform feedback

    /// <summary>The owner preserves capture and outputs across pause/resume; scope unwind restores selection.</summary>
    [Fact]
    public void TransformFeedbackCapturesAcrossOneCompleteScope()
    {
        // Arrange: create the real GL program and bind its output to an RHI-owned transform-feedback buffer.
        Start();
        int program = 0, shader = 0, vao = 0;
        int previousProgram = GL.GetInteger(GetPName.CurrentProgram);
        int previousVao = GL.GetInteger(GetPName.VertexArrayBinding);
        bool discard = GL.IsEnabled(EnableCap.RasterizerDiscard);
        try
        {
            var context = RHICommandContext.Current;
            using var output = context.CreateBuffer(new RHIBufferDescription
            { Role = BufferRole.TransformFeedback, Mode = ResourceStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.StreamRead }).RequireResource();
            using var capture = context.CreateTransformFeedback(new([new(0, new(output, 0, 16))])).RequireResource();
            shader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(shader, "#version 450 core\nout float captured;\nvoid main(){ captured=3.0; gl_Position=vec4(0.0); }");
            GL.CompileShader(shader);
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int compiled);
            Assert.Equal(1, compiled);
            program = GL.CreateProgram(); GL.AttachShader(program, shader);
            GL.TransformFeedbackVaryings(program, 1, ["captured"], TransformFeedbackMode.InterleavedAttribs);
            GL.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.Equal(1, linked);
            GL.CreateVertexArrays(1, out vao); GL.BindVertexArray(vao); GL.UseProgram(program);
            GL.Enable(EnableCap.RasterizerDiscard);
            int previous = GL.GetInteger(GetPName.TransformFeedbackBinding);
            using var beginObservation = new BeginObservation(() =>
            {
                Assert.Throws<InvalidOperationException>(() => context.EndTransformFeedback());
                Assert.Throws<InvalidOperationException>(() => context.BeginTransformFeedback(capture, CapturePrimitive.Points));
            });
            using (context.CaptureTransformFeedback(capture, CapturePrimitive.Points))
            {
                // Act: begin capture, pause/resume it, and draw twice within the same owner scope.
                Assert.Equal(capture.TransformFeedbackId, GL.GetInteger(GetPName.TransformFeedbackBinding));
                Assert.True(output.IsValid);
                Assert.True(capture.IsValid);
                GL.DrawArrays(PrimitiveType.Points, 0, 1);
                context.PauseTransformFeedback();
                Assert.Equal(capture.TransformFeedbackId, GL.GetInteger(GetPName.TransformFeedbackBinding));
                context.ResumeTransformFeedback();
                GL.DrawArrays(PrimitiveType.Points, 0, 1);
            }
            Assert.Equal(1, beginObservation.Calls);
            Assert.Equal(previous, GL.GetInteger(GetPName.TransformFeedbackBinding));
            context.Barrier(new(ResourceAccess.BufferUpdate));
            using var result = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(output, 0, 8)));
            Assert.Equal(CompletionStatus.Ready, result.Wait(TimeSpan.FromSeconds(5)));
            // Assert: both captured records contain the shader's authored value.
            using var mapped = result.MapReadyReadback();
            Assert.Equal(new float[] { 3, 3 }, MemoryMarshal.Cast<byte, float>(mapped.Bytes).ToArray());
            // An incompatible source change withdraws the prepared capture; repeated use
            // cannot fall back to its former range. Another legal change makes it usable again.
            output.Allocate(8);
            Assert.Throws<ArgumentOutOfRangeException>(() => context.BeginTransformFeedback(capture, CapturePrimitive.Points));
            Assert.Throws<ArgumentOutOfRangeException>(() => context.BeginTransformFeedback(capture, CapturePrimitive.Points));
            output.Allocate(16);
            using (context.CaptureTransformFeedback(capture, CapturePrimitive.Points))
                GL.DrawArrays(PrimitiveType.Points, 0, 1);
            Assert.Equal(previous, GL.GetInteger(GetPName.TransformFeedbackBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            if (!discard) GL.Disable(EnableCap.RasterizerDiscard);
            GL.UseProgram(previousProgram); GL.BindVertexArray(previousVao);
            if (vao != 0) GL.DeleteVertexArray(vao);
            if (program != 0) GL.DeleteProgram(program);
            if (shader != 0) GL.DeleteShader(shader);
            Finish();
        }
    }
    #endregion

    #region Completion ownership

    /// <summary>A pending or failed wait never authorizes readback mapping or successful fence consumption.</summary>
    [Fact]
    public void WaitFailureAndPendingDoNotReleaseCompletionOwnership()
    {
        // Arrange: record wait ownership and intercept sync outcomes for readback and fence requests.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            using var source = context.CreateBuffer(new RHIBufferDescription
            { Role = BufferRole.CopySource, Mode = ResourceStorageMode.Mutable, Capacity = 16, Usage = BufferUsage.DynamicRead }).RequireResource();
            using var request = context.EnqueueReadback(new RHIReadbackDescription(new RHIBufferRange(source, 0, 16)));
            using var fence = context.InsertFence();
            using var list = context.CreateCommandList();
            using var recordedFence = list.InsertFence(); list.Close();
            Assert.Equal(CommandListState.Executed, context.Submit(list).State);
            using var interception = new WaitObservation();
            injectedWait = WaitSyncStatus.TimeoutExpired;
            // Act: observe a timeout before mapping, then inject a failed wait.
            // Assert: timeout leaves each request pending and rejects premature consumption.
            Assert.Equal(CompletionStatus.Pending, request.Poll());
            Assert.False(fence.TryConsumeIfSignaled());
            Assert.Equal(CompletionStatus.Pending, recordedFence.Poll());
            Assert.Throws<InvalidOperationException>(() => request.MapReadyReadback());
            injectedWait = WaitSyncStatus.WaitFailed;
            Assert.Equal(CompletionStatus.Failed, request.Poll());
            Assert.NotNull(request.Error);
            Assert.Throws<InvalidOperationException>(() => fence.TryConsumeIfSignaled());
            Assert.True(fence.IsValid);
            Assert.Equal(CompletionStatus.Failed, recordedFence.Poll()); Assert.NotNull(recordedFence.Error);
            Assert.Equal(6, waitCalls);
        }
        finally { Finish(); }
    }
    #endregion

    #region Optional capabilities

    /// <summary>Optional statistics preserve their native query result contract when the capability exists.</summary>
    [Fact]
    public void PipelineStatisticsUseDeclaredOptionalKind()
    {
        // Arrange: select the capability-dependent query kind on the active context.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            if (!context.Capabilities.PipelineStatistics)
            {
                Assert.Throws<NotSupportedException>(() => context.CreateQuery(new(QueryKind.VerticesSubmitted)));
                return;
            }
            using var query = context.CreateQuery(new(QueryKind.VerticesSubmitted)).RequireResource();
            // Act: issue and complete the optional statistics query.
            context.BeginQuery(query); context.EndQuery(query);
            var result = context.RequestQueryResult(query);
            // Assert: the declared result can be read and leaves GL error state clean.
            Assert.Equal(CompletionStatus.Ready, result.Wait());
            Assert.Equal(0UL, result.Result);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }

    /// <summary>Optional imports validate ownership and sparse-buffer requests validate capability and page ranges.</summary>
    [Fact]
    public void OptionalRequestsValidateImportsAndSparseRanges()
    {
        // Arrange: read the optional capability set used to choose supported and unsupported paths.
        Start();
        try
        {
            var context = RHICommandContext.Current;
            outputLog.WriteLine($"SparseBuffer={context.Capabilities.SparseBuffers}; SparseTexture={context.Capabilities.SparseTextures}; PipelineStatistics={context.Capabilities.PipelineStatistics}; Semaphore={context.Capabilities.ExternalSemaphores}; Memory={context.Capabilities.ExternalMemory}; MemoryWin32={context.Capabilities.ExternalMemoryWin32}; SemaphoreWin32={context.Capabilities.ExternalSemaphoresWin32}");
            // Act: validate imported handles and sparse page ranges at the semantic boundary.
            if (context.Capabilities.ExternalSemaphores)
                Assert.Throws<ArgumentException>(() => context.CreateSemaphore(new(new(ExternalHandleKind.OpaqueWin32, 1, ExternalHandleOwnership.TransferToDriver))));
            else Assert.Throws<NotSupportedException>(() => context.CreateSemaphore(new()));
            if (context.Capabilities.ExternalMemory)
                Assert.Throws<ArgumentException>(() => context.CreateMemoryObject(new(Import: new(ExternalHandleKind.OpaqueFileDescriptor, -1, ExternalHandleOwnership.TransferToDriver), Size: 16)));
            else Assert.Throws<NotSupportedException>(() => context.CreateMemoryObject(new()));
            if (!context.Capabilities.SparseBuffers)
                Assert.Throws<NotSupportedException>(() => context.CommitBufferPages(default, true));
            else
            {
                int size = context.Capabilities.SparseBufferPageSize;
                using var sparse = context.CreateBuffer(new RHIBufferDescription
                { Role = BufferRole.CopyDestination, Mode = ResourceStorageMode.Immutable, Capacity = size * 2, Access = StorageAccess.Sparse }).RequireResource();
                context.CommitBufferPages(new(sparse, 0, size), true);
                context.CommitBufferPages(new(sparse, 0, size), false);
                Assert.Throws<ArgumentException>(() => context.CommitBufferPages(new(sparse, 1, size), true));
            }
            // Assert: rejected inputs do not leave native GL errors pending.
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { Finish(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Observes saved-original query dispatch instead of patching the managed wrapper it bypasses.</summary>
    private sealed unsafe class QueryBeginObservation : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        internal int Calls { get; private set; }
        #region Public API
        /// <summary>Decorates the real driver begin while containing test assertions at the unmanaged boundary.</summary>
        public QueryBeginObservation(Action callback)
        {
            nint original = native.Original("glBeginQuery");
            native.Add("glBeginQuery", new Begin((target, id) =>
            {
                ((delegate* unmanaged[Stdcall]<int, uint, void>)original)(target, id);
                Calls++;
                native.Observe(callback);
            }));
            native.Install();
        }
        /// <summary>Restores ordinary binding loading and reports any captured assertion failure.</summary>
        public void Dispose() => native.Dispose();
        #endregion
        #region Private
        /// <summary>Matches the native begin-query ABI.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Begin(int target, uint id);
        #endregion
    }

    /// <summary>Exercises reentry from a real native begin call without adding production callback machinery.</summary>
    private sealed unsafe class BeginObservation : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        /// <summary>Counts actual begin dispatch independently of managed wrapper selection.</summary>
        internal int Calls { get; private set; }
        /// <summary>Matches transform-feedback begin primitive selection.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Begin(int primitive);
        #region Public API
        /// <summary>Installs a test callback after the selected native begin operation.</summary>
        public BeginObservation(Action callback)
        {
            var original = (delegate* unmanaged[Stdcall]<int, void>)native.Original("glBeginTransformFeedback");
            native.Add("glBeginTransformFeedback", new Begin(primitive =>
            {
                original(primitive);
                Calls++;
                native.Observe(callback);
            }));
            native.Install();
        }
        /// <summary>Releases only this fixture's interception and captured references.</summary>
        public void Dispose() => native.Dispose();
        #endregion
    }
    /// <summary>Injects corruption after a real native unmap, preserving actual cleanup and deterministic failure evidence.</summary>
    private sealed unsafe class UnmapObservation : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        #region Public API
        /// <summary>Observes the core named unmap without a production diagnostic switch.</summary>
        public UnmapObservation()
        {
            unmapCalls = 0;
            nint original = native.Original("glUnmapNamedBuffer");
            native.Add("glUnmapNamedBuffer", new Unmap(id =>
            {
                _ = ((delegate* unmanaged[Stdcall]<uint, byte>)original)(id);
                unmapCalls++;
                return 0;
            }));
            native.Install();
        }
        /// <summary>Removes only the fixture-owned interception.</summary>
        public void Dispose() => native.Dispose();
        #endregion
        #region Private
        /// <summary>Reports native corruption after the real mapping has been released.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte Unmap(uint id);
        #endregion
    }
    /// <summary>Starts the native execution and resource-retirement interval.</summary>
    private void Start() { fixture.EnsureContextValid(); fixture.InitializeResourceDisposal(); OpenGlStateCache.Current.InvalidateAll(); }
    /// <summary>Drains test-owned resources and clears external selector changes.</summary>
    private void Finish() { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    /// <summary>Substitutes only native wait outcomes to prove deterministic pending/failure transitions.</summary>
    private sealed class WaitObservation : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        /// <summary>Matches the native pointer-width fence wait and unsigned timeout ABI.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Wait(nint sync, uint flags, ulong timeout);
        #region Public API
        /// <summary>Installs observation without adding a production instrumentation path.</summary>
        public WaitObservation()
        {
            waitCalls = 0;
            native.Add("glClientWaitSync", new Wait((_, _, _) => { waitCalls++; return (int)injectedWait; }));
            native.Install();
        }
        /// <summary>Removes every test-owned hook.</summary>
        public void Dispose() => native.Dispose();
        #endregion
    }
    #endregion
}
