using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.Tests.Support.PBR.Materials;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.Support.Lighting;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks capture dependency caching against real publication and coverage changes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceCaptureAdmissionTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    #region Admission dependencies
    /// <summary>Air and material-backed geometry remain capturable, while unpublished or materialless solid cells defer.</summary>
    [Theory]
    [InlineData(0u,false,false)]
    [InlineData(1u,false,true)]
    [InlineData(2u,false,false)]
    [InlineData(2u,true,true)]
    [InlineData(3u,true,true)]
    public void SourceKindsMatchCapturePrerequisites(uint kind,bool hasMaterial,bool available)
    {
        // Arrange: Encode one source kind and its material readiness in a published shared scene.
        EnsureContextValid();using var material=new ScopedPbrMaterialFixture();
        material.SetReadiness(true,true,new System.Numerics.Vector3(.25f),0);
        var materials=new TraceGeometryMaterials();uint materialId=materials.Resolve(material.Cube);
        uint word=kind|(hasMaterial?materialId<<2:0u);
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,32,0),false,32,256),materials,(_,_,_)=>new(word,0,0));
        geometry.Publish();
        var admission=new LumonSceneCaptureAdmission();admission.SetScene(geometry.Scene);
        // Act: Ask the production admission policy about that source coordinate.
        // Assert: Only source kinds with the required publication and material prerequisites are admitted.
        Assert.Equal(available,admission.CanAdmit(0,7,1,new(0,1,0),1));
    }

    /// <summary>Unchanged rejected inputs stay deferred while publication, ownership and coverage changes invalidate their token.</summary>
    [Fact]
    public void DependencyCacheTracksPublicationCoverageAndPhysicalOwnership()
    {
        // Arrange: Publish an initially admissible scene and count dependency checks at the admission boundary.
        EnsureContextValid();
        var plan=TraceGeometryCoverage.Plan(new(0,32,0),false,32,256);
        using var geometry=new SharedTraceGeometryFixture(plan,new TraceGeometryMaterials(),(_,_,_)=>new(1,0,0));
        geometry.Publish();
        var admission=new LumonSceneCaptureAdmission();admission.SetScene(geometry.Scene);
        Assert.True(admission.CanAdmit(0,7,1,new(0,1,0),1));
        long checks=admission.Checks;
        // Act: Reject the source and repeat the unchanged request before changing any dependency.
        admission.Reject(0);
        for(int retry=0;retry<16;retry++)Assert.False(admission.CanAdmit(0,7,1,new(0,1,0),1));
        // Assert: Repeated rejected requests reuse the original dependency token.
        Assert.Equal(checks,admission.Checks);
        // Act: Change physical slot ownership; a new owner must be checked independently.
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        Assert.Equal(checks+1,admission.Checks);
        // Act: Dirty and republish the source under the same physical owner.
        geometry.Dirty();
        Assert.False(admission.CanAdmit(0,7,2,new(0,1,0),1));
        geometry.Publish();
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        // Act: Move the coverage away, then back and publish the restored window.
        geometry.Move(TraceGeometryCoverage.Plan(new(32,32,0),false,32,256));
        Assert.False(admission.CanAdmit(0,7,2,new(0,1,0),1));
        geometry.Move(plan);geometry.Publish();
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        // Act: Replace the scene identity, then prune and clear the cached dependency records.
        admission.Reject(0);
        using var replacement=new SharedTraceGeometryFixture(plan,new TraceGeometryMaterials(),(_,_,_)=>new(1,0,0));
        replacement.Publish();admission.SetScene(replacement.Scene);
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        admission.Prune((_,_,_)=>false);Assert.Equal(0,admission.Count);
        admission.Clear();Assert.Equal(0,admission.Checks);Assert.Equal(0,admission.Deferred);
        // Assert: A cleared cache performs a fresh check on the next request.
        Assert.False(admission.CanAdmit(0,7,2,new(0,1,0),1));
    }
    #endregion
    #endregion
}
