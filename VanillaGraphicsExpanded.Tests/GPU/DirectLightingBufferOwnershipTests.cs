using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies direct-light target identity and lifetime through storage resizing.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DirectLightingBufferOwnershipTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    private static Texture3D? partialTexture;
    private static int partialTextureId;

    #region Lifecycle tests

    /// <summary>Stable size retains storage; resizing replaces all layers and retires the old publication.</summary>
    [Fact]
    public void ResizeReplacesAllLayersAndDisposalRetiresThem()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var buffers = new DirectLightingBufferManager(assets.Api);
        Assert.True(buffers.EnsureBuffers(8, 6));
        var original = buffers.Radiance!;
        var pass = Assert.IsType<RHIRenderPassDescription>(buffers.LightingPass);
        Assert.True(buffers.EnsureBuffers(8, 6));
        Assert.Same(original, buffers.Radiance);
        Assert.True(buffers.EnsureBuffers(13, 9));
        Assert.True(original.IsDisposed);
        Assert.All(pass.Colors, output => Assert.False(output.Image!.Value.Texture.IsValid));
        var resized = buffers.Radiance!;
        Assert.Equal(13, resized.Width);
        Assert.Equal(9, resized.Height);
        Assert.Equal(3, resized.Depth);
        for (int layer = 0; layer < 3; layer++)
        {
            var attachment = buffers.LightingPass!.Colors[layer].Image!.Value;
            Assert.Equal((GpuTextureRef)resized, attachment.Texture);
            Assert.Equal(layer, attachment.Subresource.Layer);
        }
        buffers.Dispose();
        Assert.True(resized.IsDisposed);
    }

    /// <summary>Image declaration defers framebuffer realization and retires its sole owned allocation.</summary>
    [Fact]
    public void ConstructorDefersFramebufferRealizationAndRetiresImage()
    {
        EnsureContextValid();
        using var incoming = DynamicGpuFramebuffer.CreateEmpty();
        incoming.Bind();
        partialTexture = null;
        partialTextureId = 0;
        var harmony = new Harmony("VGE.Tests.DirectLightingAllocationFailure");
        var create = AccessTools.Method(typeof(RHICommandContext), nameof(RHICommandContext.CreateTexture3D));
        try
        {
            harmony.Patch(create,
                postfix: new HarmonyMethod(typeof(DirectLightingBufferOwnershipTests), nameof(CaptureRadianceAllocation)));
            harmony.Patch(AccessTools.Method(typeof(StaticGpuFramebuffer), nameof(StaticGpuFramebuffer.Create)),
                prefix: new HarmonyMethod(typeof(DirectLightingBufferOwnershipTests), nameof(FailFramebufferAllocation)));
            using (var targets = new DirectLightingTargets(8, 6))
            {
                Assert.Equal(3,targets.Pass.Colors.Length);
                Assert.Same(partialTexture,targets.Radiance);
                Assert.Equal(incoming.FboId,GL.GetInteger(GetPName.FramebufferBinding));
            }
            Assert.NotNull(partialTexture);
            Assert.True(partialTexture.IsDisposed);
            Assert.False(GL.IsTexture(partialTextureId));
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.FramebufferBinding));
            Assert.Equal(incoming.FboId, OpenGlStateCache.Current.GetCurrentFramebuffer(FramebufferTarget.Framebuffer));
        }
        finally
        {
            harmony.UnpatchAll(harmony.Id);
            partialTexture?.Dispose();
            partialTexture = null;
            GpuFramebuffer.Unbind();
        }
    }

    #endregion

    #region Allocation fault injection

    /// <summary>Rejects eager framebuffer realization during image-only declaration.</summary>
    private static void FailFramebufferAllocation(string? debugName)
    {
        throw new InvalidOperationException("Image declaration must defer native framebuffer realization.");
    }

    /// <summary>Retains the radiance array identity so its cleanup can be observed after framebuffer allocation failure.</summary>
    private static void CaptureRadianceAllocation(in RHITextureDescription description, RHICreationResult<Texture3D> __result)
    {
        if (description.Label != "DirectLighting.Radiance" || !__result.IsSuccess) return;
        partialTexture = __result.RequireResource();
        partialTextureId = partialTexture.TextureId;
    }

    #endregion
}
