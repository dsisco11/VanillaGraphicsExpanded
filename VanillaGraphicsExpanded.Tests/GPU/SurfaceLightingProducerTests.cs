using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies separated lighting terms and previous-generation multi-bounce transport numerically.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category","GPU")]
public sealed class SurfaceLightingProducerTests : RenderTestBase
{
    /// <summary>Uses the isolated material and real SPIR-V graphics context.</summary>
    public SurfaceLightingProducerTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Lighting terms
    /// <summary>Direct irradiance excludes receiver reflectance; outgoing radiance applies it once.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.25f)]
    [InlineData(.5f)]
    public void DirectAlbedoAndValidDarkness(float albedo)
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(albedo);
        room.Seed();
        float rho=MathF.Round(albedo*255)/255;
        AssertClose(32,room.Read(room.Snapshot.DirectIrradiance)[0]);
        AssertClose(32*rho/MathF.PI,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        Assert.Equal(1,room.Read(room.Snapshot.OutgoingRadiance)[3]);
        Assert.Equal(0,room.Read(room.Snapshot.IndirectIrradiance)[3]);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Explicit emission is HDR, independent of albedo, and replaces aggregate block light globally.</summary>
    [Fact]
    public void EmissionPolicyExcludesDuplicateBlockLightingButRetainsSun()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(0,32,12);
        room.EmissionPolicy=true; room.Seed();
        AssertClose(0,room.Read(room.Snapshot.DirectIrradiance)[0]);
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        room.Bounce();
        AssertClose(12*MathF.PI,room.Read(room.Snapshot.IndirectIrradiance)[0]);
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        room.SunLight=16; room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        AssertClose(16,room.Read(room.Snapshot.DirectIrradiance)[0]);
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance)[0]);
    }

    /// <summary>A uniform enclosure follows the diffuse bounce recurrence without same-dispatch feedback.</summary>
    [Fact]
    public void IndirectUsesPreviousOutgoingGenerationAndRemainsBounded()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(.25f);
        room.Seed();
        float rho=64f/255f, expected=32*rho/MathF.PI;
        for(int bounce=0;bounce<6;bounce++)
        {
            room.Bounce();
            // Temporal history averages completed samples of the evolving published field.
            float incident=room.Read(room.Snapshot.IndirectIrradiance)[0];
            Assert.True(incident>0);
            if(bounce==0) AssertClose(32*rho,incident);
            float outgoing=room.Read(room.Snapshot.OutgoingRadiance)[0];
            Assert.InRange(outgoing,expected-.04f,32*rho/(MathF.PI*(1-rho))+.04f);
            expected=outgoing;
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Removal and restoration clear dependent bounce history within one complete seed update.</summary>
    [Fact]
    public void LightChangesDiscardIndirectHistoryAndPublishValidBlack()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        room.Seed();room.Bounce();
        Assert.True(room.Read(room.Snapshot.IndirectIrradiance)[0]>0);
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        AssertClose(0,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        Assert.Equal(1,room.Read(room.Snapshot.OutgoingRadiance)[3]);
        Assert.Equal(0,room.Read(room.Snapshot.IndirectIrradiance)[3]);
        room.Bounce();AssertClose(0,room.Read(room.Snapshot.IndirectIrradiance)[0]);
        room.BlockLight=32;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        Assert.True(room.Read(room.Snapshot.OutgoingRadiance)[0]>0);
    }

    /// <summary>Unavailable cached hit lighting preserves the previously published outgoing generation.</summary>
    [Fact]
    public void MissingPagesDoNotPublishPartialBounceOrAddWeight()
    {
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture();
        room.Seed();
        var prior=room.Snapshot;
        float[] before=room.Read(prior.OutgoingRadiance);
        room.WithholdPages();
        Assert.False(room.TryIncompleteBounce());
        Assert.Equal(prior.Generation,room.Snapshot.Generation);
        Assert.Equal(before,room.Read(room.Snapshot.OutgoingRadiance));
        Assert.Equal(0,room.Read(room.Snapshot.IndirectIrradiance)[3]);
    }

    /// <summary>Allows the documented half-float quantization error in engine-relative units.</summary>
    private static void AssertClose(float expected,float actual)=>Assert.InRange(actual,expected-.04f,expected+.04f);
    #endregion
}
