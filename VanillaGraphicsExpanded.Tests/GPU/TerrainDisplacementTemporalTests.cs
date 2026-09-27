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
    #endregion

}
