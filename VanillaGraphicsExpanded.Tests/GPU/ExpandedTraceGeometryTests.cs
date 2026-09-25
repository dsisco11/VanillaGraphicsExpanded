using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks real surface capture outside the old coverage and after bounded ring reuse.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class ExpandedTraceGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Expanded capture
    /// <summary>A patch beyond the old 64-block half-width becomes capturable and survives correct ring retirement.</summary>
    [Theory]
    [InlineData(192,-16777216)] [InlineData(256,16777216)]
    public void DistantSurfaceCapturesAcrossMovementAndSlotReuse(int resolution,int anchor)
    {
        EnsureContextValid();
        using var material=new ScopedPbrMaterialFixture();material.SetReadiness(true,true);
        var materials=new TraceGeometryMaterials();uint id=materials.Resolve(material.Cube);
        var plan=TraceGeometryCoverage.Plan(new(anchor,128,0),true,resolution,256);
        using var geometry=new SharedTraceGeometryFixture(plan,materials,(_,_,_)=>new(2 | id<<2,0,0));
        geometry.Publish(700);
        using var page=new SharedSurfacePageFixture(anchor+80);
        Assert.True(page.Capture(geometry.Scene));
        Assert.InRange(geometry.Scene.CaptureIdentityBytes,1,((long)plan.Resolution*plan.Resolution*plan.Resolution)<<2);

        // Overlap remains available when the camera crosses a publication-cell boundary.
        geometry.Move(TraceGeometryCoverage.Plan(new(anchor+16,128,0),true,resolution,256));
        Assert.True(page.Capture(geometry.Scene));
        geometry.Move(TraceGeometryCoverage.Plan(new(anchor+1024,128,0),true,resolution,256));
        Assert.False(page.Capture(geometry.Scene));
        geometry.Publish(700);
        using var replacement=new SharedSurfacePageFixture(anchor+1104);
        Assert.True(replacement.Capture(geometry.Scene));
        Assert.False(page.Capture(geometry.Scene));
    }
    #endregion
}
