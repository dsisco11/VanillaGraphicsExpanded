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
        var framebuffer = buffers.DirectLightingFbo!;
        Assert.True(buffers.EnsureBuffers(8, 6));
        Assert.Same(original, buffers.Radiance);
        Assert.True(buffers.EnsureBuffers(13, 9));
        Assert.True(original.IsDisposed);
        Assert.True(framebuffer.IsDisposed);
        var resized = buffers.Radiance!;
        Assert.Equal(13, resized.Width);
        Assert.Equal(9, resized.Height);
        Assert.Equal(3, resized.Depth);
        for (int layer = 0; layer < 3; layer++)
        {
            var attachment = buffers.DirectLightingFbo!.GetAttachment(FramebufferAttachment.ColorAttachment0 + layer)!;
            Assert.Same(resized, attachment.Resource);
            Assert.Equal(layer, attachment.Layer);
        }
        buffers.Dispose();
        Assert.True(resized.IsDisposed);
    }

    /// <summary>A later allocation failure cleans the production target constructor's earlier texture and restores the caller binding.</summary>
    [Fact]
    public void ConstructorFailureReleasesEarlierAllocationAndRestoresFramebuffer()
    {
        EnsureContextValid();
        using var incoming = GpuFramebuffer.CreateEmpty();
        incoming.Bind();
        partialTexture = null;
        partialTextureId = 0;
        var harmony = new Harmony("VGE.Tests.DirectLightingAllocationFailure");
        var create = AccessTools.Method(typeof(Texture3D), nameof(Texture3D.Create));
        try
        {
            harmony.Patch(create,
                postfix: new HarmonyMethod(typeof(DirectLightingBufferOwnershipTests), nameof(CaptureRadianceAllocation)));
            harmony.Patch(AccessTools.Method(typeof(GpuFramebuffer), nameof(GpuFramebuffer.Create)),
                prefix: new HarmonyMethod(typeof(DirectLightingBufferOwnershipTests), nameof(FailFramebufferAllocation)));
            Assert.Throws<InvalidOperationException>(() => new DirectLightingTargets(8, 6));
            Assert.NotNull(partialTexture);
            Assert.True(partialTexture.IsDisposed);
            Assert.False(GL.IsTexture(partialTextureId));
            Assert.Equal(incoming.FboId, GL.GetInteger(GetPName.FramebufferBinding));
            Assert.Equal(incoming.FboId, GpuFramebuffer.SaveBinding());
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

    /// <summary>Interrupts framebuffer allocation after the radiance array has been registered.</summary>
    private static void FailFramebufferAllocation(string? debugName)
    {
        if (debugName == "DirectLightingFBO") throw new InvalidOperationException("Injected allocation failure.");
    }

    /// <summary>Retains the radiance array identity so its cleanup can be observed after framebuffer allocation failure.</summary>
    private static void CaptureRadianceAllocation(string? debugName, Texture3D __result)
    {
        if (debugName != "DirectLighting.Radiance") return;
        partialTexture = __result;
        partialTextureId = __result.TextureId;
    }

    #endregion
}
