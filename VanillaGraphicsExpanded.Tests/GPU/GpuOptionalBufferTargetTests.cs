using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks optional-target uncertainty without native capability or selector discovery.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed unsafe class GpuOptionalBufferTargetTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Missing optional-target admission withdraws only possible effects and preserves known scalar rejection.</summary>
    [Theory]
    [InlineData(false, 0)] [InlineData(true, 0)]
    [InlineData(false, 1)] [InlineData(true, 1)]
    [InlineData(false, 2)] [InlineData(true, 2)]
    [InlineData(false, 3)] [InlineData(true, 3)]
    [InlineData(false, 4)] [InlineData(true, 4)]
    public void UncachedParameterTargetDoesNotRetainStaleFacts(bool modern, int operation)
    {
        fixture.EnsureContextValid(); fixture.InitializeResourceDisposal();
        var initialized = HarmonyLib.AccessTools.Field(typeof(GpuSupport), "isInitialized");
        object? previousInitialized = initialized.GetValue(null);
        nint mapped = 0;
        int first = 0, second = 0;
        try
        {
            GL.CreateBuffers(1, out first); GL.NamedBufferData(first, 128, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.CreateBuffers(1, out second); GL.NamedBufferData(second, 128, IntPtr.Zero, BufferUsageHint.StaticDraw);
            GL.BindBuffer(BufferTarget.ArrayBuffer, first);
            var manager = RHIResourceManagerSystem.CurrentManager!;
            var old = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, first));
            var incoming = Assert.IsType<GpuResourceMetadataEntry<GpuBufferMetadata>>(manager.FindResource(GpuResourceKind.Buffer, second));
            var cache = OpenGlStateCache.Current;
            // The deterministic native decorator uses ArrayBuffer as the optional target's lane.
            // This qualifies uncertainty delivery even on hardware without the optional extension.
            cache.EstablishBufferBinding(BufferTarget.ParameterBuffer, first);
            if (operation == 3) { mapped = GL.MapNamedBuffer(first, BufferAccess.ReadWrite); Assert.NotEqual(0, mapped); }
            using var native = new NativeDriverCapture();
            nint bind = native.Original("glBindBuffer"), data = native.Original("glBufferData");
            nint map = native.Original("glMapBuffer"), unmap = native.Original("glUnmapBuffer"), query = native.Original("glGetIntegerv");
            int selectorReads = 0;
            native.Add("glBindBuffer", new Bind((target, buffer) =>
                ((delegate* unmanaged[Stdcall]<int, uint, void>)bind)(Lane(target), buffer)));
            native.Add("glBufferData", new Data((target, size, pointer, usage) =>
                ((delegate* unmanaged[Stdcall]<int, nint, nint, int, void>)data)(Lane(target), size, pointer, usage)));
            native.Add("glMapBuffer", new Map((target, access) =>
                ((delegate* unmanaged[Stdcall]<int, int, nint>)map)(Lane(target), access)));
            native.Add("glUnmapBuffer", new Unmap(target =>
                ((delegate* unmanaged[Stdcall]<int, byte>)unmap)(Lane(target))));
            native.Add("glGetIntegerv", new Query((field, value) =>
            { selectorReads++; ((delegate* unmanaged[Stdcall]<int, int*, void>)query)(field, value); }));
            native.Install();
            initialized.SetValue(null, false);
            if (modern)
            {
                if (operation == 0) OpenTK.Graphics.OpenGL4.GL.BindBuffer(OpenTK.Graphics.OpenGL4.BufferTarget.ParameterBuffer, second);
                else if (operation is 1 or 4) OpenTK.Graphics.OpenGL4.GL.BufferData(OpenTK.Graphics.OpenGL4.BufferTarget.ParameterBuffer,
                    operation == 4 ? -1 : 64, IntPtr.Zero, OpenTK.Graphics.OpenGL4.BufferUsageHint.StaticDraw);
                else if (operation == 2) mapped = OpenTK.Graphics.OpenGL4.GL.MapBuffer(OpenTK.Graphics.OpenGL4.BufferTarget.ParameterBuffer, OpenTK.Graphics.OpenGL4.BufferAccess.ReadWrite);
                else { Assert.True(OpenTK.Graphics.OpenGL4.GL.UnmapBuffer(OpenTK.Graphics.OpenGL4.BufferTarget.ParameterBuffer)); mapped = 0; }
            }
            else
            {
                if (operation == 0) GL.BindBuffer(BufferTarget.ParameterBuffer, second);
                else if (operation is 1 or 4) GL.BufferData(BufferTarget.ParameterBuffer, operation == 4 ? -1 : 64, IntPtr.Zero, BufferUsageHint.StaticDraw);
                else if (operation == 2) mapped = GL.MapBuffer(BufferTarget.ParameterBuffer, BufferAccess.ReadWrite);
                else { Assert.True(GL.UnmapBuffer(BufferTarget.ParameterBuffer)); mapped = 0; }
            }
            Assert.False(GpuSupport.HasCachedCapabilitiesForCurrentContext);
            Assert.Equal(0, selectorReads);
            if (operation == 0)
            {
                Assert.False(cache.TryGetCachedBoundBuffer(BufferTarget.ParameterBuffer, out _));
                Assert.False(old.IsUsable); Assert.False(incoming.IsUsable);
                // A later allocation has no certified selector; neither possible target may expose stale storage.
                GL.BufferData(BufferTarget.ParameterBuffer, 256, IntPtr.Zero, BufferUsageHint.StaticDraw);
                Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, first, new GpuBufferMetadataReader()));
                Assert.Null(manager.AcquireResource(GpuResourceKind.Buffer, second, new GpuBufferMetadataReader()));
            }
            else
            {
                Assert.True(incoming.IsUsable);
                Assert.Equal(operation != 1, old.IsUsable);
                if (operation is 2 or 3) Assert.False(cache.TryGetBufferMapping(first, out _, out _));
                if (operation == 2) Assert.NotEqual(0, mapped);
            }
            Assert.Equal(operation == 4 ? ErrorCode.InvalidValue : ErrorCode.NoError, GL.GetError());
            Assert.Equal(0, selectorReads);
            Assert.True(cache.TryGetCachedBoundBuffer(BufferTarget.ArrayBuffer, out int unrelated));
            Assert.Equal(first, unrelated);
        }
        finally
        {
            initialized.SetValue(null, previousInitialized);
            if (mapped != 0) GL.UnmapNamedBuffer(first);
            GL.DeleteBuffer(first); GL.DeleteBuffer(second);
            fixture.CleanupGpuResources();
        }
    }
    #endregion

    #region Private
    /// <summary>Provides deterministic optional-target effects through a supported native buffer lane.</summary>
    private static int Lane(int target) => target == (int)BufferTarget.ParameterBuffer ? (int)BufferTarget.ArrayBuffer : target;
    /// <summary>Matches native buffer selection.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Bind(int target, uint buffer);
    /// <summary>Matches native allocation input widths.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Data(int target, nint size, nint pointer, int usage);
    /// <summary>Matches native mapping output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate nint Map(int target, int access);
    /// <summary>Matches native unmapping result.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate byte Unmap(int target);
    /// <summary>Matches native scalar selector output.</summary>
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate void Query(int field, int* value);
    #endregion
}
