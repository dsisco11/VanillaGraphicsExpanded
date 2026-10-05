using OpenTK.Graphics.OpenGL;
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
    /// <summary>Nested persistent owners restore their distinct retained ranges and record the restoring bind.</summary>
    [Fact]
    public void NestedResolveScopesRestorePersistentContentsWithoutCopying()
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
            using (outer.UseScope())
            {
                Assert.Equal(64f, ReadSlot(GpuBindingRegistry.Ubo.Object));
                using (inner.UseScope())
                    Assert.Equal(128f, ReadSlot(GpuBindingRegistry.Ubo.Object));
                Assert.Equal(64f, ReadSlot(GpuBindingRegistry.Ubo.Object));
            }
            Assert.Equal(2, ring.PersistentStorage.AllocationsWritten);
            Assert.Equal(3, ring.PersistentStorage.Bindings);
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
            using (shader.UseScope()) { }
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
            shader.InvalidateAssets();
            shader.EnsureReady();
            using (shader.UseScope()) { }
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
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

    /// <summary>A native upload error leaves dirty work pending and the previous complete range unchanged.</summary>
    [Theory]
    [InlineData(false, UniformBufferUsage.MultiFrame)]
    [InlineData(true, UniformBufferUsage.MultiFrame)]
    [InlineData(false, UniformBufferUsage.SingleFrame)]
    [InlineData(true, UniformBufferUsage.SingleFrame)]
    public void NativeUploadFailurePreservesDirtyWorkAndRetries(bool mapped, UniformBufferUsage usage)
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
            Assert.True(block.TryBindToSlot(13));
            BitConverter.TryWriteBytes(bytes, 2f);
            block.SetBytes(bytes);
            // Inject a driver error before the next native upload's success check.
            GL.Enable((EnableCap)(-1));
            Assert.Throws<InvalidOperationException>(() => block.TryBindToSlot(13));
            Assert.True(block.IsDirty);
            Assert.Equal(1f, ReadSlot(13));
            Assert.Equal(1, usage == UniformBufferUsage.MultiFrame ? ring.PersistentStorage.AllocationsWritten : ring.AllocationsWritten);
            Assert.True(block.TryBindToSlot(13));
            Assert.False(block.IsDirty);
            Assert.Equal(2f, ReadSlot(13));
            Assert.Equal(2, usage == UniformBufferUsage.MultiFrame ? ring.PersistentStorage.AllocationsWritten : ring.AllocationsWritten);
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
            Assert.True(block.TryBindTo(first, LumOnUpsampleParamsUbo.BlockName, "Shared.Persistent"));
            Assert.True(block.TryBindTo(second, LumOnUpsampleParamsUbo.BlockName, "Shared.Persistent"));
            Assert.Equal(0.25f, ReadSlot(14));
            ring.EndFrame();
            ring.BeginFrame(1);
            first.InvalidateAssets();
            first.EnsureReady();
            Assert.True(block.TryBindTo(first, LumOnUpsampleParamsUbo.BlockName, "Shared.Persistent"));
            first.Dispose();
            Assert.True(block.TryBindTo(second, LumOnUpsampleParamsUbo.BlockName, "Shared.Persistent"));
            Assert.True(block.TryBindToSlot(13));
            Assert.Equal(0.25f, ReadSlot(13));
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
            Assert.Equal(32, ring.PersistentStorage.BytesWritten);
            Assert.Equal(5, ring.PersistentStorage.Bindings);
            using var incompatible = new PackedUniformBuffer(16, UniformBufferUsage.MultiFrame);
            Assert.Throws<InvalidOperationException>(() => incompatible.TryBindTo(second, LumOnUpsampleParamsUbo.BlockName, "Wrong.Layout"));
            Assert.Equal(1, ring.PersistentStorage.AllocationsWritten);
            // CPU packing disposal deliberately resets publication; unlike logical disposal it remains publishable.
            block.Dispose();
            Assert.True(block.TryBindToSlot(14));
            Assert.Equal(0.25f, ReadSlot(14));
            Assert.Equal(2, ring.PersistentStorage.AllocationsWritten);
            ring.EndFrame();
        }
        finally { GpuUniformRingSystem.ClearCurrent(); }
    }
    #endregion

    #region Private
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
