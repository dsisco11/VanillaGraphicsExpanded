using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded native-support allocation and area-weighted optical-depth arithmetic.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CloudShadowSamplingTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Checks gaps, corner ties, parallel rays, units and shared sample budgets against authored geometry.</summary>
    [Fact]
    public void AllocationUsesNativeSupportWithoutMultiplyingTheDensityBudget()
    {
        EnsureContextValid();
        using var programs=new ComponentShaderPrograms();
        using var probe=new CloudTraversalProbe(programs.Api);
        using var volume=new CloudVolumeParameters();
        using var map=DynamicTexture2D.Create(20,20,PixelInternalFormat.Rgba32f);
        var columns=new float[20*20*4];
        for(int z=0;z<20;z++)for(int x=0;x<20;x++)
        {
            int index=(z*20+x)*4;columns[index]=x<7||x>=13?.125f:0;columns[index+3]=1;
        }
        map.UploadDataImmediate(columns);
        foreach(int cap in new[]{2,32,64})
        {
            volume.CaptureMap(20,default,2,CloudVolumeSettings.Default with {Detail=false,LightRaySampleMax=cap});
            foreach(var (origin,direction,limit,length) in new[]{
                (new Vector3(0,25,25),Vector3.UnitX,1000f,700f),
                (new Vector3(1000,25,25),-Vector3.UnitX,1000f,700f),
                (new Vector3(0,25,0),Vector3.Normalize(new(1,0,1)),1500f,700*MathF.Sqrt(2)),
                (new Vector3(25,-25,25),Vector3.UnitY,100f,50f),
                (new Vector3(0,100,25),Vector3.UnitX,1000f,0f),
                (new Vector3(25,25,25),Vector3.Normalize(new(1,1e-6f,0)),1000f,675f),
                (new Vector3(0,-5e-7f,25),Vector3.Normalize(new(1,1e-9f,0)),1000f,350f),
                (new Vector3(-5e-7f,25,0),Vector3.Normalize(new(1e-9f,0,1)),1000f,500f)})
            {
                Vector4 result=probe.Run(volume,map,origin,direction,limit,18)[402];
                Assert.Equal(1,result.W);
                // Native boundaries and normalized diagonal directions use binary32;
                // tolerance covers arithmetic roundoff, not approximation agreement.
                Assert.InRange(Math.Abs(result.X-length),0,.002f);
                Assert.Equal(length==0?0:Math.Min(cap,(int)MathF.Ceiling(length/50)),result.Y);
                Assert.InRange(Math.Abs(result.Z-.125f*length/50),0,1e-5f);
                Assert.InRange(result.Y,0,cap);
            }
        }
        using var scene=new CloudReconstructionScene(9,9,programs,false,20,
            settings:CloudVolumeSettings.Default with {Detail=false});
        using var hierarchy=new CloudHierarchy(()=>CloudHierarchyComputeShader.Create(programs.Api));
        scene.Capture(0,.125f,1847,1,0,"lit");scene.SetShadowColumns(columns);
        scene.SetDirectionalLights(Vector3.UnitY,Vector3.UnitY,3,100,1);
        foreach(int policy in new[]{1,2})
        {
            using var builder=programs.CreateCompute(api=>CloudShadowAttenuationComputeShader.Create(api,policy));
            using var target=DynamicTexture2D.Create(8,24,PixelInternalFormat.Rgba32f);
            scene.BuildShadowCache(builder,hierarchy,target,64,0);
            float[] records=target.ReadPixels();int metadata=(1*8+1)*4;
            Assert.Equal(1,records[metadata+3]);
            Assert.InRange(Math.Abs(records[metadata+2]-.125f),0,1e-5f);
            // Summed depth records use the same nonnegative area weights as total tau.
            float sum=0;
            for(int segment=0;segment<8;segment++)
                sum+=records[((1+(segment/4+1)*8)*8+1)*4+segment%4];
            Assert.InRange(Math.Abs(sum-.125f),0,1e-5f);
            if(policy==1)
            {
                // Long horizontal chords exceed the support-query estimate. Check
                // the retained finite midpoint algorithm rather than analytic quality.
                scene.SetDirectionalLights(Vector3.UnitX,Vector3.UnitX,3,100,1);
                scene.BuildShadowCache(builder,hierarchy,target,64,0);
                float expected=0;
                for(int i=0;i<64;i++)
                {
                    float x=1000-(i+.5f)*(1000f/64);
                    if(x<350||x>=650) expected+=.125f/50*(1000f/64);
                }
                records=target.ReadPixels();metadata=(4*8+4)*4;
                Assert.Equal(1,records[metadata+3]);
                Assert.InRange(Math.Abs(records[metadata+2]-expected),0,1e-5f);
                scene.SetDirectionalLights(Vector3.UnitY,Vector3.UnitY,3,100,1);
            }
        }
        GlDebug.ThrowIfErrors("Cloud support-aware shadow allocation");
    }
    #endregion
}
