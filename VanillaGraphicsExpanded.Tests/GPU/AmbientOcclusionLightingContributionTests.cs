using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Connects generated horizon visibility to actual solar BRDF and indirect composition before exposure or atmospheric transport.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
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
        using var scene=new AmbientOcclusionSceneFixture(TestFramework,Programs,width,height,4,roughness:roughness,availability:.5f);
        scene.Render(owner,draw,2);var stages=AmbientOcclusionSceneFixture.ReadStages(owner);
        int pixel=Enumerable.Range(0,width*height).Where(i=>i%width>=width/2+3&&i/width>=height/4&&i/width<3*height/4).MinBy(i=>stages.Reconstructed[i*4]);
        float visibility=stages.Reconstructed[pixel*4];Assert.InRange(visibility,0,.995f);
        using var lighting=new AmbientOcclusionLightingFixture(TestFramework,Programs,scene,lumon,sunlit?8:0,.5f);
        var neutral=lighting.Capture(null);var occluded=lighting.Capture(owner.Texture);
        for(int i=0;i<neutral.Combined.Length;i++) {
            if(i%4==3)continue;
            Assert.InRange(occluded.Direct[i],neutral.Direct[i]-.00001f,neutral.Direct[i]+.00001f);
            Assert.True(float.IsFinite(occluded.Combined[i]));
            Assert.InRange(occluded.Combined[i],-0.00001f,neutral.Combined[i]+.0001f);
        }
        int channel=pixel*4;
        Assert.True(neutral.Diffuse[channel]>.1f);Assert.True(neutral.Specular[channel]>.001f);
        Assert.InRange(occluded.Diffuse[channel]/neutral.Diffuse[channel],visibility-.0001f,visibility+.0001f);
        float[] position=scene.Position.ReadPixels();
        double nv=-position[channel+2]/Math.Sqrt(position[channel]*position[channel]+position[channel+1]*position[channel+1]+position[channel+2]*position[channel+2]);
        double expectedSpecular=Math.Clamp(Math.Pow(nv+visibility,Math.Pow(2,-16*roughness-1))-1+visibility,0,1);
        Assert.InRange(occluded.Specular[channel]/neutral.Specular[channel],expectedSpecular-.0003,expectedSpecular+.0003);
        Assert.True(neutral.Combined[channel]-occluded.Combined[channel]>.0001f);
        if(sunlit)Assert.True(occluded.Direct[channel]>1);else Assert.InRange(occluded.Direct[channel],0,.00001f);

        // Retain the same valid scalar signal while reducing only available indirect energy.
        using var weakScene=new AmbientOcclusionSceneFixture(TestFramework,Programs,width,height,4,roughness:roughness,availability:.005f);
        using var weakLighting=new AmbientOcclusionLightingFixture(TestFramework,Programs,weakScene,lumon,sunlit?8:0,.005f);
        var weakNeutral=weakLighting.Capture(null);var weakOccluded=weakLighting.Capture(owner.Texture);
        double strongLoss=neutral.Combined[channel]-occluded.Combined[channel],weakLoss=weakNeutral.Combined[channel]-weakOccluded.Combined[channel];
        Assert.InRange(weakLoss,strongLoss*.008,strongLoss*.012);
        Assert.InRange(weakOccluded.Diffuse[channel]/weakNeutral.Diffuse[channel],visibility-.001f,visibility+.001f);

        float[] wrongDepth=(float[])stages.Reconstructed.Clone();for(int i=1;i<wrongDepth.Length;i+=4)wrongDepth[i]+=20;
        using var rejected=TestFramework.CreateTexture(width,height,PixelInternalFormat.Rgba32f,wrongDepth);
        var unmatched=lighting.Capture(rejected);
        for(int c=0;c<3;c++)Assert.InRange(unmatched.Combined[channel+c],neutral.Combined[channel+c]-.0001f,neutral.Combined[channel+c]+.0001f);
        int rawChannel=((pixel/width/2)*((width+1)/2)+(pixel%width/2))*4;
        log.WriteLine($"AO lighting lumon={lumon} sunlit={sunlit} roughness={roughness}: receiver=({pixel%width},{pixel/width}) rawAtReceiver={stages.Raw[rawChannel]:R} filteredAtReceiver={stages.Filtered[rawChannel]:R} finalVisibility={visibility:R}; direct={occluded.Direct[channel]:R}; diffuse neutral/occluded={neutral.Diffuse[channel]:R}/{occluded.Diffuse[channel]:R}; specular neutral/occluded={neutral.Specular[channel]:R}/{occluded.Specular[channel]:R}; combined neutral/occluded={neutral.Combined[channel]:R}/{occluded.Combined[channel]:R}; strongLoss={strongLoss:R} weakLoss={weakLoss:R}; rejectedDepthCombined={unmatched.Combined[channel]:R}. Linear pre-exposure radiance; solar8 or0, indirect availability .5 or .005; generated visibility unchanged.");
    }
    #endregion
}
