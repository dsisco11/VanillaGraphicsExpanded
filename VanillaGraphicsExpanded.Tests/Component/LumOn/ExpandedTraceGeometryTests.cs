using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Support.PBR.Materials;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks real surface capture outside the old coverage and after bounded ring reuse.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class ExpandedTraceGeometryTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    #region Expanded capture
    /// <summary>A patch beyond the old 64-block half-width becomes capturable and survives correct ring retirement.</summary>
    [Theory]
    [InlineData(192,-16777216)] [InlineData(256,16777216)]
    public void DistantSurfaceCapturesAcrossMovementAndSlotReuse(int resolution,int anchor)
    {
        // Arrange: Publish a distant solid patch at either signed large-world coordinate.
        EnsureContextValid();
        using var material=new ScopedPbrMaterialFixture();material.SetReadiness(true,true);
        var materials=new TraceGeometryMaterials();uint id=materials.Resolve(material.Cube);
        var plan=TraceGeometryCoverage.Plan(new(anchor,128,0),true,resolution,256);
        using var geometry=new SharedTraceGeometryFixture(plan,materials,(_,_,_)=>new(2 | id<<2,0,0));
        // Act: Capture before and after overlapping movement, then retire the old physical slot.
        geometry.Publish(700);
        using var page=new SharedSurfacePageFixture(anchor+80);
        // Assert: The first two windows overlap; a disjoint window fails until its new slot is published.
        Assert.True(page.Capture(geometry.Scene));
        Assert.InRange(geometry.Scene.CaptureIdentityBytes,1,((long)plan.Resolution*plan.Resolution*plan.Resolution)<<2);

        // Overlap remains available when the camera crosses a publication-cell boundary.
        // Act: Move by one chunk into overlapping coverage and retry the same page.
        geometry.Move(TraceGeometryCoverage.Plan(new(anchor+16,128,0),true,resolution,256));
        Assert.True(page.Capture(geometry.Scene));
        // Act: Move fully out of range, publish the new slot, and create a page at its distant location.
        geometry.Move(TraceGeometryCoverage.Plan(new(anchor+1024,128,0),true,resolution,256));
        Assert.False(page.Capture(geometry.Scene));
        geometry.Publish(700);
        using var replacement=new SharedSurfacePageFixture(anchor+1104);
        Assert.True(replacement.Capture(geometry.Scene));
        Assert.False(page.Capture(geometry.Scene));
    }
    #endregion
    #endregion
}
