using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GpuProgramLayoutBindingTests : RenderTestBase
{
    public GpuProgramLayoutBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    [Fact]
    public void ProgramLayout_TryBindSamplerTexture_UsesLayoutBindingUnit()
    {
        EnsureContextValid();

        const string shader = "tests/GpuProgramLayoutBindingTests_1.csh";

        int shaderId = VanillaGraphicsExpanded.Tests.GPU.Helpers.BuiltShaderFixture.Load(shader, ShaderType.ComputeShader);
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        string shaderLog = GL.GetShaderInfoLog(shaderId) ?? string.Empty;
        Assert.True(okShader != 0, $"Compute shader compile failed:\n{shaderLog}");

        int programId = GL.CreateProgram();
        GL.AttachShader(programId, shaderId);
        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int okLink);
        string programLog = GL.GetProgramInfoLog(programId) ?? string.Empty;
        Assert.True(okLink != 0, $"Compute program link failed:\n{programLog}");

        var layout = global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.BuildLayout(programId);
        Assert.NotSame(GpuProgramLayout.Empty, layout);

        // Create a 1x1x1 R32UI occ texture with a known value.
        using var occ = Texture3D.Create(
            width: 1,
            height: 1,
            depth: 1,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_Occ");

        uint[] occData = [123u];
        occ.UploadDataImmediate(occData, x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);

        // Output image as R32UI 1x1x1.
        using var outTex = Texture3D.Create(
            width: 1,
            height: 1,
            depth: 1,
            format: PixelInternalFormat.R32ui,
            filter: TextureFilterMode.Nearest,
            textureTarget: TextureTarget.Texture3D,
            debugName: "Test_Out");

        outTex.UploadDataImmediate(new uint[1], x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);

        GL.UseProgram(programId);

        // Bind using the cached program layout. If the sampler unit is wrong, outImg will stay 0.
        Assert.True(layout.TryBindSamplerTexture("uOcc", TextureTarget.Texture3D, occ.TextureId));
        Assert.True(layout.TryBindImageTexture("outImg", outTex, access: TextureAccess.WriteOnly, level: 0, layered: false, layer: 0, formatOverride: SizedInternalFormat.R32ui));

        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("ProgramLayout sampler/image dispatch");

        uint[] outData = ReadTexImageR32ui(outTex.TextureId, width: 1, height: 1, depth: 1);
        Assert.Equal(123u, outData[0]);

        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteProgram(programId);
        global::VanillaGraphicsExpanded.Tests.GPU.Helpers.TestShaderInterfaces.DeleteShader(shaderId);
    }

    private static uint[] ReadTexImageR32ui(int textureId, int width, int height, int depth)
    {
        uint[] data = new uint[checked(width * height * depth)];
        GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
        GL.BindTexture(TextureTarget.Texture3D, textureId);
        GL.GetTexImage(TextureTarget.Texture3D, level: 0, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        GL.BindTexture(TextureTarget.Texture3D, 0);
        return data;
    }
}
