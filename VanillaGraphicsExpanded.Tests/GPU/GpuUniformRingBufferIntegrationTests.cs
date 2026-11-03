using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks uniform-ring uploads and binding behavior through real shader execution.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuUniformRingBufferIntegrationTests : RenderTestBase
{
    #region Public API

    /// <summary>Creates uniform-ring integration tests with the shared render context.</summary>
    /// <param name="fixture">The headless graphics fixture.</param>
    public GpuUniformRingBufferIntegrationTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Verifies a published uniform-ring range reaches the shader with the expected contents.</summary>
    [Fact]
    public void UniformRingBuffer_BindRange_WritesCorrectData()
    {
        EnsureContextValid();

        const string shader = "tests/GpuUniformRingBufferIntegrationTests_1.csh";

        using var shaderOwner = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.Load(shader, ShaderType.ComputeShader);
        int shaderId = shaderOwner.ShaderId;
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        Assert.True(okShader != 0, GL.GetShaderInfoLog(shaderId));

        using var programOwner = NativeResourceSetup.CreateProgram();
        int programId = programOwner.ProgramId;
        GL.AttachShader(programId, shaderId);
        GL.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int okLink);
        Assert.True(okLink != 0, GL.GetProgramInfoLog(programId));

        var layout = new GpuProgramLayout { BinaryInterface = new GpuProgramInterface(programId, [GpuShaderContracts.Registry.FindStage(shader).Bindings]) };
        layout.RebuildCache(programId);
        layout.RegisterUniformBlockBinding("TestParams", bindingIndex: 0, required: true);
        layout.ApplyContract(programId);

        using var outTex = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_Out" }).RequireResource();

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

        // Withdraw the current selection before the program owner retires at method exit.
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
    }

    /// <summary>Verifies unused uniform storage does not bind a block that the executable does not consume.</summary>
    [Fact]
    public void UnusedUniformAllocationDoesNotBindInactiveBlock()
    {
        EnsureContextValid();

        const string shader = "tests/GpuUniformRingBufferIntegrationTests_2.csh";

        using var shaderOwner = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.Load(shader, ShaderType.ComputeShader);
        int shaderId = shaderOwner.ShaderId;
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        Assert.True(okShader != 0, GL.GetShaderInfoLog(shaderId));

        using var programOwner = NativeResourceSetup.CreateProgram();
        int programId = programOwner.ProgramId;
        GL.AttachShader(programId, shaderId);
        GL.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int okLink);
        Assert.True(okLink != 0, GL.GetProgramInfoLog(programId));

        var layout = new GpuProgramLayout { BinaryInterface = new GpuProgramInterface(programId, [GpuShaderContracts.Registry.FindStage(shader).Bindings]) };
        layout.RebuildCache(programId);
        layout.RegisterUniformBlockBinding("TestParams", bindingIndex: 0, required: true);
        layout.ApplyContract(programId);

        using var outTex = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_Out" }).RequireResource();

        outTex.UploadDataImmediate(new uint[4], x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);
        GL.BindImageTexture(0, outTex.TextureId, level: 0, layered: true, layer: 0, access: TextureAccess.WriteOnly, format: SizedInternalFormat.Rgba32ui);

        using var ring = new GpuUniformRingBuffer(pageSizeBytes: 64 * 1024, pageCount: 3, preferPersistent: true, coherent: true, debugName: "TestUboRing");
        ring.BeginFrame(frameIndex: 0);

        byte[] uboData = new byte[16];
        WriteU32(uboData, 0, 999u);

        var alloc = ring.AllocateAndWrite(uboData);
        Assert.True(alloc.IsValid);

        Assert.False(layout.ResolveUniformBlockActive(programId, "TestParams").IsActive);

        GL.UseProgram(programId);
        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.UniformBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("UniformRingBuffer missing-block dispatch");

        uint[] outData = ReadTexImageRgba32ui(outTex.TextureId);
        Assert.Equal(0u, outData[0]);

        ring.EndFrame();

        // Withdraw the current selection before the program owner retires at method exit.
        OpenGLProgramSelection.Select(OpenGlStateCache.Current, 0);
    }

    #endregion

    #region Private

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
    #endregion
}
