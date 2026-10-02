using System.Reflection;
using VanillaGraphicsExpanded.Rendering.Shaders;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies generated property assignments with a linked compute dispatch and readback.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GeneratedResourceBindingTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared headless GL context.</summary>
    public GeneratedResourceBindingTests(HeadlessGLFixture fixture) : base(fixture) { }

    /// <summary>Typed setters retain resources until generated publication binds them and skips an absent sampler.</summary>
    [Fact]
    public void TypedAssignmentsDriveComputeAndSkipInactiveResources()
    {
        EnsureContextValid();
        int module = BuiltShaderFixture.Load("tests/GpuProgramLayoutBindingTests_1.csh", ShaderType.ComputeShader);
        int program = GL.CreateProgram();
        try
        {
            GL.AttachShader(program, module);
            TestShaderInterfaces.LinkProgram(program);
            GL.GetProgram(program, GetProgramParameterName.LinkStatus, out int linked);
            Assert.True(linked != 0, GL.GetProgramInfoLog(program));
            var shader = new GeneratedResourceBindingShader { ProgramId = program };
            shader.ProgramLayout.BinaryInterface = TestShaderInterfaces.BuildLayout(program).BinaryInterface;
            shader.ProgramLayout.RebuildCache(program);
            using var input = Texture3D.Create(1, 1, 1, PixelInternalFormat.R32ui,
                TextureFilterMode.Nearest, TextureTarget.Texture3D, "GeneratedBinding.Input");
            using var output = Texture3D.Create(1, 1, 1, PixelInternalFormat.R32ui,
                TextureFilterMode.Nearest, TextureTarget.Texture3D, "GeneratedBinding.Output");
            input.UploadDataImmediate(new uint[] { 123 }, 0, 0, 0, 1, 1, 1, 0);
            output.UploadDataImmediate(new uint[1], 0, 0, 0, 1, 1, 1, 0);

            // Readback depends on both assignments reaching the binary's actual binding units.
            GL.UseProgram(program);
            // The texture-only property must infer format and use the same defaults as an explicit view.
            var direct = new GeneratedTextureImageShader { ProgramId = program };
            direct.ProgramLayout.BinaryInterface = shader.ProgramLayout.BinaryInterface;
            direct.ProgramLayout.RebuildCache(program);
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int priorImage);
            direct.Image = output;
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int stagedImage);
            Assert.Equal(priorImage, stagedImage);
            Publish(direct);
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int imageName);
            GL.GetInteger((GetIndexedPName)All.ImageBindingAccess, 0, out int imageAccess);
            GL.GetInteger((GetIndexedPName)All.ImageBindingFormat, 0, out int imageFormat);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLevel, 0, out int imageLevel);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLayered, 0, out int imageLayered);
            GL.GetInteger((GetIndexedPName)All.ImageBindingLayer, 0, out int imageLayer);
            Assert.Equal(output.TextureId, imageName);
            Assert.Equal((int)TextureAccess.ReadOnly, imageAccess);
            Assert.Equal((int)SizedInternalFormat.R32ui, imageFormat);
            Assert.Equal(0, imageLevel);
            Assert.Equal(1, imageLayered);
            Assert.Equal(0, imageLayer);
            direct.ProgramId = 0;
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.BindTexture(TextureTarget.Texture3D, 0);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.GetInteger(GetPName.TextureBinding3D, out int priorInput);
            shader.Input = input;
            shader.Output = new(output, Access: TextureAccess.WriteOnly, Layered: true, Format: SizedInternalFormat.R32ui);
            shader.Unused = input;
            GL.GetInteger(GetPName.TextureBinding3D, out int stagedInput);
            Assert.Equal(priorInput, stagedInput);
            Publish(shader);
            GL.ActiveTexture(TextureUnit.Texture7);
            GL.GetInteger(GetPName.TextureBinding3D, out int unusedBinding);
            Assert.Equal(0, unusedBinding);
            GL.DispatchCompute(1, 1, 1);
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
            GpuTestFence.WaitForGpuOrSkip("Generated resource binding dispatch");
            uint[] result = new uint[1];
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            GL.BindTexture(TextureTarget.Texture3D, output.TextureId);
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, result);
            Assert.Equal(123u, result[0]);
            // Optional active absence explicitly clears a binding left by the preceding owner.
            ShaderBindingSubmission.ValidateSampler(shader, "uOcc", false, (GpuTexture?)null);
            ShaderBindingSubmission.Sampler(shader, "uOcc", (GpuTexture?)null, VanillaGraphicsExpanded.Rendering.Contracts.ShaderTextureTarget.Texture3D);
            GL.ActiveTexture(TextureUnit.Texture3);
            GL.GetInteger(GetPName.TextureBinding3D, out int clearedInput);
            Assert.Equal(0, clearedInput);
            shader.ProgramId = 0;
        }
        finally
        {
            GL.UseProgram(0);
            GL.BindTexture(TextureTarget.Texture3D, 0);
            TestShaderInterfaces.DeleteProgram(program);
            TestShaderInterfaces.DeleteShader(module);
        }
    }
    #endregion

    #region Private
    /// <summary>Runs production publication for a fixture-owned executable without invoking asset readiness.</summary>
    private static void Publish(GpuProgram shader) => typeof(GpuProgram).GetMethod("SubmitPreparedInputs", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(shader, null);
    #endregion
}
