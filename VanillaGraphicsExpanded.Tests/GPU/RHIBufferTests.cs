using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using OpenTK.Windowing.GraphicsLibraryFramework;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using CoreGL = OpenTK.Graphics.OpenGL4.GL;
using ErrorCode = OpenTK.Graphics.OpenGL.ErrorCode;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises semantic creation, content operations and original-context rejection.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class RHIBufferTests(HeadlessGLFixture fixture)
{
    [ThreadStatic] private static Action? beforeAllocation;
    [ThreadStatic] private static Action? afterAllocation;

    #region Public API
    #region Creation and content
    /// <summary>Partial initial content works with immutable storage that rejects later client updates.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void InitialContentAndGpuOperationsPreserveSelectorsAndStorageRevision(bool immutable)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var description = Description(16);
            if (immutable) description = new() { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Immutable, Capacity = 16, Access = StorageAccess.None };
            using var sentinel = context.CreateBuffer(Description(16)).RequireResource();
            context.BindBuffer(BufferRole.Vertex, sentinel);
            byte[] initial = [1, 2, 3, 4];
            var result = context.CreateBuffer(description, new(initial, 4));
            Assert.True(result.IsSuccess); Assert.Null(result.Diagnostics);
            using var source = result.RequireResource();
            using var destination = context.CreateBuffer(Description(16)).RequireResource();
            var entry = GpuResourceManagerSystem.CurrentManager!.GetResourceRegistration(source)!;
            ulong revision = entry.MetadataRevision;
            context.CopyBuffer(source, 4, destination, 2, 4);
            context.ClearBuffer(source, 4, 4, 9);
            byte[] bytes = new byte[4];
            using (var readback = context.EnqueueReadback(new(new RHIBufferRange(destination, 2, bytes.Length))))
            { Assert.Equal(CompletionStatus.Ready, readback.Wait()); using var mapped = readback.MapReadyReadback(); mapped.Bytes.CopyTo(bytes); }
            Assert.Equal(initial, bytes);
            using (var readback = context.EnqueueReadback(new(new RHIBufferRange(source, 4, bytes.Length))))
            { Assert.Equal(CompletionStatus.Ready, readback.Wait()); using var mapped = readback.MapReadyReadback(); mapped.Bytes.CopyTo(bytes); }
            Assert.Equal(new byte[] { 9, 9, 9, 9 }, bytes);
            if (immutable) Assert.Throws<InvalidOperationException>(() => context.UploadBuffer(source, 0, initial));
            else context.UploadBuffer(source, 0, initial);
            Assert.Equal(revision, entry.MetadataRevision);
            Assert.Equal(16, source.SizeBytes);
            Assert.Equal(sentinel.BufferId, GL.GetInteger(GetPName.ArrayBufferBinding));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Omission is rejected, explicit zero is accepted, and references grant no ownership.</summary>
    [Fact]
    public void CreationRequiresAssignedInputsAndReturnsOnlyCompletedOwners()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var incomplete = new RHIBufferDescription { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Usage = BufferUsage.StaticDraw };
            Assert.Throws<ArgumentException>(() => context.CreateBuffer(incomplete));
            Assert.False(default(RHICreationResult<GpuBufferObject>).IsSuccess);
            using var owner = context.CreateBuffer(Description(0)).RequireResource();
            GpuBufferRef reference = owner;
            Assert.True(reference.IsValid);
            Assert.True(reference.TryGetMetadata(out var data));
            Assert.Equal((nint)0, data.Capacity);
            owner.Dispose();
            Assert.False(reference.IsValid);
            Assert.ThrowsAny<InvalidOperationException>(() => context.ClearBuffer(reference, 0, 0));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>A failed native allocation returns diagnostics without exposing an owning declaration.</summary>
    [Fact]
    public void FailedCreationReturnsNoOwner()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.RHIBuffer.CreationFailure");
        try
        {
            var context = RHICommandContext.Current;
            harmony.Patch(((CreateBuffersCall)CoreGL.CreateBuffers).Method,
                prefix: new HarmonyMethod(typeof(RHIBufferTests), nameof(FailCreation)));
            var result = context.CreateBuffer(Description(16));
            Assert.False(result.IsSuccess);
            Assert.False(result.TryGetResource(out var owner));
            Assert.Null(owner);
            Assert.False(string.IsNullOrWhiteSpace(result.Diagnostics));
            Assert.Throws<InvalidOperationException>(() => result.RequireResource());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Mapping
    /// <summary>Named mapping flush offsets are relative to the mapped range, rather than allocation offsets.</summary>
    [Fact]
    public void ExplicitFlushUsesMappingRelativeOffset()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            using var buffer = RHICommandContext.Current.CreateBuffer(Description(32)).RequireResource();
            using (var mapping = buffer.MapRange<int>(16, 4, BufferMapAccess.Write | BufferMapAccess.ExplicitFlush))
            {
                mapping.Span[3] = 1234;
                mapping.Flush(12, 4);
            }
            using var read = buffer.MapRange<int>(28, 1, BufferMapAccess.Read);
            Assert.Equal(1234, read.Span[0]);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Rejected native mapping dispatch propagates once and cannot select another execution path.</summary>
    [Fact]
    public void MappingFailureNeverRetriesBoundMapping()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.RHIBuffer.MappingFailure");
        try
        {
            using var buffer = RHICommandContext.Current.CreateBuffer(Description(16)).RequireResource();
            harmony.Patch(((Func<int, nint, nint, OpenTK.Graphics.OpenGL4.BufferAccessMask, nint>)CoreGL.MapNamedBufferRange).Method,
                prefix: new HarmonyMethod(typeof(RHIBufferTests), nameof(RejectMapping)));
            Assert.Throws<InvalidOperationException>(() => buffer.MapRange<int>(0, 4, BufferMapAccess.Read));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
    #region Context identity
    /// <summary>An admitted context rejects execution while no native context is current, then remains usable on return.</summary>
    [Fact]
    public unsafe void OriginalContextIsRequiredBeforeNativeExecution()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var buffer = context.CreateBuffer(Description(16)).RequireResource();
            var original = GLFW.GetCurrentContext();
            try
            {
                GLFW.MakeContextCurrent(null);
                Assert.Throws<InvalidOperationException>(() => context.ClearBuffer(buffer, 0, 16));
            }
            finally { GLFW.MakeContextCurrent(original); }
            context.ClearBuffer(buffer, 0, 16);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Local tools retain physical identity without borrowing another context's selector knowledge or disposal authority.</summary>
    [Fact]
    public unsafe void UnregisteredContextCannotDonateResourceOrCacheIdentity()
    {
        fixture.EnsureContextValid();
        var original = GLFW.GetCurrentContext();
        var originalContext = RHICommandContext.Current;
        GLFW.WindowHint(WindowHintInt.ContextVersionMajor, 4);
        GLFW.WindowHint(WindowHintInt.ContextVersionMinor, 5);
        GLFW.WindowHint(WindowHintBool.Visible, false);
        var local = GLFW.CreateWindow(16, 16, "Local RHI context", null, null);
        Assert.True(local != null);
        GpuBufferObject? owner = null;
        try
        {
            GLFW.MakeContextCurrent(local);
            var localContext = RHICommandContext.Current;
            owner = localContext.CreateBuffer(Description(16)).RequireResource();
            localContext.BindBuffer(BufferRole.Vertex, owner);
            Assert.True(OpenGlStateCache.Current.TryGetCachedBoundBuffer(BufferTarget.ArrayBuffer, out _));
            GLFW.MakeContextCurrent(original);
            originalContext.RequireContext();
            Assert.False(OpenGlStateCache.Current.TryGetCachedBoundBuffer(BufferTarget.ArrayBuffer, out _));
            Assert.Throws<InvalidOperationException>(() => originalContext.ClearBuffer(owner, 0, 16));
            Assert.Throws<InvalidOperationException>(() => owner.Dispose());
            Assert.False(owner.IsDisposed);
            GLFW.MakeContextCurrent(local);
            owner.Dispose();
            owner = null;
            Assert.Equal(OpenTK.Graphics.OpenGL4.ErrorCode.NoError, CoreGL.GetError());
        }
        finally
        {
            GLFW.MakeContextCurrent(local);
            owner?.Dispose();
            GLFW.DestroyWindow(local);
            GLFW.MakeContextCurrent(original);
            GpuSupport.Initialize();
            OpenGlStateCache.Current.InvalidateAll();
        }
    }

    #endregion
    #region Publication and suppression
    /// <summary>Controlled binds suppress only complete known-equal keys; exact external calls always execute.</summary>
    [Fact]
    public void IndexedSuppressionIncludesGenericSelectionAndPreservesExternalCalls()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            using var first = context.CreateBuffer(Description(32)).RequireResource();
            using var second = context.CreateBuffer(Description(32)).RequireResource();
            using var capture = new BindingCallCapture();
            context.BindBufferBase(BufferRole.Uniform, 0, first);
            context.BindBufferBase(BufferRole.Uniform, 0, first);
            Assert.Equal(1, capture.Calls(nameof(GL.BindBufferBase)));
            context.BindBuffer(BufferRole.Uniform, second);
            context.BindBufferBase(BufferRole.Uniform, 0, first);
            Assert.Equal(2, capture.Calls(nameof(GL.BindBufferBase)));
            context.BindBufferRange(BufferRole.Uniform, 0, first, 0, 16);
            context.BindBufferRange(BufferRole.Uniform, 0, first, 0, 16);
            Assert.Equal(1, capture.Calls(nameof(GL.BindBufferRange)));
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, first.BufferId);
            GL.BindBufferBase(BufferRangeTarget.UniformBuffer, 0, first.BufferId);
            Assert.Equal(4, capture.Calls(nameof(GL.BindBufferBase)));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Native callbacks cannot change already frozen initialization intent or provoke another dispatch.</summary>
    [Fact]
    public void AllocationPublishesCapturedRequestAfterCallerMutation()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.RHIBuffer.FrozenRequest");
        try
        {
            var context = RHICommandContext.Current;
            using var buffer = context.CreateBuffer(Description(16)).RequireResource();
            var request = Description(64);
            int calls = 0;
            beforeAllocation = () => { calls++; request.Capacity = 128; };
            using var allocation = new NativeBufferAllocationCapture(before: ObserveAllocation);
            context.AllocateBufferStorage(buffer, request);
            Assert.Equal(1, calls);
            Assert.Equal((nint)128, request.Capacity);
            Assert.Equal(64, buffer.SizeBytes);
            GL.GetNamedBufferParameter(buffer.BufferId, BufferParameterName.BufferSize, out int capacity);
            Assert.Equal(64, capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { beforeAllocation = null; harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    /// <summary>Retirement during allocation stops partial initial-content dispatch and never targets zero or a replacement name.</summary>
    [Fact]
    public void RetiredAllocationStopsBeforePartialUpload()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var harmony = new Harmony("Tests.RHIBuffer.InterruptedInitialization");
        try
        {
            var context = RHICommandContext.Current;
            using var buffer = context.CreateBuffer(Description(16)).RequireResource();
            afterAllocation = buffer.Dispose;
            using var allocation = new NativeBufferAllocationCapture(after: ObserveCompletedAllocation);
            Assert.Throws<InvalidOperationException>(() => context.AllocateBufferStorage(buffer, Description(64), new byte[] { 1, 2, 3, 4 }));
            Assert.True(buffer.IsDisposed);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { afterAllocation = null; harmony.UnpatchAll(harmony.Id); fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Builds an explicit mutable allocation request for content tests.</summary>
    private static RHIBufferDescription Description(nint capacity) => new()
    { Role = BufferRole.Vertex, Mode = ResourceStorageMode.Mutable, Capacity = capacity, Usage = BufferUsage.DynamicDraw };

    /// <summary>Fails before mapping any bytes to verify exception propagation.</summary>
    private static void RejectMapping() => throw new InvalidOperationException("Injected named mapping failure.");

    /// <summary>Identifies the exact generated core allocation overload.</summary>
    private delegate void CreateBuffersCall(int count, out int buffer);

    /// <summary>Simulates an unsuccessful allocation without creating a native object.</summary>
    private static bool FailCreation(out int __1) { __1 = 0; return false; }

    /// <summary>Runs a test callback at the exact native allocation boundary.</summary>
    private static void ObserveAllocation() => beforeAllocation?.Invoke();

    /// <summary>Retires the target only after its native allocation has completed.</summary>
    private static void ObserveCompletedAllocation() => afterAllocation?.Invoke();
    #endregion
}

