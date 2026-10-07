using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises ordered CPU radiance and metadata publication through complete graphics passes.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class WorldProbeCpuSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Two-pass publication ignores hostile ambient state, restores it and republishes changed values without clearing neighbors.</summary>
    [Fact]
    public void CpuAtlasPublicationRestoresHostileStateAndPreservesNeighbors()
    {
        EnsureContextValid();
        using var atlas = new SurfaceLightingWorldProbeFixture(2);
        GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0); GL.ColorMask(false, false, false, false);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Never); GL.Viewport(3, 4, 5, 6);
        StateCache.Current.InvalidateAll();
        try
        {
            var first = Result(.25f, .5f); long before = StateCache.Current.DrawSubmissions;
            Assert.Equal(1, atlas.TryUpload(first, 1024));
            Assert.Equal(before + 2, StateCache.Current.DrawSubmissions);
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest)); Assert.Equal((int)DepthFunction.Never, GL.GetInteger(GetPName.DepthFunc));
            int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport); Assert.Equal(new[] { 3, 4, 5, 6 }, viewport);
            bool[] mask = new bool[4]; GL.GetBoolean(GetPName.ColorWritemask, mask); Assert.All(mask, Assert.False);
            float[] pixels = atlas.Resources.ProbeRadianceAtlas.ReadPixels(); Assert.InRange(pixels[0], .2499f, .2501f);
            float[] meta = atlas.Resources.ProbeMeta0.ReadPixels(); Assert.InRange(meta[0], .4999f, .5001f);
            Assert.Equal(0, atlas.TryUpload(Result(.75f, .9f), 1));
            Assert.Equal(pixels, atlas.Resources.ProbeRadianceAtlas.ReadPixels());
            atlas.InvalidatePrograms();
            Assert.Equal(1, atlas.TryUpload(Result(.75f, .9f), 1024));
            var changed = atlas.Resources.ProbeRadianceAtlas.ReadPixels(); Assert.InRange(changed[0], .7499f, .7501f);
            Assert.Equal(pixels.AsSpan(4).ToArray(), changed.AsSpan(4).ToArray());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.DepthTest); GL.ColorMask(true, true, true, true);
            GL.DepthFunc(DepthFunction.Less); StateCache.Current.InvalidateAll();
        }
    }
    #endregion

    #region Private
    /// <summary>Supplies one already resolved CPU direction and matching confidence at the first physical atlas slot.</summary>
    private static LumOnWorldProbeTraceResult Result(float radiance, float confidence) =>
        new(1, new(0, new(), new(), 0), true, WorldProbeTraceFailureReason.None,
            [new(0, 0, new Vector3(radiance), 1)], 0, Vector3.UnitY, .5f, confidence, 1, LumOnWorldProbeImportanceFlags.None);
    #endregion
}
