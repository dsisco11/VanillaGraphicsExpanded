using System;
using System.Buffers.Binary;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks fixture-owned retirement before mapped uniform bytes are reused or released.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TestUniformRingRetirementTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Submission lifetime
    /// <summary>Distinct queued outputs retain their uniform values across resets without intermediate readbacks.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void SubmittedValuesSurviveBoundaryReuse(int boundary)
    {
        EnsureContextValid();
        int shader = BuiltShaderFixture.Load("tests/GpuUniformRingBufferIntegrationTests_1.csh", ShaderType.ComputeShader);
        int program = 0;
        var outputs = new Texture3D[16];
        try
        {
            program = GL.CreateProgram();
            GL.AttachShader(program, shader);
            TestShaderInterfaces.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            var layout = TestShaderInterfaces.BuildLayout(program);
            layout.RegisterUniformBlockBinding("TestParams", 0, required: true);
            layout.ApplyContract(program);

            // Allocate every output before submission so resource creation cannot retire intervening work.
            for (int i = 0; i < outputs.Length; i++)
                outputs[i] = Texture3D.Create(1, 1, 1, PixelInternalFormat.Rgba32ui, TextureFilterMode.Nearest,
                    TextureTarget.Texture3D, debugName: "Tests.Retirement." + i);
            GpuUniformBuffer? originalBuffer = null;
            byte[] bytes = new byte[16];
            for (int i = 0; i < outputs.Length; i++)
            {
                Assert.True(GpuUniformRingSystem.TryGetCurrent(out var ring));
                BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)(i + 101));
                var allocation = ring.AllocateAndWrite(bytes);
                Assert.True(allocation.IsValid);
                Assert.Equal(0, allocation.OffsetBytes);
                if (boundary != 2 && originalBuffer != null) Assert.Same(originalBuffer, allocation.Buffer);
                originalBuffer = allocation.Buffer;
                if (boundary == 1)
                {
                    // Helpers used while preparing a draw must preserve the already-written range.
                    using var target = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba16f);
                    byte[] sentinel = new byte[16];
                    BinaryPrimitives.WriteUInt32LittleEndian(sentinel, 9999);
                    var later = ring.AllocateAndWrite(sentinel);
                    Assert.True(later.IsValid);
                    Assert.NotEqual(allocation.OffsetBytes, later.OffsetBytes);
                }
                allocation.Buffer.BindRange(0, allocation.OffsetBytes, allocation.SizeBytes);
                GL.BindImageTexture(0, outputs[i].TextureId, 0, true, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
                GL.UseProgram(program);
                GL.DispatchCompute(1, 1, 1);
                GL.MemoryBarrier(MemoryBarrierFlags.TextureUpdateBarrierBit | MemoryBarrierFlags.ShaderImageAccessBarrierBit);

                // Only the fixture boundary may wait here; no observation can hide unsafe mapped reuse.
                if (boundary == 0) TestUniformRing.BeginFrame();
                else if (boundary == 1)
                {
                    EnsureContextValid();
                    using var target = CreateRenderTarget(1, 1, PixelInternalFormat.Rgba16f);
                }
                else
                {
                    TestUniformRing.Dispose();
                    Assert.False(GpuUniformRingSystem.TryGetCurrent(out _));
                    TestUniformRing.BeginFrame();
                }
            }

            // Observe only after every dispatch and the last retirement boundary have completed.
            for (int i = 0; i < outputs.Length; i++)
            {
                uint[] actual = new uint[4];
                GL.BindTexture(TextureTarget.Texture3D, outputs[i].TextureId);
                GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, actual);
                Assert.Equal((uint)(i + 101), actual[0]);
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.UseProgram(0);
            GL.BindTexture(TextureTarget.Texture3D, 0);
            GL.BindImageTexture(0, 0, 0, false, 0, TextureAccess.WriteOnly, SizedInternalFormat.Rgba32ui);
            foreach (var output in outputs) output?.Dispose();
            if (program != 0) TestShaderInterfaces.DeleteProgram(program);
            TestShaderInterfaces.DeleteShader(shader);
            GlStateCache.Current.InvalidateAll();
        }
    }
    #endregion
}
