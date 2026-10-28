using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Core = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Proves viewport-zero managed work and restoration leave independently owned viewport indices untouched.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ViewportIndexPreservationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Viewport, scissor, depth-range and scissor-enable changes stay confined to the RHI's viewport zero.</summary>
    [Fact]
    public void ManagedBoundaryPreservesViewportOneSentinels()
    {
        EnsureContextValid();
        float[] oldViewport = new float[4]; int[] oldScissor = new int[4]; double[] oldRange = new double[2];
        Core.GL.GetFloat(Core.GetIndexedPName.Viewport, 1, oldViewport);
        Core.GL.GetInteger(Core.GetIndexedPName.ScissorBox, 1, oldScissor);
        Core.GL.GetDouble(Core.GetIndexedPName.DepthRange, 1, oldRange);
        bool oldEnabled = Core.GL.IsEnabled(Core.IndexedEnableCap.ScissorTest, 1);
        var cache = OpenGlStateCache.Current;
        try
        {
            Core.GL.ViewportIndexed(1, 11, 13, 17, 19);
            Core.GL.ScissorIndexed(1, 3, 5, 7, 9);
            Core.GL.DepthRangeIndexed(1, .2, .8);
            Core.GL.Enable(Core.IndexedEnableCap.ScissorTest, 1);
            cache.ApplyDynamic(new() { Width = 4, Height = 4 });
            cache.SetScissor(0, 0, 2, 2);
            cache.SetDepthRange(.1, .9);
            cache.SetCapability(EnableCap.ScissorTest, false);
            AssertSentinels();
            var declaration = new EngineBoundaryDeclaration("Tests.ViewportZero", new PipelineStateCoverage(
                depth: DepthStateKnowledge.DepthRange,
                rasterizer: RasterizerStateKnowledge.ScissorEnabled,
                dynamic: DynamicDrawStateKnowledge.Viewport | DynamicDrawStateKnowledge.Scissor, completeGraphics: true));
            Assert.True(cache.TryBeginEngineBoundary(declaration, out var boundary), cache.BoundaryEntryFailure?.ToString());
            boundary!.Run(() =>
            {
                cache.ApplyDynamic(new() { Width = 8, Height = 8 });
                cache.SetScissor(1, 1, 3, 3);
                cache.SetDepthRange(.3, .7);
                cache.SetCapability(EnableCap.ScissorTest, true);
                AssertSentinels();
            });
            AssertSentinels();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            Core.GL.ViewportIndexed(1, oldViewport);
            Core.GL.ScissorIndexed(1, oldScissor);
            Core.GL.DepthRangeIndexed(1, oldRange[0], oldRange[1]);
            if (oldEnabled) Core.GL.Enable(Core.IndexedEnableCap.ScissorTest, 1);
            else Core.GL.Disable(Core.IndexedEnableCap.ScissorTest, 1);
        }
    }
    #endregion

    #region Private
    /// <summary>Checks independent native array fields without consulting the viewport-zero managed cache.</summary>
    private static void AssertSentinels()
    {
        float[] viewport = new float[4]; int[] scissor = new int[4]; double[] range = new double[2];
        Core.GL.GetFloat(Core.GetIndexedPName.Viewport, 1, viewport);
        Core.GL.GetInteger(Core.GetIndexedPName.ScissorBox, 1, scissor);
        Core.GL.GetDouble(Core.GetIndexedPName.DepthRange, 1, range);
        Assert.Equal(new float[] { 11, 13, 17, 19 }, viewport);
        Assert.Equal(new[] { 3, 5, 7, 9 }, scissor);
        Assert.Equal(.2, range[0], 6);
        Assert.Equal(.8, range[1], 6);
        Assert.True(Core.GL.IsEnabled(Core.IndexedEnableCap.ScissorTest, 1));
    }
    #endregion
}
