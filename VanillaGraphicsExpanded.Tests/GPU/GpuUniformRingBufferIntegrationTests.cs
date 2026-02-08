using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuUniformRingBufferIntegrationTests : RenderTestBase
{
    public GpuUniformRingBufferIntegrationTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void UniformRingBuffer_BindRange_WritesCorrectData()
    {
        EnsureContextValid();

        const string shader = """
            #version 430 core
            layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

            layout(std140) uniform TestParams
            {
                uvec4 u0;
            } params;

            layout(binding = 0, rgba32ui) writeonly uniform uimage3D outImg;

            void main()
            {
                imageStore(outImg, ivec3(0, 0, 0), params.u0);
            }
            """;

        int shaderId = GL.CreateShader(ShaderType.ComputeShader);
        GL.ShaderSource(shaderId, shader);
        GL.CompileShader(shaderId);
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        Assert.True(okShader != 0, GL.GetShaderInfoLog(shaderId));

        int programId = GL.CreateProgram();
        GL.AttachShader(programId, shaderId);
        GL.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int okLink);
        Assert.True(okLink != 0, GL.GetProgramInfoLog(programId));

        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding("TestParams", bindingIndex: 0, required: true);
        layout.ApplyContract(programId);

        using var outTex = Texture3D.Create(
            width: 1,
            height: 1,
            depth: 1,
            format: PixelInternalFormat.Rgba32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_Out");

        uint[] zeros = new uint[4];
        outTex.UploadDataImmediate(zeros, x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);

        GL.BindImageTexture(0, outTex.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba32ui);

        using var ring = new GpuUniformRingBuffer(pageSizeBytes: 64 * 1024, pageCount: 3, preferPersistent: true, coherent: true, debugName: "TestUboRing");
        ring.BeginFrame(frameIndex: 0);

        // std140 uvec4 => 16 bytes
        byte[] uboData = new byte[16];
        WriteU32(uboData, 0, 123u);
        WriteU32(uboData, 4, 0u);
        WriteU32(uboData, 8, 0u);
        WriteU32(uboData, 12, 0u);

        var alloc = ring.AllocateAndWrite(uboData);
        Assert.True(alloc.IsValid);

        // Bind the range to binding=0.
        alloc.Buffer.BindRange(bindingIndex: 0, offsetBytes: alloc.OffsetBytes, sizeBytes: alloc.SizeBytes);

        GL.UseProgram(programId);
        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.UniformBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("UniformRingBuffer bind-range dispatch");

        uint[] outData = ReadTexImageRgba32ui(outTex.TextureId);
        Assert.Equal(123u, outData[0]);

        ring.EndFrame();

        GL.DeleteProgram(programId);
        GL.DeleteShader(shaderId);
    }

    [Fact]
    public void UniformRingBuffer_TryBindUniformBlockRange_MissingBlock_IsNoOp()
    {
        EnsureContextValid();

        const string shader = """
            #version 430 core
            layout(local_size_x = 1, local_size_y = 1, local_size_z = 1) in;

            layout(binding = 0, rgba32ui) writeonly uniform uimage3D outImg;

            void main()
            {
                imageStore(outImg, ivec3(0, 0, 0), uvec4(0u));
            }
            """;

        int shaderId = GL.CreateShader(ShaderType.ComputeShader);
        GL.ShaderSource(shaderId, shader);
        GL.CompileShader(shaderId);
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        Assert.True(okShader != 0, GL.GetShaderInfoLog(shaderId));

        int programId = GL.CreateProgram();
        GL.AttachShader(programId, shaderId);
        GL.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int okLink);
        Assert.True(okLink != 0, GL.GetProgramInfoLog(programId));

        var layout = new GpuProgramLayout();
        layout.RegisterUniformBlockBinding("TestParams", bindingIndex: 0, required: true);
        layout.ApplyContract(programId);

        using var outTex = Texture3D.Create(
            width: 1,
            height: 1,
            depth: 1,
            format: PixelInternalFormat.Rgba32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_Out");

        outTex.UploadDataImmediate(new uint[4], x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);
        GL.BindImageTexture(0, outTex.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba32ui);

        using var ring = new GpuUniformRingBuffer(pageSizeBytes: 64 * 1024, pageCount: 3, preferPersistent: true, coherent: true, debugName: "TestUboRing");
        ring.BeginFrame(frameIndex: 0);

        byte[] uboData = new byte[16];
        WriteU32(uboData, 0, 999u);

        var alloc = ring.AllocateAndWrite(uboData);
        Assert.True(alloc.IsValid);

        bool bound = layout.TryBindUniformBlockRange(programId, "TestParams", alloc.Buffer, alloc.OffsetBytes, alloc.SizeBytes);
        Assert.False(bound);

        GL.UseProgram(programId);
        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.UniformBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("UniformRingBuffer missing-block dispatch");

        uint[] outData = ReadTexImageRgba32ui(outTex.TextureId);
        Assert.Equal(0u, outData[0]);

        ring.EndFrame();

        GL.DeleteProgram(programId);
        GL.DeleteShader(shaderId);
    }

    private static void WriteU32(byte[] dst, int offsetBytes, uint value)
    {
        dst[offsetBytes + 0] = (byte)(value & 0xFF);
        dst[offsetBytes + 1] = (byte)((value >> 8) & 0xFF);
        dst[offsetBytes + 2] = (byte)((value >> 16) & 0xFF);
        dst[offsetBytes + 3] = (byte)((value >> 24) & 0xFF);
    }

    private static uint[] ReadTexImageRgba32ui(int textureId)
    {
        uint[] data = new uint[4];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture3D, textureId);
        GL.GetTexImage(TextureTarget.Texture3D, level: 0, PixelFormat.RgbaInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(TextureTarget.Texture3D, 0);
        return data;
    }
}
