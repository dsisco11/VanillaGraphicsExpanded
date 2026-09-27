using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks temporal rejection across changes to displaced geometry.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class TerrainDisplacementTemporalTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Temporal transition regression
    /// <summary>A frame that removes prior displacement must reject the previous displaced history.</summary>
    [Theory]
    [InlineData("adaptiveNeutral")]
    [InlineData("adaptiveFaded")]
    [InlineData("adaptiveMissing")]
    [InlineData("adaptiveZeroAmplitude")]
    public void ReturningToFlatSurfaceRejectsDisplacedHistory(string mode)
    {
        EnsureContextValid();
        using var scope=new TerrainDetailWorkload();
        scope.Select("adaptiveNear");scope.Draw();
        float[] before=scope.Read();
        Assert.True(before[(128*256+128)*4]<-.02f);
        Assert.True(before[(128*256+128)*4+1]>.9f);
        scope.Select(mode);scope.Draw();
        float[] after=scope.Read();
        Assert.InRange(after[(128*256+128)*4],-.00001f,.00001f);
        Assert.True(after[(128*256+128)*4+1]>.9f,"Reactive frame must reject history when a previously displaced surface becomes flat.");
    }
    /// <summary>Unchanged geometry and draws outside the displaced route do not reject temporal history.</summary>
    [Theory]
    [InlineData("adaptiveNear", false, true)]
    [InlineData("adaptiveNear", true, false)]
    [InlineData("triangles", true, true)]
    [InlineData("identity2", true, true)]
    public void StableOrIneligibleGeometryKeepsHistory(string mode, bool reactive, bool eligible)
    {
        EnsureContextValid();
        using var scope = new TerrainDetailWorkload();
        scope.Select(mode, reactive, eligible);
        scope.Draw();
        float[] result = scope.Read();
        Assert.Equal(0f, result[(128 * 256 + 128) * 4 + 1]);
        if (mode == "adaptiveNear" && eligible)
            Assert.True(result[(128 * 256 + 128) * 4] < -.02f, "Stable history must not disable physical displacement.");
        else
            Assert.InRange(result[(128 * 256 + 128) * 4], -.00001f, .00001f);
    }
    #endregion

}
