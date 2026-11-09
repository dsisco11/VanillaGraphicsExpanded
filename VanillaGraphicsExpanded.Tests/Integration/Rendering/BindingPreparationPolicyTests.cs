using OpenTK.Graphics.OpenGL4;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies active policy and preparation-context contracts through actual immutable binding tables.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class BindingPreparationPolicyTests(HeadlessGLFixture fixture)
{
    #region Public API
    /// <summary>Active optional arrays clear declared targets; inactive required inputs do no work, while present retired inputs fail.</summary>
    [Fact]
    public void OptionalAbsenceAndInactiveInputsHaveDistinctPublicationRules()
    {
        // Arrange
        fixture.EnsureContextValid();
        fixture.InitializeResourceDisposal();
        try
        {
            var context = RHICommandContext.Current;
            var contract = new GpuBindingContract();
            contract.Samplers.Add("sources", new(0, false, 2));
            contract.Samplers.Add("unused", new(5, true));
            contract.RegisterImageUnit("result", 0);
            using var shader = context.CreateShader(new(ShaderKind.Compute, [new(ShaderStage.Compute,
                "#version 450\nlayout(local_size_x=1) in; layout(binding=0) uniform sampler2D sources[2]; layout(rgba32f,binding=0) writeonly uniform image2D result; void main(){ imageStore(result,ivec2(0),texelFetch(sources[0],ivec2(0),0)+texelFetch(sources[1],ivec2(0),0)); }")], contract)).RequireResource();
            using var pipeline = context.CreateComputePipeline(new(shader)).RequireResource();
            using var output = context.CreateTexture2D(new() { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f,
                Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var otherTarget = context.CreateTexture3D(new() { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f,
                Width = 1, Height = 1, Depth = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            using var source = context.CreateTexture2D(new() { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f,
                Width = 1, Height = 1, MipLevels = 1, Storage = ResourceStorageMode.Mutable }).RequireResource();
            source.Dispose();
            context.SetPipeline(pipeline);
            context.BindSampledTextures([new(0, otherTarget, Dimension: TextureDimension.Three)]);
            var absent = new RHIResourceBindings(shader, samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "unused"), source)],
                images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "result"), new RHIImageBinding(output))]);
            // Act
            context.BindResources(absent);

            // Assert
            GL.ActiveTexture(TextureUnit.Texture0);
            Assert.Equal(0, GL.GetInteger(GetPName.TextureBinding2D));
            Assert.Equal(otherTarget.TextureId, GL.GetInteger(GetPName.TextureBinding3D));
            Assert.Equal(3, absent.Structure.Inputs.Length);
            var unavailable = new RHIResourceBindings(shader, samplers: [new(GpuBindingEntry.Identity(ShaderBindingKind.Sampler, "sources"), source)],
                images: [new(GpuBindingEntry.Identity(ShaderBindingKind.Image, "result"), new RHIImageBinding(output))]);
            // Act and assert: an active required array with retired input must fail before native work.
            Assert.Throws<InvalidOperationException>(() => context.BindResources(unavailable));
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally { fixture.CleanupGpuResources(); OpenGlStateCache.Current.InvalidateAll(); }
    }

    #endregion
}
