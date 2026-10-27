using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using HarmonyLib;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Uniforms;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks retained logical ownership across named bindings, numeric slots and executable replacement.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class UniformLifetimeSharingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Manual byte edits with MarkDirty publish fresh bytes without overwriting the earlier range.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectWritesPublishNewSnapshot(bool mapped)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped);
        using var block = new DirectWriteBlock();
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            block.Write(3f);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            block.Write(7f);
            Assert.True(block.IsDirty);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(14, block.Capture()));
            Assert.Equal(3f, ReadSlot(13));
            Assert.Equal(7f, ReadSlot(14));
            Assert.False(block.IsDirty);
#if DEBUG
            Assert.Equal(2, ring.AllocationsWritten);
#endif
            ring.EndFrame();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Ring exhaustion preserves the last publication and dirty work until the next frame can retry.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientExhaustionPreservesPublicationAndRetries(bool mapped)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(16, 3, mapped);
        using var block = new DirectWriteBlock();
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            block.Write(3f);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            block.Write(7f);
            Assert.Throws<InvalidOperationException>(() => VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            Assert.True(block.IsDirty);
            Assert.Equal(3f, ReadSlot(13));
#if DEBUG
            Assert.Equal(1, ring.AllocationsWritten);
#endif
            ring.EndFrame();
            ring.BeginFrame(1);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            Assert.Equal(7f, ReadSlot(13));
            Assert.False(block.IsDirty);
            ring.EndFrame();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Independent snapshots explicitly restore their retained ranges without copying CPU contents.</summary>
    [Fact]
    public void CapturedResolveVersionsRestorePersistentContentsWithoutCopying()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var outer = programs.Create<LumOnWorldProbeClipmapResolveShaderProgram>();
        var inner = programs.Create<LumOnWorldProbeClipmapResolveShaderProgram>();
        outer.AtlasSize = new(64, 32);
        inner.AtlasSize = new(128, 64);
        using var ring = new GpuUniformRingBuffer(65536, 3, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            var outerSnapshot = ResolveSnapshot(outer);
            var innerSnapshot = ResolveSnapshot(inner);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, outerSnapshot));
            Assert.Equal(64f, ReadSlot(GpuBindingRegistry.Ubo.Object));
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, innerSnapshot));
            Assert.Equal(128f, ReadSlot(GpuBindingRegistry.Ubo.Object));
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, outerSnapshot));
            Assert.Equal(64f, ReadSlot(GpuBindingRegistry.Ubo.Object));
#if DEBUG
            Assert.Equal(2, ring.PersistentStorage.AllocationsWritten);
            Assert.Equal(3, ring.PersistentStorage.Bindings);
#endif
            ring.EndFrame();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>The migrated resolve owner keeps its parameters on reload and releases retained storage on final disposal.</summary>
    [Fact]
    public void OwnedResolveParametersRetireOnlyWithShaderOwner()
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var shader = programs.Create<LumOnWorldProbeClipmapResolveShaderProgram>();
        using var ring = new GpuUniformRingBuffer(65536, 3, false);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            shader.AtlasSize = new(64, 32);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, ResolveSnapshot(shader)));
#if DEBUG
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
#endif
            shader.InvalidateAssets();
            shader.EnsureReady();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, ResolveSnapshot(shader)));
