using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Uniforms;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Observes immutable transient uniform versions and allocation epochs on real storage backends.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class UniformLifetimeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
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
