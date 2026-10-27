using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        string path = Path.Combine(AppContext.BaseDirectory,"assets","shaders","tests","GpuProgramLayoutBindingTests_1.csh.spv");
        var settings = new ShaderSettings(VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram("tests/GpuProgramLayoutBindingTests_1"));
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path,settings,out var first,out string log),log);
        using var shader = new GeneratedResourceBindingShader(first!);
        Assert.True(ComputeShaderAsset.TryLoadFromSpirv(path,settings,out var second,out log),log);
        using var direct = new GeneratedTextureImageShader(second!);
        try
        {
            using var input = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "GeneratedBinding.Input" }).RequireResource();
            using var output = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.R32ui, Width = System.Math.Max(1, 1), Height = System.Math.Max(1, 1), Depth = System.Math.Max(1, 1), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "GeneratedBinding.Output" }).RequireResource();
            input.UploadDataImmediate(new uint[] { 123 }, 0, 0, 0, 1, 1, 1, 0);
            output.UploadDataImmediate(new uint[1], 0, 0, 0, 1, 1, 1, 0);

            // Readback depends on both assignments reaching the binary's actual binding units.
            // The texture-only property infers the canonical image defaults.
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int priorImage);
            direct.Image = output;
            GL.GetInteger((GetIndexedPName)All.ImageBindingName, 0, out int stagedImage);
            Assert.Equal(priorImage, stagedImage);
            var context=RHICommandContext.Current;
            context.SetPipeline(direct.Pipeline);
            context.BindResources(new(direct.Shader,
                samplers:[new(VanillaGraphicsExpanded.Rendering.Contracts.GpuBindingEntry.Identity(VanillaGraphicsExpanded.Rendering.Contracts.ShaderBindingKind.Sampler,"uOcc"), input)],
                images:direct.CaptureBindings().Images));
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
            OpenGLImageBindings.ActiveTexture(StateCache.Current, 7);
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture3D, StateCache.Current.GetActiveTextureUnit(), 0);
            OpenGLImageBindings.ActiveTexture(StateCache.Current, 3);
            GL.GetInteger(GetPName.TextureBinding3D, out int priorInput);
            shader.Input = input;
            shader.Output = new(output, Access: ImageAccess.WriteOnly, Layered: true, Format: TextureFormat.R32ui);
            shader.Unused = input;
            GL.GetInteger(GetPName.TextureBinding3D, out int stagedInput);
            Assert.Equal(priorInput, stagedInput);
            context.SetPipeline(shader.Pipeline);
            context.BindResources(shader.CaptureBindings());
            OpenGLImageBindings.ActiveTexture(StateCache.Current, 7);
            GL.GetInteger(GetPName.TextureBinding3D, out int unusedBinding);
            Assert.Equal(0, unusedBinding);
            context.Dispatch(new(1));
            GL.MemoryBarrier(MemoryBarrierFlags.ShaderImageAccessBarrierBit | MemoryBarrierFlags.TextureFetchBarrierBit);
            GpuTestFence.WaitForGpuOrSkip("Generated resource binding dispatch");
            uint[] result = new uint[1];
            GL.PixelStore(PixelStoreParameter.PackAlignment, 1);
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture3D, StateCache.Current.GetActiveTextureUnit(), output.TextureId);
            GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, result);
            Assert.Equal(123u, result[0]);
            // The fixed contract cannot be weakened by changing a copied prepared entry.
            Assert.Throws<InvalidOperationException>(() => context.BindResources(new(shader.Shader, images:shader.CaptureBindings().Images)));
            OpenGLImageBindings.ActiveTexture(StateCache.Current,3);
            GL.GetInteger(GetPName.TextureBinding3D,out int retainedInput);
            Assert.Equal(input.TextureId,retainedInput);
        }
        finally
        {
            GL.UseProgram(0);
            OpenGLImageBindings.BindTexture(StateCache.Current, TextureTarget.Texture3D, StateCache.Current.GetActiveTextureUnit(), 0);
        }
    }
    #endregion

}
