using System.Numerics;
using System.Text.Json;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks sample-boundary termination and records transport differences without approximation acceptance gates.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudTerminationTests(HeadlessGLFixture fixture,ITestOutputHelper output):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Every retained step updates all optical products and support before the configured strict predicate.</summary>
    [Theory]
    [InlineData(100f,.005f,1)][InlineData(100f,0f,4)]
    [InlineData(.02f,.005f,4)][InlineData(.02f,0f,4)]
    public void FirstSaturatedSampleAndDisabledExitKeepExactBookkeeping(float extinction,float threshold,int steps)
    {
        VerifyUniformRay(extinction,threshold,steps);
    }

    /// <summary>Equality does not satisfy the strict predicate, so the next step must still contribute.</summary>
    [Fact]
    public void ThresholdEqualityContinuesThroughTheNextSample()
    {
        const float threshold=1f/256,step=37.5f;
        float extinction=-MathF.Log(threshold)*50/step;
        float alpha=1-MathF.Exp(-(extinction/50)*step);
        Assert.Equal(threshold,1-alpha);
        VerifyUniformRay(extinction,threshold,2);
    }

    /// <summary>Foreground and liquid clips bound contributing support even for an inside-cloud saturated ray.</summary>
    [Theory]
    [InlineData("terrain",0f)][InlineData("terrain",.005f)]
    [InlineData("liquid",0f)][InlineData("liquid",.005f)]
    [InlineData("visibility",0f)][InlineData("visibility",.005f)]
    public void ClippedInsideRayHasNoResidualSupportMarch(string scene,float threshold)
    {
        EnsureShaderTestAvailable();
        var settings=CloudVolumeSettings.Default with {ViewRaySampleMax=4,ViewRaySampleMin=4,StopTransmittance=threshold};
        using var view=CreateScene(1,1,settings);
        view.Capture(0,100,10,1,0,"static");
        view.SetUniformDepthClips(scene=="terrain"?20:1000,scene=="visibility"?20:1000,scene=="liquid"?60:1000);
        view.Render(0,false);
        var raw=view.ReadTraceRecord();
        float clip=scene=="liquid"?60:20,expectedFar=threshold==0?clip:clip/4;
        Close(0,raw[2][0]);
        Assert.InRange(MathF.Abs(raw[2][1]-expectedFar),0,.01f);
        Assert.InRange(raw[2][1],0,clip+.01f);
        float attenuation=1-(1-MathF.Exp(-(100f/50)*(clip/4)));
        float expectedTransmission=threshold==0?MathF.Pow(attenuation,4):attenuation;
        Assert.InRange(MathF.Abs(raw[0][3]-expectedTransmission),0,2e-7f);
    }

    /// <summary>Paired default/disabled captures report each optical and depth channel without bounding the approximation difference.</summary>
    [Theory]
    [InlineData("thin")][InlineData("thick")][InlineData("bright")][InlineData("hdr")][InlineData("inside")]
    [InlineData("terrain")][InlineData("liquid")][InlineData("moving-edge")]
    [InlineData("particle-before")][InlineData("particle-within")][InlineData("particle-behind")]
    [InlineData("aerial")][InlineData("underwater")]
    public void TerminationDifferencesAreDiagnosticAcrossSceneContracts(string scenario)
    {
        EnsureShaderTestAvailable();
        using var candidate=CreateScene(9,7,CloudVolumeSettings.Default);
        using var disabled=CreateScene(9,7,CloudVolumeSettings.Default with {StopTransmittance=0});
        float extinction=scenario=="thin"?.015f:20;
        float environment=scenario is "bright" or "hdr"?1000000:10;
        float scale=scenario=="hdr"?.125f:1;
        string scene=scenario is "thin" or "thick" or "inside" or "bright" or "hdr"?"homogeneous":scenario;
        foreach(var view in new[]{candidate,disabled})
        {
            view.Capture(0,extinction,environment,scale,0,scene);
            if(scenario=="bright")
            {
                var staged=view.SetDirectionalLights(Vector3.UnitY,-Vector3.UnitY,3,solarIlluminance:300000,lunarIlluminance:3000);
                Assert.True(staged.Solar.X>0&&staged.Lunar.X>0);
            }
            view.Render(0,false);
        }
        // These fixtures include independently seeded translucent OIT before/within/behind cloud support.
        // Only valid arithmetic is asserted; differences never imply acceptable quality or a speedup.
        var raw=Difference(candidate.ReadTraceRecord(),disabled.ReadTraceRecord());
        var reconstructed=Difference(candidate.ReadDefault(),disabled.ReadDefault());
        var direct=Difference(candidate.ReadReference(),disabled.ReadReference());
        output.WriteLine(JsonSerializer.Serialize(new {scenario,threshold=.005f,disabledThreshold=0,extinction,
            environment,scale,raw,reconstructed,direct}));
    }
    #endregion

    #region Private
    /// <summary>Runs actual trace and direct-output programs against an independent four-step uniform ray.</summary>
    private void VerifyUniformRay(float extinction,float threshold,int steps)
    {
        EnsureShaderTestAvailable();
        var settings=CloudVolumeSettings.Default with {ViewRaySampleMax=4,ViewRaySampleMin=4,StopTransmittance=threshold};
        using var view=CreateScene(1,1,settings);
        view.Capture(0,extinction,10,.125f,0,"homogeneous");view.Render(0,false);
        const float step=37.5f;
        float transmission=1,radiance=0,alpha=1-MathF.Exp(-(extinction/50)*step);
        float[] bins=[1,1,1];int integrated=0;
        // The authored center ray starts inside the finite grid at z150 and exits at z0.
        // Evaluate the declared finite intervals, not a finer or continuous transport reference.
        for(int i=0;i<4;i++)
        {
            radiance+=transmission*alpha*10;transmission*=1-alpha;integrated++;
            float near=i*step,adjusted=near*near*near*(1-near/1000)/250000;
            float bin=MathF.Log(adjusted/30+1);
            for(int bucket=0;bucket<3;bucket++)
            {
                float n=(bin-bucket)/.832f;
                bins[bucket]*=1-alpha*(bucket==2&&bin>2?1:MathF.Exp(-n*n));
            }
            if(transmission<threshold)break;
        }
        Assert.Equal(steps,integrated);
        var raw=view.ReadTraceRecord();var direct=view.ReadReference();
        for(int channel=0;channel<3;channel++)Close(radiance,raw[0][channel]);
        Close(transmission,raw[0][3]);Close(0,raw[2][0]);Close(step*steps,raw[2][1]);
        // Depth clipping channels are retained even when the optical march terminates early.
        Assert.Equal(raw[2][2],raw[2][3]);Assert.InRange(raw[2][2],0,1);
        float opacity=1-transmission;
        Close(transmission,direct[1][0]);
        for(int channel=0;channel<3;channel++)Close(0,direct[2][channel]);
        Close(opacity*opacity,direct[2][3]);
        for(int bucket=0;bucket<3;bucket++)
        {
            Close(bins[bucket],raw[1][bucket]);Close(bins[bucket],direct[0][bucket]);
            for(int channel=0;channel<4;channel++)
                Close((channel==3?opacity:radiance*.125f)*(1-bins[bucket]),direct[bucket+3][channel]);
        }
    }

    /// <summary>Creates tiny targets and the actual shader owners with an immutable termination selection.</summary>
    private CloudReconstructionScene CreateScene(int width,int height,CloudVolumeSettings settings)=>new(width,height,
        Programs.Create<CloudVolumeProgram>(),Programs.Create<CloudTraceProgram>(),Programs.Create<CloudTemporalProgram>(),
        Programs.Create<CloudResolveProgram>(),false,4,settings:settings);

    /// <summary>Compares one prescribed arithmetic result within binary32 accumulation precision.</summary>
    private static void Close(float expected,float actual)=>Assert.InRange(MathF.Abs(expected-actual),0,MathF.Max(1,MathF.Abs(expected))*2e-5f);

    /// <summary>Reports signed extrema and absolute differences per channel while checking finite optical arithmetic.</summary>
    private static object[] Difference(float[][] candidate,float[][] disabled)
    {
        Assert.Equal(disabled.Length,candidate.Length);
        var differences=new object[candidate.Length];
        for(int layer=0;layer<candidate.Length;layer++)
        {
            Assert.Equal(disabled[layer].Length,candidate[layer].Length);
            int components=candidate.Length==3?4:layer==0?3:layer==1?1:4;
            var channels=new object[components];
            for(int channel=0;channel<components;channel++)
            {
                float minimum=float.PositiveInfinity,maximum=float.NegativeInfinity,maxAbsolute=0;
                for(int i=channel;i<candidate[layer].Length;i+=components)
                {
                    Assert.True(float.IsFinite(candidate[layer][i])&&candidate[layer][i]>=0);
                    Assert.True(float.IsFinite(disabled[layer][i])&&disabled[layer][i]>=0);
                    float delta=candidate[layer][i]-disabled[layer][i];
                    minimum=MathF.Min(minimum,delta);maximum=MathF.Max(maximum,delta);maxAbsolute=MathF.Max(maxAbsolute,MathF.Abs(delta));
                }
                channels[channel]=new {channel,minimum,maximum,maxAbsolute};
            }
            differences[layer]=new {layer,channels};
        }
        return differences;
    }
    #endregion
}
