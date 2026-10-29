using System;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks native rejection, realization and uncertainty before canonical or selector publication.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuBufferAcceptanceTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>A rejected zero-name range cannot erase an established generic or indexed binding.</summary>
    [Fact]
    public void NegativeOffsetUnbindPreservesIndexedAndGenericFacts()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateBuffers(1, out int id);
        try
        {
            GL.NamedBufferData(id, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, id, IntPtr.Zero, 16);
            GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, 0, (nint)(-1), 0);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            var cache = OpenGlStateCache.Current;
            Assert.False(cache.NeedsIndexedBufferBinding(BufferRangeTarget.UniformBuffer, 0, id, 0, 16, true));
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, 0, out int indexed);
            Assert.Equal(id, indexed); Assert.Equal(id, GL.GetInteger(GetPName.UniformBufferBinding));
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Invalid per-element multi-bind ranges preserve the slot even when its requested name is zero.</summary>
    [Theory]
    [InlineData(-1, 16)]
    [InlineData(0, 0)]
    public void InvalidZeroNameMultiRangePreservesPriorSlot(int offset, int size)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        GL.CreateBuffers(1, out int id);
        try
        {
            GL.NamedBufferData(id, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.BindBufferRange(BufferRangeTarget.UniformBuffer, 0, id, IntPtr.Zero, 16);
            GL.BindBuffersRange(BufferRangeTarget.UniformBuffer, 0, 1, new[] { 0 }, new[] { (nint)offset }, new[] { (nint)size });
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.False(OpenGlStateCache.Current.NeedsIndexedBufferBinding(BufferRangeTarget.UniformBuffer, 0, id, 0, 16, true));
            GL.GetInteger(GetIndexedPName.UniformBufferBinding, 0, out int actual); Assert.Equal(id, actual);
        }
        finally { GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Single, multi and EXT range rejections preserve prior feedback binding and buffer defaults.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void MisalignedFeedbackSizeDoesNotRealizeOrRebind(int variant)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var aliases = new OptionalAliases();
        using var feedback = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
        var cache = OpenGlStateCache.Current;
        OpenGLFeedbackBindings.Bind(cache, feedback.TransformFeedbackId);
        GL.CreateBuffers(1, out int prior);
        int reserved = GL.GenBuffer();
        try
        {
            GL.NamedBufferData(prior, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.BindBufferRange(BufferRangeTarget.TransformFeedbackBuffer, 0, prior, IntPtr.Zero, 16);
            if (variant == 0) GL.BindBufferRange(BufferRangeTarget.TransformFeedbackBuffer, 0, reserved, IntPtr.Zero, 3);
            else if (variant == 1) GL.BindBuffersRange(BufferRangeTarget.TransformFeedbackBuffer, 0, 1, new[] { prior }, new[] { IntPtr.Zero }, new[] { (nint)3 });
            else ((delegate* unmanaged[Stdcall]<int, uint, uint, nint, nint, void>)
                OpenGLBindingTable.For(typeof(GL)).Read("glBindBufferRangeEXT"))((int)BufferRangeTarget.TransformFeedbackBuffer, 0, (uint)reserved, 0, 3);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.TransformFeedbackBuffer, 0, out int id, out _, out nint size));
            Assert.Equal(prior, id); Assert.Equal((nint)16, size);
            var entry = RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Buffer, reserved)!;
            Assert.False(entry.IsRealized); Assert.False(entry.IsUsable);
        }
        finally { OpenGLFeedbackBindings.Bind(cache, 0); feedback.Dispose(); GL.DeleteBuffer(reserved); GL.DeleteBuffer(prior); fixture.CleanupGpuResources(); }
    }

    /// <summary>Saved-original element binding establishes the same reserved buffer's guaranteed defaults.</summary>
    [Fact]
    public void ManagedElementBindingRealizesOriginalBuffer()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var array = NativeResourceSetup.CreateVertexArray();
        int id = GL.GenBuffer();
        try
        {
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(RHIResourceManagerSystem.CurrentManager!.FindResource(GpuResourceKind.Buffer, id));
            OpenGLVertexSelection.Bind(OpenGlStateCache.Current, array.VertexArrayId);
            using var queries = new BufferDescriptorCapture();
            OpenGLBufferBindings.BindBuffer(OpenGlStateCache.Current, BufferTarget.ElementArrayBuffer, id);
            Assert.True(entry.IsRealized); Assert.True(entry.IsUsable);
            Assert.Equal(new GpuBufferMetadata(0, BufferUsage.StaticDraw), entry.Metadata);
            Assert.Equal(0, queries.DescriptorReads); Assert.Equal(0, queries.IdentityReads);
        }
        finally { GL.DeleteBuffer(id); array.Dispose(); fixture.CleanupGpuResources(); }
    }

    /// <summary>The separate EXT vertex-bind alias requires a generated buffer and validates its update.</summary>
    [Fact]
    public void ExtSeparateBindingDoesNotInventNongeneratedBuffer()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var aliases = new OptionalAliases();
        GL.CreateVertexArrays(1, out int vao);
        int reserved = GL.GenBuffer();
        try
        {
            var call = (delegate* unmanaged[Stdcall]<uint, uint, uint, nint, int, void>)
                OpenGLBindingTable.For(typeof(GL)).Read("glVertexArrayBindVertexBufferEXT");
            call((uint)vao, 0, 1900000099, 0, 16);
            Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            var manager = RHIResourceManagerSystem.CurrentManager!;
            Assert.Null(manager.FindResource(GpuResourceKind.Buffer, 1900000099));
            call((uint)vao, 0, (uint)reserved, 0, -1);
            Assert.Equal(ErrorCode.InvalidValue, GL.GetError());
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, reserved));
            Assert.False(entry.IsRealized);
            GL.NamedBufferData(reserved, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            call((uint)vao, 0, (uint)reserved, 0, 16);
            Assert.False(entry.IsUsable);
            Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Buffer, reserved, new GpuBufferMetadataReader()));
            Assert.Equal((nint)64, entry.Metadata.Capacity);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { GL.DeleteVertexArray(vao); GL.DeleteBuffer(reserved); fixture.CleanupGpuResources(); }
    }

    /// <summary>EXT offset captures a rounded explicit range and the exposed Base alias retains native whole-buffer semantics.</summary>
    [Fact]
    public void ExtOffsetRangeMatchesNativeStateAcrossStorageReplacement()
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        using var aliases = new OptionalAliases();
        using var feedback = RHICommandContext.Current.CreateTransformFeedback(new()).RequireResource();
        var cache = OpenGlStateCache.Current;
        OpenGLFeedbackBindings.Bind(cache, feedback.TransformFeedbackId);
        GL.CreateBuffers(1, out int id);
        try
        {
            GL.NamedBufferData(id, 67, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            var table = OpenGLBindingTable.For(typeof(GL));
            ((delegate* unmanaged[Stdcall]<int, uint, uint, nint, void>)table.Read("glBindBufferOffsetEXT"))
                ((int)BufferRangeTarget.TransformFeedbackBuffer, 0, (uint)id, 16);
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.True(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.TransformFeedbackBuffer, 0, out int known, out nint offset, out nint size));
            GL.GetInteger64((GetIndexedPName)All.TransformFeedbackBufferSize, 0, out long nativeSize);
            Assert.Equal(id, known); Assert.Equal((nint)16, offset); Assert.Equal((nint)48, size); Assert.Equal(48, nativeSize);
            GL.NamedBufferData(id, 128, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.GetInteger64((GetIndexedPName)All.TransformFeedbackBufferSize, 0, out nativeSize);
            Assert.Equal(48, nativeSize);
            ((delegate* unmanaged[Stdcall]<int, uint, uint, void>)table.Read("glBindBufferBaseEXT"))
                ((int)BufferRangeTarget.TransformFeedbackBuffer, 0, (uint)id);
            GL.GetInteger64((GetIndexedPName)All.TransformFeedbackBufferSize, 0, out nativeSize);
            Assert.True(nativeSize == 0 || nativeSize == 128);
            GL.NamedBufferData(id, 256, IntPtr.Zero, BufferUsageHint.DynamicDraw);
            GL.GetInteger64((GetIndexedPName)All.TransformFeedbackBufferSize, 0, out nativeSize);
            Assert.True(nativeSize == 0 || nativeSize == 256);
            Assert.False(cache.TryGetCachedIndexedBuffer(BufferRangeTarget.TransformFeedbackBuffer, 0, out _, out _, out _));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { OpenGLFeedbackBindings.Bind(cache, 0); feedback.Dispose(); GL.DeleteBuffer(id); fixture.CleanupGpuResources(); }
    }

    /// <summary>Uncached limits cannot turn a previously configured native buffer back into a fabricated empty default.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreCapabilityRealizationRetainsUncertaintyThroughNamedAllocation(bool rejected)
    {
        fixture.EnsureContextValid();
        object owner = new();
        int buffer = 0, vao = 0;
        try
        {
            RenderContextRegistry.RegisterCurrent(owner, static _ => true);
            Assert.False(GpuSupport.HasCachedCapabilitiesForCurrentContext);
            GL.CreateVertexArrays(1, out vao); GL.BindVertexArray(vao);
            buffer = GL.GenBuffer();
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var entry = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, buffer));
            if (rejected) GL.BindBuffer((BufferTarget)(-1), buffer);
            else GL.BindVertexBuffer(0, buffer, IntPtr.Zero, 16);
            if (rejected) Assert.Equal(ErrorCode.InvalidEnum, GL.GetError());
            GL.NamedBufferData(buffer, 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
            if (rejected) Assert.Equal(ErrorCode.InvalidOperation, GL.GetError());
            GL.BindBuffer(BufferTarget.ArrayBuffer, buffer);
            Assert.True(entry.IsRealized);
            if (rejected)
            {
                Assert.True(entry.IsUsable); Assert.Equal(new GpuBufferMetadata(0, BufferUsage.StaticDraw), entry.Metadata);
            }
            else
            {
                Assert.False(entry.IsUsable); Assert.NotEqual(GpuMetadataChange.None, entry.PendingChange);
                Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, buffer, new ForbiddenReader()));
                GpuSupport.EnsureCurrentContext();
                Assert.Same(entry, manager.RecoverResource(GpuResourceKind.Buffer, buffer, new GpuBufferMetadataReader()));
                Assert.Equal((nint)64, entry.Metadata.Capacity);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.DeleteVertexArray(vao); GL.DeleteBuffer(buffer);
            RenderContextRegistry.RegisterCurrent(fixture, static _ => true);
            fixture.CleanupGpuResources();
        }
    }
    #endregion

    #region Private
    /// <summary>Uses deterministic real-core stand-ins only for optional entrypoints absent from this driver.</summary>
    private sealed class OptionalAliases : IDisposable
    {
        private readonly NativeDriverCapture native = new();
        #region Internal API
        /// <summary>Provides the specified EXT effects to generated callbacks without claiming native extension support.</summary>
        internal OptionalAliases()
        {
            var table = OpenGLBindingTable.For(typeof(GL));
            if (table.Read("glBindBufferRangeEXT") == 0)
            {
                nint original = native.Original("glBindBufferRange");
                native.Add("glBindBufferRangeEXT", new RangeCall((target, index, buffer, offset, size) =>
                    ((delegate* unmanaged[Stdcall]<int, uint, uint, nint, nint, void>)original)(target, index, buffer, offset, size)));
            }
            if (table.Read("glBindBufferBaseEXT") == 0)
            {
                nint original = native.Original("glBindBufferBase");
                native.Add("glBindBufferBaseEXT", new BaseCall((target, index, buffer) =>
                    ((delegate* unmanaged[Stdcall]<int, uint, uint, void>)original)(target, index, buffer)));
            }
            if (table.Read("glBindBufferOffsetEXT") == 0)
            {
                nint original = native.Original("glBindBufferRange");
                native.Add("glBindBufferOffsetEXT", new OffsetCall((target, index, buffer, offset) =>
                {
                    GL.GetNamedBufferParameter((int)buffer, BufferParameterName.BufferSize, out long capacity);
                    nint size = (nint)capacity - offset; size -= size % 4;
                    ((delegate* unmanaged[Stdcall]<int, uint, uint, nint, nint, void>)original)(target, index, buffer, offset, size);
                }));
            }
            if (table.Read("glVertexArrayBindVertexBufferEXT") == 0)
            {
                nint original = native.Original("glVertexArrayVertexBuffer");
                native.Add("glVertexArrayBindVertexBufferEXT", new VertexCall((array, index, buffer, offset, stride) =>
                    ((delegate* unmanaged[Stdcall]<uint, uint, uint, nint, int, void>)original)(array, index, buffer, offset, stride)));
            }
            native.Install();
        }
        #endregion
        #region Public API
        /// <inheritdoc />
        public void Dispose() => native.Dispose();
        #endregion
        #region Private
        /// <summary>Matches native range binding.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void RangeCall(int target, uint index, uint buffer, nint offset, nint size);
        /// <summary>Matches native base binding.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void BaseCall(int target, uint index, uint buffer);
        /// <summary>Matches native offset binding.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void OffsetCall(int target, uint index, uint buffer, nint offset);
        /// <summary>Matches separate named vertex binding.</summary>
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void VertexCall(uint array, uint index, uint buffer, nint offset, int stride);
        #endregion
    }

    /// <summary>Rejects any ordinary attempt to rediscover a deliberately unavailable original entry.</summary>
    private sealed class ForbiddenReader : IGpuResourceMetadataReader<GpuBufferMetadata>
    {
        /// <inheritdoc />
        public bool IsResource(nint id) => throw new InvalidOperationException("Unexpected identity query.");
        /// <inheritdoc />
        public bool TryRead(nint id, out GpuBufferMetadata metadata) => throw new InvalidOperationException("Unexpected recovery.");
    }
    #endregion
}
