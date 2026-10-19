using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds.LightingVolume;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks candidate finite view sampling, physical source composition, clipping and native OIT encoding.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudLightingVolumeViewTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>The declared finite lattice emits independent bucket/overall products and applies Q plus cached D once.</summary>
    [Fact]
    public void DeterministicViewUsesPhysicalCachedSourcesAndNativeProducts()
    {
        EnsureShaderTestAvailable();using var scene=new LightingVolumeViewSceneFixture(Programs);
        var settings=new LightingVolumeViewSettings(4,50,0,false,true,true);
        scene.SeedLighting(.25f,.75f,new Vector3(12),15);
        var value=scene.Draw(settings);
        float phase=.64f/(4*MathF.PI*MathF.Pow(1.36f,1.5f));
        float source=.9f*(30*.25f+3*.75f)*phase+12;
        float opacity=1-value[1][0];Assert.InRange(Math.Abs(value[1][0]-MathF.Exp(-2)),0,2e-5f);
        for(int bucket=0;bucket<3;bucket++)
        {
            float expected=1,alpha=1-MathF.Exp(-.5f);
            for(int i=0;i<4;i++)
            {
                float near=i*25,adjusted=near*near*near*(1-near/1000)/250000;
                float bin=MathF.Log(1+adjusted/30),n=(bin-bucket)/.832f;
                float weight=bucket==2&&bin>2?1:MathF.Exp(-n*n);expected*=1-alpha*weight;
            }
            Assert.InRange(Math.Abs(value[0][bucket]-expected),0,2e-5f);
            float[] accumulation=value[3+bucket];
            Assert.InRange(Math.Abs(accumulation[3]-opacity*(1-value[0][bucket])),0,2e-5f);
            Assert.InRange(Math.Abs(accumulation[0]/accumulation[3]-source),0,2e-4f);
        }
        Assert.Equal(0,value[2][0]);Assert.Equal(0,value[2][1]);Assert.Equal(0,value[2][2]);
        Assert.InRange(Math.Abs(value[2][3]-opacity*opacity),0,2e-5f);
        var encoded=scene.Draw(settings,scale:2);
        Assert.Equal(value[0],encoded[0]);Assert.Equal(value[1],encoded[1]);Assert.Equal(value[2],encoded[2]);
        Assert.InRange(Math.Abs(encoded[3][0]-2*value[3][0]),0,2e-5f);
        // User-disabled paths use compiled variants and current fallback, ignoring authored cached fields.
        var disabled=scene.Draw(settings with {SelfShadowing=false,Diffuse=false});
        float fallback=.9f*33*phase+9;
        Assert.InRange(Math.Abs(disabled[3][0]/disabled[3][3]-fallback),0,2e-4f);
        scene.SetMap(0);var empty=scene.Draw(settings);
        Assert.Equal(1,empty[1][0]);Assert.All(empty.Skip(2).SelectMany(x=>x),v=>Assert.Equal(0,v));
        scene.SetMap(1);scene.Atmosphere=scene.Atmosphere with {Solar=new Vector3(float.MaxValue),Lunar=new Vector3(float.MaxValue)};
        var rejected=scene.Draw(settings,succeeds:false);Assert.Equal(1,rejected[1][0]);
        Assert.All(rejected.Skip(2).SelectMany(x=>x),v=>Assert.Equal(0,v));
    }
    /// <summary>Independent clips, thin/out-of-band fallback, gaps, representative aerial response and optical exit remain explicit.</summary>
    [Fact]
    public void NativeSupportDepthsAndMediumUseTheSelectedFiniteSchedule()
    {
        EnsureShaderTestAvailable();using var scene=new LightingVolumeViewSceneFixture(Programs);
        var settings=new LightingVolumeViewSettings(4,50,0,false,false,false);
        var baseline=scene.Draw(settings);float source=baseline[3][0]/baseline[3][3];
        scene.SetDepths(100,50,100);var particles=scene.Draw(settings);
        Assert.InRange(Math.Abs(particles[1][0]-MathF.Exp(-1)),0,2e-5f);
        scene.SetDepths(100,100,50);var water=scene.Draw(settings);Assert.Equal(particles[1],water[1]);
        scene.SetDepths(50,100,100);Assert.Equal(particles[1],scene.Draw(settings)[1]);
        scene.SetDepths(100,100,100);scene.SetMap(1,gap:true);
        Assert.InRange(Math.Abs(scene.Draw(settings)[1][0]-MathF.Exp(-1)),0,2e-5f);
        scene.SetMap(1,lower:-.001f,upper:.001f);Assert.InRange(Math.Abs(scene.Draw(settings)[1][0]-MathF.Exp(-2)),0,2e-5f);
        scene.SetLocalHeight(500);scene.SetMap(1,lower:9.99f,upper:10.01f);
        Assert.InRange(Math.Abs(scene.Draw(settings)[1][0]-MathF.Exp(-2)),0,2e-5f);
        scene.SetLocalHeight(0);
        scene.SetMap(1);scene.SetAerial(3,.25f);var transported=scene.Draw(settings);
        Assert.InRange(Math.Abs(transported[3][0]/transported[3][3]-(source*.75f+3)),0,2e-4f);
        scene.Underwater=1;scene.Fog=new Vector4(.02f,0,0,0);scene.FogColor=Vector3.Zero;
        var local=scene.Draw(settings);float total=0,moment=0,t=1,stepAlpha=1-MathF.Exp(-.5f);
        for(int i=0;i<4;i++) {float w=t*stepAlpha;total+=w;moment+=w*(i*25+12.5f);t*=1-stepAlpha;}
        Assert.InRange(Math.Abs(local[3][0]/local[3][3]-source*MathF.Exp(-.02f*moment/total)),0,2e-4f);
        scene.Underwater=0;scene.SetAerial(0,0);scene.SetMap(100);
        var terminated=scene.Draw(settings with {Stop=.005f,Detail=true});
        Assert.InRange(terminated[1][0],0,.005f);Assert.All(terminated.SelectMany(x=>x),v=>Assert.True(float.IsFinite(v)&&v>=0));
        Assert.InRange(Math.Abs(terminated[0][1]-(1-MathF.Exp(-1/(.832f*.832f)))),0,2e-5f);
        var allSteps=scene.Draw(settings with {Detail=true});Assert.True(allSteps[0][1]<terminated[0][1]);
    }
    #endregion
}
