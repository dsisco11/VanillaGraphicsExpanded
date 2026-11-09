using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR.SceneColor;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Postprocessing;

/// <summary>Checks nested scene allocation hooks restore the parent allocation state after inner cleanup.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SceneAllocationHookNestingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API

    /// <summary>Nested setup restores the parent allocation sequence after inner cleanup.</summary>
    [Fact]
    public void NestedSetupRestoresPrimaryGlowPosition()
    {
        // Arrange: Start an outer allocation hook and provide the frame list used by nested setup.
        var frames = Enumerable.Range(0, 32).Select(_ => new FrameBufferRef()).ToList();
        SceneColorAllocationHook.Prefix(out var outer);
        try
        {
            // Act: Run allocation under a nested hook, then finalize the inner hook before the parent.
            SceneColorAllocationHook.Assign(frames, (int)EnumFrameBuffer.Primary, new FrameBufferRef());
            Assert.Equal(PixelInternalFormat.Rgba16f, SceneColorAllocationHook.SelectFormat(PixelInternalFormat.Rgba8));
            SceneColorAllocationHook.Prefix(out var inner);
            try
            {
                SceneColorAllocationHook.Assign(frames, (int)EnumFrameBuffer.Primary, new FrameBufferRef());
                Assert.Equal(PixelInternalFormat.Rgba16f, SceneColorAllocationHook.SelectFormat(PixelInternalFormat.Rgba8));
            }
            finally { SceneColorAllocationHook.Finalizer(inner); }
            // Assert: Inner cleanup restores the parent's promoted policy; outer cleanup restores the original format.
            Assert.Equal(PixelInternalFormat.Rgba8, SceneColorAllocationHook.SelectFormat(PixelInternalFormat.Rgba8));
        }
        finally { SceneColorAllocationHook.Finalizer(outer); }
    }
    #endregion
}
