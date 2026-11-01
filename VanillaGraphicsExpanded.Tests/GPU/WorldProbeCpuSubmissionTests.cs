using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Tracing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises ordered CPU radiance and metadata publication through complete graphics passes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class WorldProbeCpuSubmissionTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Invalid scalar metadata or one invalid directional lane rejects the complete probe update before any attachment is changed.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)]
    public void UnsupportedCpuPublicationRetainsEveryAttachment(int invalidKind)
    {
        EnsureContextValid();
        using var atlas = new SurfaceLightingWorldProbeFixture();
        Assert.Equal(1, atlas.TryUpload(Result(.25f, .5f), 1024));
        using var radiance = new Assertions.GpuImageSnapshot(atlas.Resources.ProbeRadianceAtlas);
        using var metadata = new Assertions.GpuImageSnapshot(atlas.Resources.ProbeMeta0);
        using var visibility = new Assertions.GpuImageSnapshot(atlas.Resources.ProbeVis0);
        using var distance = new Assertions.GpuImageSnapshot(atlas.Resources.ProbeDist0);
        var invalid = Result(.75f, .9f);
        invalid = invalidKind switch
        {
            0 => invalid with { Confidence = float.NaN },
            1 => invalid with { MeanLogHitDistance = float.PositiveInfinity },
            2 => invalid with { AtlasSamples = [new(0, 0, new Vector3(.75f), 1), new(1, 0, new Vector3(131200), 1)] },
            3 => invalid with { AtlasSamples = [new(0, 0, new Vector3(.75f), 1), new(1, 0, new Vector3(float.NaN), 1)] },
            4 => invalid with { SkyIntensity = float.NaN },
            5 => invalid with { ShortRangeAoDirWorld = Vector3.Zero },
            6 => invalid with { ShortRangeAoDirWorld = new Vector3(float.NaN) },
            _ => invalid with { ShortRangeAoDirWorld = new Vector3(float.MaxValue) }
        };
        Assert.Equal(0, atlas.TryUpload(invalid, 1024));
        ImageAssertions.CompareHistory(atlas.Resources.ProbeRadianceAtlas, radiance).AssertEqual();
        ImageAssertions.CompareHistory(atlas.Resources.ProbeMeta0, metadata).AssertEqual();
        ImageAssertions.CompareHistory(atlas.Resources.ProbeVis0, visibility).AssertEqual();
        ImageAssertions.CompareHistory(atlas.Resources.ProbeDist0, distance).AssertEqual();
    }

    /// <summary>Two-pass publication ignores hostile ambient state, restores it and republishes changed values without clearing neighbors.</summary>
    [Fact]
    public void CpuAtlasPublicationRestoresHostileStateAndPreservesNeighbors()
    {
        EnsureContextValid();
        using var atlas = new SurfaceLightingWorldProbeFixture(2);
        GL.Enable(EnableCap.ScissorTest); GL.Scissor(0, 0, 0, 0); GL.ColorMask(false, false, false, false);
        GL.Enable(EnableCap.DepthTest); GL.DepthFunc(DepthFunction.Never); GL.Viewport(3, 4, 5, 6);
        OpenGlStateCache.Current.InvalidateAll();
        try
        {
            var first = Result(.25f, .5f);
#if DEBUG
            long before = OpenGlStateCache.Current.DrawSubmissions;
#endif
            Assert.Equal(1, atlas.TryUpload(first, 1024));
#if DEBUG
            Assert.Equal(before + 2, OpenGlStateCache.Current.DrawSubmissions);
#endif
            Assert.True(GL.IsEnabled(EnableCap.ScissorTest)); Assert.Equal((int)DepthFunction.Never, GL.GetInteger(GetPName.DepthFunc));
            int[] viewport = new int[4]; GL.GetInteger(GetPName.Viewport, viewport); Assert.Equal(new[] { 3, 4, 5, 6 }, viewport);
            bool[] mask = new bool[4]; GL.GetBoolean(GetPName.ColorWritemask, mask); Assert.All(mask, Assert.False);
            using var pixels = new Assertions.GpuImageSnapshot(atlas.Resources.ProbeRadianceAtlas);
            Assert.InRange(Assertions.GpuLocalRead.Scalar(pixels.Texture, 0), .2499f * LightingStorage.PersistentScale, .2501f * LightingStorage.PersistentScale);
            Assert.InRange(Assertions.GpuLocalRead.Scalar(atlas.Resources.ProbeMeta0, 0), .4999f, .5001f);
            Assert.Equal(0, atlas.TryUpload(Result(.75f, .9f), 1));
            ImageAssertions.CompareHistory(atlas.Resources.ProbeRadianceAtlas, pixels).AssertEqual();
            atlas.InvalidatePrograms();
            Assert.Equal(1, atlas.TryUpload(Result(.75f, .9f), 1024));
            var changed = atlas.Resources.ProbeRadianceAtlas;
            Assert.InRange(Assertions.GpuLocalRead.Scalar(changed, 0), .7499f * LightingStorage.PersistentScale, .7501f * LightingStorage.PersistentScale);
            foreach (var region in new VanillaGraphicsExpanded.Rendering.RHI.RHITextureRegion[] {
                new(new(0),1,0,0,changed.Width-1,1), new(new(0),0,1,0,changed.Width,changed.Height-1) })
            {
                using var previous = new Assertions.GpuImageSnapshot(pixels.Texture, region);
                ImageAssertions.CompareHistory(changed, previous, region).AssertEqual();
            }
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
        finally
        {
            GL.Disable(EnableCap.ScissorTest); GL.Disable(EnableCap.DepthTest); GL.ColorMask(true, true, true, true);
            GL.DepthFunc(DepthFunction.Less); OpenGlStateCache.Current.InvalidateAll();
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
