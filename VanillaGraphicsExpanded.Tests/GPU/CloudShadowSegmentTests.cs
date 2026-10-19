using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks the finite segmented attenuation algorithm independently of approximation quality.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CloudShadowSegmentTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Verifies original midpoint contributions, empty depth gaps and finite-window lookups in both layouts.</summary>
    [Fact]
    public void GenerationAndLookupPreserveSegmentContributions()
    {
        EnsureContextValid();
        using var programs=new ComponentShaderPrograms();
        using var builder=programs.CreateCompute(api=>CloudShadowAttenuationComputeShader.Create(api,0));
        using var probe=new CloudTraversalProbe(programs.Api);
        using var volume=new CloudVolumeParameters();
        using var scene=new CloudReconstructionScene(9,9,programs,false,20,
            settings:CloudVolumeSettings.Default with {Detail=false});
        using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(programs.Api));
        scene.Capture(0,.125f,1847,1,0,"lit");
        // Looking along X crosses two occupied banks and a clear gap. The native
        // root spans y=0..50; all map rays through the central texel share the same chord.
        var columns=new float[20*20*4];
        for(int z=0;z<20;z++) for(int x=0;x<20;x++)
        {
            int index=(z*20+x)*4;
            columns[index]=x<7?.12f:x>=13?.36f:0;
            columns[index+3]=1;
        }
        scene.SetShadowColumns(columns);
        foreach(int segments in new[]{8,16})
        {
            using var target=DynamicTexture2D.Create(8,8*(1+segments/4),PixelInternalFormat.Rgba32f);
            for(int source=0;source<2;source++)
            {
                scene.SetDirectionalLights(Vector3.UnitX,Vector3.UnitX,3,source==0?100:1,source==1?100:1);
                scene.BuildShadowCache(builder,hierarchy,target,64,source);
                float[] records=target.ReadPixels();
                const int x=4,y=4;
                int metadata=(y*8+x)*4;
                float start=records[metadata],end=records[metadata+1];
                Assert.InRange(Math.Abs(start),0,1e-3f);
                Assert.InRange(Math.Abs(end-1000),0,1e-3f);
                Assert.Equal(source+1,records[metadata+3]);
                var expected=new float[segments];
                // Independent CPU finite quadrature: retain all 64 original midpoint
                // samples, then distribute each interval by exact geometric overlap.
                float ds=(end-start)/64;
                for(int i=0;i<64;i++)
                {
                    float near=start+i*ds,far=near+ds;
                    float position=1000-(near+far)*.5f;
                    float extinction=position<350?.12f/50:position>=650?.36f/50:0;
                    for(int segment=0;segment<segments;segment++)
                    {
                        float lo=start+(end-start)*segment/segments,hi=start+(end-start)*(segment+1)/segments;
                        expected[segment]+=extinction*Math.Max(0,Math.Min(far,hi)-Math.Max(near,lo));
                    }
                }
                for(int segment=0;segment<segments;segment++)
                {
                    int index=((y+(segment/4+1)*8)*8+x)*4+segment%4;
                    Assert.InRange(Math.Abs(records[index]-expected[segment]),0,2e-6f);
                }
                // Metadata sums 64 contributions in ray order, while the independent
                // oracle groups them by segment. Bound floating-point summation error.
                float accumulationRoundoff=64*1.1920929e-7f*Math.Max(1,expected.Sum());
                Assert.InRange(Math.Abs(records[metadata+2]-expected.Sum()),0,accumulationRoundoff);
                foreach(var (depth,distance) in new[]{(1000f,1000f),(500f,100f),(301f,130f),(900f,250f),(0f,0f),(1100f,20f)})
                {
                    float tau=0;
                    for(int segment=0;segment<segments;segment++)
                    {
                        float lo=start+(end-start)*segment/segments,hi=start+(end-start)*(segment+1)/segments;
                        tau+=expected[segment]*Math.Max(0,Math.Min(depth,hi)-Math.Max(depth-distance,lo))/(hi-lo);
                    }
                    Vector4 result=probe.Run(volume,target,new(x,y,source),new(distance,0,0),depth,14)[402];
                    Assert.Equal(1,result.Y);
                    Assert.InRange(Math.Abs(result.X-tau),0,2e-6f);
                    Assert.InRange(result.Z,0,1);
                }
                Assert.Equal(0,probe.Run(volume,target,new(x,y,1-source),new(100,0,0),500,14)[402].Y);
            }
            // Source validity is discrete: an empty valid ray differs from unavailable data.
            var empty=new float[target.Width*target.Height*4];
            empty[(4*8+4)*4+3]=1;
            target.UploadDataImmediate(empty);
            Assert.Equal(new Vector4(0,1,1,0),probe.Run(volume,target,new(4,4,0),new(20,0,0),100,14)[402]);
            empty[(4*8+4)*4+3]=0;target.UploadDataImmediate(empty);
            Assert.Equal(0,probe.Run(volume,target,new(4,4,0),new(20,0,0),100,14)[402].Y);
            empty[(4*8+4)*4+1]=150;empty[(4*8+4)*4+3]=1;
            empty[((4+8)*8+4)*4]=float.NaN;target.UploadDataImmediate(empty);
            Assert.Equal(0,probe.Run(volume,target,new(4,4,0),new(150,0,0),150,14)[402].Y);
            empty[((4+8)*8+4)*4]=float.MaxValue;target.UploadDataImmediate(empty);
            Assert.Equal(new Vector4(1e30f,1,0,0),probe.Run(volume,target,new(4,4,0),new(150,0,0),150,14)[402]);
            empty[((4+8)*8+4)*4]=-1;target.UploadDataImmediate(empty);
            Assert.Equal(0,probe.Run(volume,target,new(4,4,0),new(150,0,0),150,14)[402].Y);
        }
        GlDebug.ThrowIfErrors("Segmented cloud shadow arithmetic");
    }
    #endregion
}
