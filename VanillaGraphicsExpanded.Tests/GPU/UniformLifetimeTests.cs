using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Uniforms;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Observes immutable uniform versions and their last-use retirement on real storage backends.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class UniformLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Unchanged cross-frame publications retain one range while changed bytes preserve earlier GPU snapshots.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void PersistentVersionsReuseAcrossFramesAndPreserveSnapshots(bool mapped, bool coherent)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped, coherent);
        using var publication = new UniformPublication(UniformBufferUsage.MultiFrame);
        ring.BeginFrame(0);
        var original = Publish(publication, ring, 123, 1);
        for (int frame = 1; frame < 5; frame++)
        {
            ring.EndFrame();
            ring.BeginFrame(frame);
            var reused = Publish(publication, ring, 123, 1);
            Assert.Same(original.Retained, reused.Retained);
            Assert.Equal(123u, Observe(reused));
        }
        var changed = Publish(publication, ring, 456, 2);
        Assert.NotSame(original.Retained, changed.Retained);
        Assert.Equal(123u, Observe(original));
        Assert.Equal(456u, Observe(changed));
        Assert.Equal(2, ring.PersistentStorage.AllocationsWritten);
        Assert.Equal(32, ring.PersistentStorage.BytesWritten);
        Assert.Equal(6, ring.PersistentStorage.Bindings);
        ring.EndFrame();
    }

    /// <summary>A later unchanged bind extends retirement beyond the original upload's completed frame.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void RetirementFollowsLatestUseAndNeverReclaimsCurrentVersions(bool mapped, bool coherent)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped, coherent);
        var pool = ring.PersistentStorage;
        var original = pool.Allocate(Bytes(17));
        pool.MarkUsed(original);
        pool.SealFrame();
        GL.Finish();
        pool.Collect();
        Assert.Same(original, original.Page.Slots[original.Slot]);
        Assert.True(pool.IsCurrent(original));
        // The old fence has completed, but the new last-use interval has not even been sealed.
        pool.MarkUsed(original);
        pool.Release(original);
        pool.Collect();
        Assert.Same(original, original.Page.Slots[original.Slot]);
        Assert.True(pool.PendingBytes > 0);
        var other = pool.Allocate(Bytes(29));
        Assert.NotEqual(original.Offset, other.Offset);
        pool.SealFrame();
        GL.Finish();
        pool.Collect();
        Assert.Null(original.Page.Slots[original.Slot]);
        var recycled = pool.Allocate(Bytes(41));
        Assert.Equal(original.Offset, recycled.Offset);
        Assert.True(recycled.Generation > original.Generation);
        Assert.False(pool.IsCurrent(original));
        Assert.True(pool.IsCurrent(other));
        Assert.True(pool.IsCurrent(recycled));
        Assert.Equal(0, pool.PendingBytes);
    }

    /// <summary>Unpublished candidates release their reservation without replacing the successful logical publication.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailedCandidateRetainsSuccessfulVersionAndCanRetry(bool mapped)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped);
        using var publication = new UniformPublication(UniformBufferUsage.MultiFrame);
        ring.BeginFrame(0);
        var original = Publish(publication, ring, 7, 1);
        var rejected = publication.Prepare(ring, Bytes(8), 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => rejected.Buffer.BindRange(-1, rejected.Offset, rejected.Size));
        publication.Abort(rejected);
        Assert.Null(rejected.Retained!.Page.Slots[rejected.Retained.Slot]);
        Assert.Same(original.Retained, publication.Prepare(ring, Bytes(7), 1).Retained);
        var retry = Publish(publication, ring, 8, 2);
        Assert.Equal(8u, Observe(retry));
        publication.Dispose();
        Assert.Throws<ObjectDisposedException>(() => publication.Prepare(ring, Bytes(9), 3));
        ring.EndFrame();
    }

    /// <summary>Transient usage preserves same-frame reuse only for frame lifetime and invalidates repeated frame indices.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TransientLifetimeDistinguishesIndependentDrawsAndFrameEpochs(bool mapped)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped);
        using var frame = new UniformPublication(UniformBufferUsage.SingleFrame);
        using var draw = new UniformPublication(UniformBufferUsage.SingleDraw);
        ring.BeginFrame(0);
        var first = Publish(frame, ring, 3, 1);
        Assert.Equal(first.Offset, Publish(frame, ring, 3, 1).Offset);
        var single = Publish(draw, ring, 4, 1);
        Assert.NotEqual(single.Offset, Publish(draw, ring, 4, 1).Offset);
        Assert.Equal(3, ring.AllocationsWritten);
        var changed = Publish(frame, ring, 5, 2);
        Assert.Equal(3u, Observe(first));
        Assert.Equal(5u, Observe(changed));
        // Re-entering an open frame must seal and retire pending writes before resetting its page.
        ring.BeginFrame(0);
        Publish(frame, ring, 5, 2);
        Assert.Equal(5, ring.AllocationsWritten);
        ring.EndFrame();
    }

    /// <summary>Capacity failure does not reclaim owned slots; released unused slots recycle with a new generation.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(true, false)]
    public void PoolBudgetAndAlignmentPreserveOwnedRanges(bool mapped, bool coherent)
    {
        EnsureContextValid();
        using var ring = new GpuUniformRingBuffer(65536, 3, mapped, coherent);
        int alignment = ring.UniformBufferOffsetAlignmentBytes;
        using var pool = new PersistentUniformStorage(alignment, ring.UsesPersistentMapping, coherent, 65536);
        var versions = new List<UniformStorageVersion>();
        for (int slot = 0; slot < 65536 / alignment; slot++)
        {
            var version = pool.Allocate(Bytes((uint)slot));
            Assert.Equal(0, version.Offset % alignment);
            versions.Add(version);
        }
        Assert.Throws<InvalidOperationException>(() => pool.Allocate(Bytes(99)));
        Assert.All(versions, version => Assert.True(pool.IsCurrent(version)));
        pool.Release(versions[0]);
        var replacement = pool.Allocate(Bytes(99));
        Assert.Equal(versions[0].Offset, replacement.Offset);
        Assert.True(replacement.Generation > versions[0].Generation);
        Assert.Equal(65536, pool.ResidentBytes);
        Assert.Equal(0, pool.PendingBytes);
    }
    #endregion

    #region Private
    /// <summary>Publishes through the logical candidate/commit contract using the native range-binding owner.</summary>
    private static UniformPublication.Candidate Publish(UniformPublication owner, GpuUniformRingBuffer ring, uint value, ulong revision)
    {
        var candidate = owner.Prepare(ring, Bytes(value), revision);
        candidate.Buffer.BindRange(0, candidate.Offset, candidate.Size);
        owner.Commit(ring, candidate, revision);
        return candidate;
    }

    /// <summary>Builds one complete std140 vector snapshot.</summary>
    private static byte[] Bytes(uint value)
    {
        byte[] data = new byte[16];
        BitConverter.TryWriteBytes(data, value);
        return data;
    }

    /// <summary>Dispatches the existing packaged UBO fixture and reads its actual shader result.</summary>
    private static uint Observe(UniformPublication.Candidate candidate)
    {
        int shader = BuiltShaderFixture.Load("tests/GpuUniformRingBufferIntegrationTests_1.csh", ShaderType.ComputeShader);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, shader);
            TestShaderInterfaces.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.NotEqual(0, linked);
            var layout = TestShaderInterfaces.BuildLayout(program);
            layout.RegisterUniformBlockBinding("TestParams", 0, required: true);
            layout.ApplyContract(program);
            using var output = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32ui,
                TextureFilterMode.Nearest, textureTarget: TextureTarget.Texture3D);
            GL.BindImageTexture(0, output.TextureId, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
            candidate.Buffer.BindRange(0, candidate.Offset, candidate.Size);
            GL.UseProgram(program);
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureUpdateBarrierBit);
            uint[] result = new uint[4];
            GL.BindTexture(TextureTarget.Texture3D, output.TextureId);
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, result);
            return result[0];
        }
        finally
        {
            GL.UseProgram(0);
            StateCache.Current.Invalidate(EPipelineState.Program);
            TestShaderInterfaces.DeleteProgram(program);
            TestShaderInterfaces.DeleteShader(shader);
        }
    }
    #endregion
}
