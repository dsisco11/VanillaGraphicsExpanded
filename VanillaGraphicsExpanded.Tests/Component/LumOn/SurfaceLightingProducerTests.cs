using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.Numerics;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Verifies stored direct, outgoing, and indirect lighting values and previous-generation bounce history.</summary>
[Collection("NearFieldMaterialCapture")]
[Trait("Category", "GpuIntegration")]
public sealed class SurfaceLightingProducerTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the isolated material and real SPIR-V graphics context.</summary>
    public SurfaceLightingProducerTests(HeadlessGLFixture fixture):base(fixture) { }

    #region Lighting terms
    /// <summary>Stored lighting attachments use fixed scales without scaling metadata.</summary>
    [Fact]
    public void PersistentDestinationsUseFixedScaleWithoutScalingMetadata()
    {
        // Arrange
        EnsureContextValid();
        using var room = new SurfaceLightingEnclosureFixture(ImageAssertions, .25f);
        // Act
        room.Seed();
        float rho = GgxHemisphereReference.CapturedDiffuse(.25f);
        // Assert
        AssertClose(32 * LightingStorage.PersistentScale, room.ReadStored(room.Snapshot.DirectIrradiance)[0]);
        AssertClose(32 * rho / MathF.PI * LightingStorage.PersistentScale,
            room.ReadStored(room.Snapshot.OutgoingRadiance)[0]);
        Assert.Equal(1, room.ReadStored(room.Snapshot.DirectIrradiance)[3]);
        Assert.Equal(1, room.ReadStored(room.Snapshot.OutgoingRadiance)[3]);
        // Act
        room.Bounce();
        // Assert
        AssertClose(32 * rho * LightingStorage.PersistentScale, room.ReadStored(room.Snapshot.IndirectIrradiance)[0]);
        Assert.Equal(1, room.ReadStored(room.Snapshot.IndirectIrradiance)[3]);
    }

    /// <summary>An individually representable hit can generate unsupported incident lux; rejected updates preserve RGB and history weight.</summary>
    [Theory]
    [InlineData(400000f)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NaN)]
    public void UnsupportedIndirectEstimateRetainsPreviousHistory(float incidentRadiance)
    {
        // Arrange
        EnsureContextValid();
        using var room = new SurfaceLightingEnclosureFixture(ImageAssertions, .25f);
        room.Seed();
        room.SetIndirect(32, 4);
        room.SetIncidentRadiance(incidentRadiance);
        float[] before = room.ReadStored(room.Snapshot.IndirectIrradiance);
        long generation = room.Snapshot.Generation;
        // Act
        Assert.False(room.BounceSample());
        // Assert
        Assert.Equal(before, room.ReadStored(room.Snapshot.IndirectIrradiance));
        Assert.Equal(generation, room.Snapshot.Generation);
    }

    /// <summary>Material emission remains suppressed on faces buried inside adjacent solid geometry.</summary>
    [Fact]
    public void HiddenEmissiveFacesPublishValidZero()
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, 0,0,12);
        room.EmissionPolicy=true;
        // Act
        room.Seed();
        // Assert
        Assert.Equal(new float[]{0,0,0,1},room.Read(room.Snapshot.OutgoingRadiance,linear:0));
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance,linear:27)[0]);
        // Act
        room.Bounce();
        // Assert
        Assert.Equal(new float[]{0,0,0,1},room.Read(room.Snapshot.OutgoingRadiance,linear:0));
    }

    /// <summary>Direct irradiance excludes receiver reflectance; outgoing radiance applies it once.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.25f)]
    [InlineData(.5f)]
    public void DirectAlbedoAndValidDarkness(float albedo)
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, albedo);
        // Act
        room.Seed();
        float rho=GgxHemisphereReference.CapturedDiffuse(albedo);
        // Assert
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
        // Arrange
        EnsureContextValid();
        using var atmosphere = new ScopedAtmosphereLighting(new System.Numerics.Vector3(32 / MathF.PI));
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, 0,32,12);
        room.EmissionPolicy=true;
        // Act
        room.Seed();
        // Assert
        AssertClose(0,room.Read(room.Snapshot.DirectIrradiance)[0]);
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        // Act
        room.Bounce();
        // Assert
        AssertClose(12*MathF.PI,room.Read(room.Snapshot.IndirectIrradiance)[0]);
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        // Act
        room.SunLight=16; room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        // Assert
        AssertClose(32 * (128f / 255),room.Read(room.Snapshot.DirectIrradiance)[0]);
        AssertClose(12,room.Read(room.Snapshot.OutgoingRadiance)[0]);
    }

    /// <summary>A uniform enclosure follows the diffuse bounce recurrence without same-dispatch feedback.</summary>
    [Fact]
    public void IndirectUsesPreviousOutgoingGenerationAndRemainsBounded()
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions, .25f);
        room.Seed();
        float rho=GgxHemisphereReference.CapturedDiffuse(.25f), expected=32*rho/MathF.PI;
        // Act
        for(int bounce=0;bounce<6;bounce++)
        {
            room.Bounce();
            // Temporal history averages completed samples of the evolving published field.
            float incident=room.Read(room.Snapshot.IndirectIrradiance)[0];
            // Assert
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
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        // Act
        room.Seed();room.Bounce();
        // Assert the initial lit baseline before applying a source change.
        Assert.True(room.Read(room.Snapshot.IndirectIrradiance)[0]>0);
        // Act
        room.BlockLight=0;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        // Assert
        AssertClose(0,room.Read(room.Snapshot.OutgoingRadiance)[0]);
        Assert.Equal(1,room.Read(room.Snapshot.OutgoingRadiance)[3]);
        Assert.Equal(0,room.Read(room.Snapshot.IndirectIrradiance)[3]);
        // Act
        room.Bounce();
        // Assert
        AssertClose(0,room.Read(room.Snapshot.IndirectIrradiance)[0]);
        // Act
        room.BlockLight=32;room.Geometry.Dirty();room.Geometry.Publish();room.Seed();
        // Assert
        Assert.True(room.Read(room.Snapshot.OutgoingRadiance)[0]>0);
    }

    /// <summary>Unavailable cached hit lighting preserves the previously published outgoing generation.</summary>
    [Fact]
    public void MissingPagesDoNotPublishPartialBounceOrAddWeight()
    {
        // Arrange
        EnsureContextValid();
        using var room=new SurfaceLightingEnclosureFixture(ImageAssertions);
        room.Seed();
        var prior=room.Snapshot;
        float[] before=room.Read(prior.OutgoingRadiance);
        // Act
        room.WithholdPages();
        Assert.False(room.TryIncompleteBounce());
        // Assert
        Assert.Equal(prior.Generation,room.Snapshot.Generation);
        Assert.Equal(before,room.Read(room.Snapshot.OutgoingRadiance));
        Assert.Equal(0,room.Read(room.Snapshot.IndirectIrradiance)[3]);
    }

    #endregion
    #endregion

    #region Private
    /// <summary>Allows the documented half-float quantization error in engine-relative units.</summary>
    private static void AssertClose(float expected,float actual)=>Assert.InRange(actual,expected-.04f,expected+.04f);
    #endregion
}