#if DEBUG
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
#endif
            Assert.Equal(0, ring.PersistentStorage.PendingBytes);
            shader.Dispose();
            Assert.True(ring.PersistentStorage.PendingBytes > 0);
            Assert.Throws<ObjectDisposedException>(() => shader.AtlasSize = new(32, 16));
            ring.EndFrame();
            GL.Finish();
            ring.PersistentStorage.Collect();
            Assert.Equal(0, ring.PersistentStorage.PendingBytes);
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>An interrupted upload leaves dirty work pending and the previous complete range unchanged.</summary>
    [Theory]
    [InlineData(false, UniformBufferUsage.MultiFrame)]
    [InlineData(true, UniformBufferUsage.MultiFrame)]
    [InlineData(false, UniformBufferUsage.SingleFrame)]
    [InlineData(true, UniformBufferUsage.SingleFrame)]
    public void UploadInterruptionPreservesDirtyWorkAndRetries(bool mapped, UniformBufferUsage usage)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped);
        using var block = new PackedUniformBuffer(16, usage);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            byte[] bytes = new byte[16];
            BitConverter.TryWriteBytes(bytes, 1f);
            block.SetBytes(bytes);
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            BitConverter.TryWriteBytes(bytes, 2f);
            block.SetBytes(bytes);
            // Interrupt the upload itself; unrelated ambient GL errors are not command results.
            var fault = new Harmony("tests.uniform-upload-interruption." + Guid.NewGuid());
            var upload = usage == UniformBufferUsage.MultiFrame
                ? AccessTools.Method(typeof(UniformStoragePage), "Write")
                : AccessTools.Method(typeof(GpuUniformRingBuffer), nameof(GpuUniformRingBuffer.AllocateAndWrite));
            try
            {
                fault.Patch(upload, prefix: new HarmonyMethod(typeof(UniformLifetimeSharingTests), nameof(InterruptUpload)));
                Assert.Throws<InvalidOperationException>(() => RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            }
            finally { fault.UnpatchAll(fault.Id); }
            Assert.True(block.IsDirty);
            Assert.Equal(1f, ReadSlot(13));
#if DEBUG
            Assert.Equal(1, usage == UniformBufferUsage.MultiFrame ? ring.PersistentStorage.AllocationsWritten : ring.AllocationsWritten);
#endif
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            Assert.False(block.IsDirty);
            Assert.Equal(2f, ReadSlot(13));
#if DEBUG
            Assert.Equal(2, usage == UniformBufferUsage.MultiFrame ? ring.PersistentStorage.AllocationsWritten : ring.AllocationsWritten);
#endif
            ring.EndFrame();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }

    /// <summary>Borrowing programs cannot retire shared publication; compatible reload and slot changes need no upload.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SharedPersistentBlockSurvivesReloadAndBorrowerDisposal(bool mapped)
    {
        EnsureContextValid();
        using var programs = new ComponentShaderPrograms();
        var first = programs.Create<LumOnUpsampleShaderProgram>();
        var second = programs.Create<LumOnUpsampleShaderProgram>();
        using var block = new PackedUniformBuffer(32, UniformBufferUsage.MultiFrame);
        byte[] bytes = new byte[32];
        BitConverter.TryWriteBytes(bytes, 0.25f);
        block.SetBytes(bytes);
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped);
        ring.BeginFrame(0);
        GpuUniformRingSystem.SetCurrent(ring);
        try
        {
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, block.Capture()));
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, block.Capture()));
            Assert.Equal(0.25f, ReadSlot(14));
            ring.EndFrame();
            ring.BeginFrame(1);
            first.InvalidateAssets();
            first.EnsureReady();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, block.Capture()));
            first.Dispose();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(GpuBindingRegistry.Ubo.Object, block.Capture()));
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(13, block.Capture()));
            Assert.Equal(0.25f, ReadSlot(13));
#if DEBUG
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
            Assert.Equal(32, ring.PersistentStorage.BytesWritten);
            Assert.Equal(5, ring.PersistentStorage.Bindings);
#endif
#if DEBUG
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
#endif
            // CPU packing disposal deliberately resets publication; unlike logical disposal it remains publishable.
            block.Dispose();
            Assert.True(VanillaGraphicsExpanded.Rendering.RHI.RHICommandContext.Current.BindUniformSnapshot(14, block.Capture()));
            Assert.Equal(0.25f, ReadSlot(14));
#if DEBUG
            Assert.Equal(2, ring.PersistentStorage.AllocationsWritten);
#endif
            ring.EndFrame();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    #endregion

    #region Private
    /// <summary>Reports an interrupted upload before publication can commit the new CPU revision.</summary>
    private static void InterruptUpload() => throw new InvalidOperationException("Fixture upload interruption.");

    /// <summary>Extracts the generated resolve block snapshot for an explicitly qualified engine integration slot.</summary>
    private static RHIUniformSnapshot ResolveSnapshot(LumOnWorldProbeClipmapResolveShaderProgram shader)
        => shader.CaptureBindings().Uniforms.Single(value => value.Identity == VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(
            VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.UniformBlock, LumOnWorldProbeResolveParamsUbo.BlockName)).Snapshot!;

    /// <summary>Exercises the supported direct-write path without typed packing setters.</summary>
    private sealed class DirectWriteBlock() : CpuUniformBuffer(16)
    {
        #region Public API
        /// <summary>Writes CPU bytes directly and explicitly advances the publication revision.</summary>
        public void Write(float value)
        {
            BitConverter.TryWriteBytes(DataWritable, value);
            MarkDirty();
        }
        #endregion
    }


    /// <summary>Reads the driver's actual selected range, preserving the generic buffer binding.</summary>
    private static float ReadSlot(int slot)
    {
        GL.GetInteger((GetIndexedPName)All.UniformBufferBinding, slot, out int buffer);
        GL.GetInteger((GetIndexedPName)All.UniformBufferStart, slot, out int offset);
        int previous = GL.GetInteger(GetPName.UniformBufferBinding);
        GL.BindBuffer(BufferTarget.UniformBuffer, buffer);
        float[] result = new float[1];
        GL.GetBufferSubData(BufferTarget.UniformBuffer, (IntPtr)offset, sizeof(float), result);
        GL.BindBuffer(BufferTarget.UniformBuffer, previous);
        return result[0];
    }
    #endregion
}
