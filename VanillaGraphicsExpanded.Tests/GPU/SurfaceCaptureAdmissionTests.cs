using VanillaGraphicsExpanded.LumOn.Scene;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks capture dependency caching against real publication and coverage changes.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceCaptureAdmissionTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
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
        EnsureContextValid();using var material=new ScopedPbrMaterialFixture();
        material.SetReadiness(true,true,new System.Numerics.Vector3(.25f),0);
        var materials=new TraceGeometryMaterials();uint materialId=materials.Resolve(material.Cube);
        uint word=kind|(hasMaterial?materialId<<2:0u);
        using var geometry=new SharedTraceGeometryFixture(TraceGeometryCoverage.Plan(new(0,32,0),false,32,256),materials,(_,_,_)=>new(word,0,0));
        geometry.Publish();
        var admission=new LumonSceneCaptureAdmission();admission.SetScene(geometry.Scene);
        Assert.Equal(available,admission.CanAdmit(0,7,1,new(0,1,0),1));
    }

    /// <summary>Unchanged rejected inputs stay deferred while publication, ownership and coverage changes invalidate their token.</summary>
    [Fact]
    public void DependencyCacheTracksPublicationCoverageAndPhysicalOwnership()
    {
        EnsureContextValid();
        var plan=TraceGeometryCoverage.Plan(new(0,32,0),false,32,256);
        using var geometry=new SharedTraceGeometryFixture(plan,new TraceGeometryMaterials(),(_,_,_)=>new(1,0,0));
        geometry.Publish();
        var admission=new LumonSceneCaptureAdmission();admission.SetScene(geometry.Scene);
        Assert.True(admission.CanAdmit(0,7,1,new(0,1,0),1));
        long checks=admission.Checks;
        admission.Reject(0);
        for(int retry=0;retry<16;retry++)Assert.False(admission.CanAdmit(0,7,1,new(0,1,0),1));
        Assert.Equal(checks,admission.Checks);
        // A new slot owner must not inherit the prior owner's rejection.
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        Assert.Equal(checks+1,admission.Checks);
        geometry.Dirty();
        Assert.False(admission.CanAdmit(0,7,2,new(0,1,0),1));
        geometry.Publish();
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        geometry.Move(TraceGeometryCoverage.Plan(new(32,32,0),false,32,256));
        Assert.False(admission.CanAdmit(0,7,2,new(0,1,0),1));
        geometry.Move(plan);geometry.Publish();
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        admission.Reject(0);
        using var replacement=new SharedTraceGeometryFixture(plan,new TraceGeometryMaterials(),(_,_,_)=>new(1,0,0));
        replacement.Publish();admission.SetScene(replacement.Scene);
        Assert.True(admission.CanAdmit(0,7,2,new(0,1,0),1));
        admission.Prune((_,_,_)=>false);Assert.Equal(0,admission.Count);
        admission.Clear();Assert.Equal(0,admission.Checks);Assert.Equal(0,admission.Deferred);
        Assert.False(admission.CanAdmit(0,7,2,new(0,1,0),1));
    }
    #endregion
}
