using System.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Connects generated horizon visibility to actual solar BRDF and indirect composition before exposure or atmospheric transport.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AmbientOcclusionLightingContributionTests(HeadlessGLFixture fixture,ITestOutputHelper log) : LumOnShaderFunctionalTestBase(fixture)
{


    #region Public API
    /// <summary>Separates indoor/sunlit direct, diffuse and specular changes and distinguishes weak illumination from rejected AO.</summary>
    [Theory]
    [InlineData(false,false,.05f)] [InlineData(false,true,.05f)]
    [InlineData(true,false,.05f)] [InlineData(true,true,.05f)]
    [InlineData(false,false,.5f)] [InlineData(false,true,.5f)]
    [InlineData(true,false,.5f)] [InlineData(true,true,.5f)]
    public void GeneratedVisibilityChangesOnlyAvailableIndirectLighting(bool lumon,bool sunlit,float roughness)
    {
        EnsureShaderTestAvailable();
        using var owner=new AmbientOcclusionPass();using var draw=new PostprocessDraw();
        const int width=257,height=129;
        using var scene=new AmbientOcclusionSceneFixture(ImageAssertions, TestFramework,Programs,width,height,4,roughness:roughness,availability:.5f);
        scene.Render(owner,draw,2);using var stages=AmbientOcclusionSceneFixture.CaptureStages(owner);
        int firstX=width/2+3,firstY=height/4;
        var selected=ImageAssertions.Selection.Extremum(stages.Reconstructed,0,false,
            new(new(),firstX,firstY,0,width-firstX,3*height/4-firstY));
        int receiverX=firstX+selected.X,receiverY=firstY+selected.Y,pixel=receiverY*width+receiverX;
        float visibility=selected.Value;Assert.InRange(visibility,0,.995f);
        using var lighting=new AmbientOcclusionLightingFixture(ImageAssertions, TestFramework,Programs,scene,lumon,sunlit?8:0,.5f);
        using var neutral=lighting.Capture(null);using var occluded=lighting.Capture(owner.Texture);
        ImageAssertions.Predicates.Predicate(occluded.Direct, AoPredicates.AmbientOcclusionLightingContributionTests_GeneratedVisibilityChangesOnlyAvailableIndirectLighting,neutral.Direct,occluded.Combined,neutral.Combined).AssertEqual();
        var neutralPixel=(Direct:Receiver(neutral.Direct),Diffuse:Receiver(neutral.Diffuse),Specular:Receiver(neutral.Specular),Combined:Receiver(neutral.Combined));
        var occludedPixel=(Direct:Receiver(occluded.Direct),Diffuse:Receiver(occluded.Diffuse),Specular:Receiver(occluded.Specular),Combined:Receiver(occluded.Combined));
        Assert.True(neutralPixel.Diffuse.X>.1f);Assert.True(neutralPixel.Specular.X>.001f);
        Assert.InRange(occludedPixel.Diffuse.X/neutralPixel.Diffuse.X,visibility-.0001f,visibility+.0001f);
        Vector4 position=Receiver(scene.Position);
        double nv=-position.Z/Math.Sqrt(position.X*position.X+position.Y*position.Y+position.Z*position.Z);
        double expectedSpecular=Math.Clamp(Math.Pow(nv+visibility,Math.Pow(2,-16*roughness-1))-1+visibility,0,1);
        Assert.InRange(occludedPixel.Specular.X/neutralPixel.Specular.X,expectedSpecular-.0003,expectedSpecular+.0003);
        Assert.True(neutralPixel.Combined.X-occludedPixel.Combined.X>.0001f);
        if(sunlit)Assert.True(occludedPixel.Direct.X>1);else Assert.InRange(occludedPixel.Direct.X,0,.00001f);

        // Retain the same valid scalar signal while reducing only available indirect energy.
        // The weak scene owns fresh inputs and reuses the already prepared immutable AO programs.
        using var weakScene=new AmbientOcclusionSceneFixture(ImageAssertions, TestFramework,Programs,width,height,4,
            roughness:roughness,availability:.005f,sharedPrograms:scene.ScenePrograms);
        using var weakLighting=new AmbientOcclusionLightingFixture(ImageAssertions, TestFramework,Programs,weakScene,lumon,sunlit?8:0,.005f);
        using var weakNeutral=weakLighting.Capture(null);using var weakOccluded=weakLighting.Capture(owner.Texture);
        var weakNeutralPixel=(Combined:Receiver(weakNeutral.Combined),Diffuse:Receiver(weakNeutral.Diffuse));
        var weakOccludedPixel=(Combined:Receiver(weakOccluded.Combined),Diffuse:Receiver(weakOccluded.Diffuse));
        double strongLoss=neutralPixel.Combined.X-occludedPixel.Combined.X,weakLoss=weakNeutralPixel.Combined.X-weakOccludedPixel.Combined.X;
        Assert.InRange(weakLoss,strongLoss*.008,strongLoss*.012);
        Assert.InRange(weakOccludedPixel.Diffuse.X/weakNeutralPixel.Diffuse.X,visibility-.001f,visibility+.001f);

        var projection = ImageAssertions.Projections;
        using var wrongDepth=projection.Apply(stages.Reconstructed, AoProjections.AmbientOcclusionLightingContributionTests_GeneratedVisibilityChangesOnlyAvailableIndirectLighting_2);
        using var retained=new GpuImageSnapshot(wrongDepth);using var rejected=retained.ToPlane();
        using var unmatched=lighting.Capture(rejected);
        var unmatchedPixel=Receiver(unmatched.Combined);
        for(int c=0;c<3;c++)Assert.InRange(unmatchedPixel[c],neutralPixel.Combined[c]-.0001f,neutralPixel.Combined[c]+.0001f);
        int rawChannel=((pixel/width/2)*((width+1)/2)+(pixel%width/2))*4;
        log.WriteLine($"AO lighting lumon={lumon} sunlit={sunlit} roughness={roughness}: receiver=({pixel%width},{pixel/width}) rawAtReceiver={GpuLocalRead.Scalar(stages.Raw,rawChannel):R} filteredAtReceiver={GpuLocalRead.Scalar(stages.Filtered,rawChannel):R} finalVisibility={visibility:R}; direct={occludedPixel.Direct.X:R}; diffuse neutral/occluded={neutralPixel.Diffuse.X:R}/{occludedPixel.Diffuse.X:R}; specular neutral/occluded={neutralPixel.Specular.X:R}/{occludedPixel.Specular.X:R}; combined neutral/occluded={neutralPixel.Combined.X:R}/{occludedPixel.Combined.X:R}; strongLoss={strongLoss:R} weakLoss={weakLoss:R}; rejectedDepthCombined={unmatchedPixel.X:R}. Linear pre-exposure radiance; solar8 or0, indirect availability .5 or .005; generated visibility unchanged.");
        /// <summary>Reads only the selected receiver tuple, independent of the complete-domain lighting predicates.</summary>
        Vector4 Receiver(GpuTexture image)
        {
            float[] values=image.ReadPixelsRegion(receiverX,receiverY,1,1);
            Assert.Equal(4,values.Length);return new(values[0],values[1],values[2],values[3]);
        }
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-ao", 0, 0);
        programs.Prepare("predicate-observation", 0, 0);
        programs.Prepare("projection-ao", 0, 0);
    }
    #endregion
}
