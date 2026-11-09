using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System;

using OpenTK.Graphics.OpenGL;

using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

using Xunit;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks production program-layout sampler binding against a real headless context.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GpuProgramLayoutBindingTests : RenderTestBase
{
    #region Public API

    /// <summary>Creates binding tests with the shared render context.</summary>
    /// <param name="fixture">The headless graphics fixture.</param>
    public GpuProgramLayoutBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Verifies the layout-driven sampler binding reaches the compute dispatch.</summary>
    [Fact]
    public void ComputeDispatchUsesLayoutResolvedSamplerAndImageSlots()
    {
        // Arrange
        EnsureContextValid();

        const string shader = "tests/GpuProgramLayoutBindingTests_1.csh";

        using var shaderOwner = VanillaGraphicsExpanded.Tests.Support.GPU.Shaders.BuiltShaderFixture.Load(shader, ShaderType.ComputeShader);
        int shaderId = shaderOwner.ShaderId;
        GL.GetShader(shaderId, ShaderParameter.CompileStatus, out int okShader);
        string shaderLog = GL.GetShaderInfoLog(shaderId) ?? string.Empty;
        Assert.True(okShader != 0, $"Compute shader compile failed:\n{shaderLog}");

        using var programOwner = NativeResourceSetup.CreateProgram();
        int programId = programOwner.ProgramId;
        GL.AttachShader(programId, shaderId);
        GL.LinkProgram(programId);
        GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int okLink);
        string programLog = GL.GetProgramInfoLog(programId) ?? string.Empty;
        Assert.True(okLink != 0, $"Compute program link failed:\n{programLog}");

        var stageContract = GpuShaderContracts.Registry.FindStage(shader);
        var layout = new GpuProgramLayout { BinaryInterface = new GpuProgramInterface(programId, [stageContract.Bindings]) };
        layout.RebuildCache(programId);
        Assert.NotSame(GpuProgramLayout.Empty, layout);

        // Create a 1x1x1 R32UI occ texture with a known value.
        using var occ = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_Occ" }).RequireResource();

        uint[] occData = [123u];
        occ.UploadDataImmediate(occData, x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);

        // Output image as R32UI 1x1x1.
        using var outTex = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "Test_Out" }).RequireResource();

        outTex.UploadDataImmediate(new uint[1], x: 0, y: 0, z: 0, regionWidth: 1, regionHeight: 1, regionDepth: 1, mipLevel: 0);

        GL.UseProgram(programId);

        // Bind using the cached program layout. If the sampler unit is wrong, outImg will stay 0.
        Assert.True(layout.TryGetSamplerUnit("uOcc", out int samplerSlot));
        Assert.True(layout.TryGetImageUnit("outImg", out int imageSlot));
        // Act
        RHICommandContext.Current.BindSampledTextures([new(samplerSlot, occ, Dimension: TextureDimension.Three)]);
        VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageSlotBindings.BindImageTexture(OpenGlStateCache.Current, imageSlot,
            new RHIImageBinding(outTex, ImageAccess.WriteOnly, Format: TextureFormat.R32ui));

        GL.DispatchCompute(1, 1, 1);
        GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);

        GpuTestFence.WaitForGpuOrSkip("ProgramLayout sampler/image dispatch");

        // Assert
        uint[] outData = ReadTexImageR32ui(outTex.TextureId, width: 1, height: 1, depth: 1);
        Assert.Equal(123u, outData[0]);

    }

    #endregion

    #region Private

    /// <summary>Reads an R32UI texture while restoring the caller's pack alignment and 3D binding.</summary>
    private static uint[] ReadTexImageR32ui(int textureId, int width, int height, int depth)
    {
        uint[] data = new uint[checked(width * height * depth)];
        GL.GetInteger(GetPName.PackAlignment, out int previousPackAlignment);
        GL.GetInteger(GetPName.TextureBinding3D, out int previousTexture);
        try
        {
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.BindTexture(TextureTarget.Texture3D, textureId);
            GL.GetTexImage(TextureTarget.Texture3D, level: 0, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        }
        finally
        {
            try { GL.BindTexture(TextureTarget.Texture3D, previousTexture); }
            finally { GL.PixelStore(PixelStoreParameter.PackAlignment, previousPackAlignment); }
        }
        return data;
    }
    #endregion
}
