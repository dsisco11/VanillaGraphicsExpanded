using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks compact record arithmetic and generation without approximation-quality gates.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudShadowRecordTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Checks independent finite-window ramps, hardware parameter filtering, physical units and malformed inputs.</summary>
    [Fact]
    public void FiniteWindowsUseOneCompactRecord()
    {
        EnsureContextValid();
        using var programs=new ComponentShaderPrograms();
        using var probe=new CloudTraversalProbe(programs.Api);
        using var volume=new CloudVolumeParameters();
        using var cache=DynamicTexture2D.Create(2,2,PixelInternalFormat.Rgba32f);
        foreach(float units in new[]{.25f,2f})
        {
            volume.CaptureMap(20,default,units,CloudVolumeSettings.Default);
            // Alpha deliberately has no source/validity meaning. Empty records are RGB zero.
            float[] data=[10,.25f,4,float.NaN, 0,0,0,-1, 20,.5f,10,0, 0,0,0,0];
            cache.UploadDataImmediate(data);
            foreach(var (depth,distance) in new[]{(30f,10f),(100f,100f),(100f,10f),(15f,20f),(0f,0f),(-1f,20f)})
            {
                double end=Math.Min(4,.25*Math.Max(0,depth-10)*units);
                double start=Math.Min(4,.25*Math.Max(0,depth-distance-10)*units);
                double expected=Math.Max(0,end-start);
                Vector4 actual=probe.Run(volume,cache,Vector3.Zero,new(distance,0,0),depth,14)[402];
                Assert.Equal(1,actual.Y);Assert.InRange(Math.Abs(actual.X-expected),0,1e-6);
                Vector4 response=probe.Run(volume,cache,new(.25f,.25f,0),new(distance,.5f,0),depth,15)[402];
                Assert.Equal(1,response.Z);
                Assert.InRange(Math.Abs(response.X-Math.Exp(-expected)),0,1e-6);
                Assert.InRange(Math.Abs(response.Y-Math.Exp(-.5*expected)),0,1e-6);
            }
            foreach(var (u,v) in new[]{(.5f,.5f),(.375f,.625f),(.25f,.75f),(.75f,.25f)})
            {
                double fx=u*2-.5,fy=v*2-.5;
                double front=0,mean=0,maximum=0;
                // Independent bilinear interpolation of parameters precedes the nonlinear ramp/response.
                for(int tap=0;tap<4;tap++)
                {
                    double weight=(tap%2==0?1-fx:fx)*(tap/2==0?1-fy:fy);
                    front+=weight*data[tap*4];mean+=weight*data[tap*4+1];maximum+=weight*data[tap*4+2];
                }
                double tau=Math.Max(0,Math.Min(maximum,mean*Math.Max(0,35-front)*units)
                    -Math.Min(maximum,mean*Math.Max(0,15-front)*units));
                Vector4 actual=probe.Run(volume,cache,new(u,v,0),new(20,.5f,0),35,15)[402];
                Assert.Equal(1,actual.Z);
                Assert.InRange(Math.Abs(actual.X-Math.Exp(-tau)),0,1e-6);
                Assert.InRange(Math.Abs(actual.Y-Math.Exp(-.5*tau)),0,1e-6);
            }
            Assert.Equal(new Vector4(1,1,1,0),probe.Run(volume,cache,new(.75f,.25f,0),new(100,.5f,0),100,15)[402]);
            foreach(float edge in new[]{0f,.249f,.751f,1f})
                Assert.Equal(0,probe.Run(volume,cache,new(edge,.25f,0),new(10,.5f,0),20,15)[402].Z);
            Assert.Equal(0,probe.Run(volume,cache,Vector3.Zero,new(-1,0,0),20,14)[402].Y);
            foreach(float invalid in new[]{-1f,float.NaN,float.PositiveInfinity})
            {
                data[1]=invalid;cache.UploadDataImmediate(data);
                Assert.Equal(0,probe.Run(volume,cache,Vector3.Zero,new(10,0,0),20,14)[402].Y);
            }
        }
        GlDebug.ThrowIfErrors("Compact cloud shadow windows");
    }

    /// <summary>Derives the compact ramp from the original finite quadrature across separated native banks.</summary>
    [Fact]
    public void GenerationStoresFrontMeanAndTotalOpticalDepth()
    {
        EnsureContextValid();
        using var programs=new ComponentShaderPrograms();
        using var builder=programs.CreateCompute(api=>CloudShadowAttenuationComputeShader.Create(api,0));
        using var scene=new CloudReconstructionScene(9,9,programs,false,20,
            settings:CloudVolumeSettings.Default with {Detail=false});
        using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(programs.Api));
        using var target=DynamicTexture2D.Create(8,8,PixelInternalFormat.Rgba32f);
        scene.Capture(0,.125f,1847,1,0,"lit");
        var columns=new float[20*20*4];
        for(int z=0;z<20;z++)for(int x=0;x<20;x++)
        {int i=(z*20+x)*4;columns[i]=x<7?.12f:x>=13?.36f:0;columns[i+3]=1;}
        scene.SetShadowColumns(columns);
        float expected=0;
        for(int i=0;i<64;i++)
        {
            float x=1000-(i+.5f)*(1000f/64);
            expected+=(x<350?.12f:x>=650?.36f:0)/50*(1000f/64);
        }
        for(int source=0;source<2;source++)
        {
            scene.SetDirectionalLights(Vector3.UnitX,Vector3.UnitX,3,source==0?100:1,source==1?100:1);
            scene.BuildShadowCache(builder,hierarchy,target,64,source);
            float[] records=target.ReadPixels();int index=(4*8+4)*4;
            Assert.InRange(Math.Abs(records[index]),0,1e-3f);
            Assert.InRange(Math.Abs(records[index+1]-expected/1000),0,1e-7f);
            Assert.InRange(Math.Abs(records[index+2]-expected),0,1e-5f);
            Assert.Equal(0,records[index+3]);
            Assert.All(records,value=>Assert.True(float.IsFinite(value)));
        }
        GlDebug.ThrowIfErrors("Compact cloud shadow generation");
    }
    #endregion
}
