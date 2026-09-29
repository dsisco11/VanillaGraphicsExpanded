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
    private static DynamicTexture2D? partialTexture;
    private static int partialTextureId;

    #region Lifecycle tests

    /// <summary>Resizing retains the published objects and GL handles until manager disposal.</summary>
    [Fact]
    public void ResizePreservesPublishedTargetsAndDisposalRetiresThem()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var buffers = new DirectLightingBufferManager(assets.Api);
        Assert.True(buffers.EnsureBuffers(8, 6));
        var textures = new[] { buffers.DirectDiffuseTex!, buffers.DirectSpecularTex!, buffers.EmissiveTex! };
        var textureIds = textures.Select(texture => texture.TextureId).ToArray();
        var framebuffer = buffers.DirectLightingFbo!;
        int framebufferId = framebuffer.FboId;

        Assert.True(buffers.EnsureBuffers(12, 10));
        Assert.Same(framebuffer, buffers.DirectLightingFbo);
        Assert.Equal(framebufferId, framebuffer.FboId);
        Assert.Equal(textures, new[] { buffers.DirectDiffuseTex, buffers.DirectSpecularTex, buffers.EmissiveTex });
        for (int index = 0; index < textures.Length; index++)
        {
            Assert.Equal(textureIds[index], textures[index].TextureId);
            Assert.Equal(12, textures[index].Width);
            Assert.Equal(10, textures[index].Height);
            Assert.Same(textures[index], framebuffer[index]);
        }

        buffers.Dispose();
        Assert.True(framebuffer.IsDisposed);
        Assert.False(GL.IsFramebuffer(framebufferId));
        Assert.All(textures, texture => Assert.True(texture.IsDisposed));
        Assert.All(textureIds, id => Assert.False(GL.IsTexture(id)));
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
        var create = AccessTools.Method(typeof(DynamicTexture2D), nameof(DynamicTexture2D.Create));
        try
        {
            harmony.Patch(create,
                prefix: new HarmonyMethod(typeof(DirectLightingBufferOwnershipTests), nameof(FailSecondRadianceAllocation)),
                postfix: new HarmonyMethod(typeof(DirectLightingBufferOwnershipTests), nameof(CaptureFirstRadianceAllocation)));
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

    /// <summary>Interrupts the second production target allocation after the first has been registered.</summary>
    private static void FailSecondRadianceAllocation(string? debugName)
    {
        if (debugName == "DirectSpecular") throw new InvalidOperationException("Injected allocation failure.");
    }

    /// <summary>Retains the first allocation's identity so its cleanup can be observed after constructor failure.</summary>
    private static void CaptureFirstRadianceAllocation(string? debugName, DynamicTexture2D __result)
    {
        if (debugName != "DirectDiffuse") return;
        partialTexture = __result;
        partialTextureId = __result.TextureId;
    }

    #endregion
}
