using System.Numerics;
using System.Collections.Immutable;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks bounded shadow quadrature against the unchanged procedural field at independent scheduled positions.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudSecondaryDensityTests(HeadlessGLFixture fixture):LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Invalid root levels and mismatched summary dimensions preserve the direct fixed-lattice result.</summary>
    [Fact]
    public void MalformedSummaryMetadataKeepsDirectShadowSchedule()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        using var native=DynamicTexture2D.Create(81,81,PixelInternalFormat.Rgba32f);
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4,float>(Enumerable.Repeat(new Vector4(.2f,0,-100,100),81*81).ToArray().AsSpan()).ToArray());
        using var malformed=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rg32f);malformed.UploadDataImmediate(new float[]{500,501});
        using var volume=new CloudVolumeParameters();volume.CaptureMap(81,default,1,CloudVolumeSettings.Default);
        Vector3 origin=new(2025,0,2025),direction=Vector3.Normalize(new(1,.005f,.1f));
        Vector4[] expected=program.Run(volume,native,origin,direction,0,7);
        foreach(int root in new[]{-1,0,6,8})
        {
            Vector4[] actual=program.Run(volume,native,origin,direction,0,7,bounds:malformed,root:root);
            Assert.Equal(expected,actual);
        }
    }

    /// <summary>Actual directional response preserves each source, octave endpoint and zero-source rejection on the same shadow lattice.</summary>
    [Fact]
    public void DirectionalSourcesAndOctaveEndpointsMatchFiniteLatticeArithmetic()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        const int width=81,count=10;const float distance=1500;
        using var native=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4,float>(Enumerable.Repeat(new Vector4(.2f,0,-100,100),width*width).ToArray().AsSpan()).ToArray());
        using var volume=new CloudVolumeParameters();Vector3 origin=new(2025,0,2025);
        Vector3 sun=Vector3.Normalize(new(1,.005f,.1f)),moon=Vector3.Normalize(new(-1,-.005f,-.1f));
        foreach(float contribution in new[]{0f,.5f,1f})foreach(float occlusion in new[]{0f,.5f,1f})foreach(int mask in new[]{0,1,2,3})
        {
            var settings=CloudVolumeSettings.Default with {OctaveContribution=contribution,OctaveOcclusion=occlusion};
            volume.CaptureMap(width,default,1,settings);
            Vector3 solar=(mask&1)!=0?new(30,15,3):Vector3.Zero,lunar=(mask&2)!=0?new(1,3,5):Vector3.Zero;
            var lighting=new AtmosphereLighting(sun,solar,Vector3.Zero,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            {Moon=moon,Lunar=lunar,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale};
            volume.CaptureLighting(lighting,0,Vector4.Zero,Vector3.Zero);
            Vector3 expected=Vector3.Zero;
            foreach(var (direction,illuminance) in new[]{(sun,solar),(moon,lunar)})
            {
                float[] positions=new float[count];double[] weights=new double[count];
                for(int i=0;i<count;i++){double a=distance*i*i/(double)(count*count),b=distance*(i+1)*(i+1)/(double)(count*count);positions[i]=(float)((a+b)/2);weights[i]=b-a;}
                Vector4[] field=program.Run(volume,native,origin,direction,0,6,positions);
                double tau=Enumerable.Range(0,count).Sum(i=>field[i].Z*weights[i]);
                double mu=Vector3.Dot(-Vector3.UnitZ,direction),phase=.64/(4*Math.PI*Math.Pow(1.36-1.2*mu,1.5));
                expected+=illuminance*(float)(phase*Math.Exp(-tau)+contribution/(4*Math.PI)*Math.Exp(-occlusion*tau));
            }
            Vector4[] result=program.Run(volume,native,origin,sun,0,10);
            Vector3 actual=new(result[400].X,result[400].Y,result[400].Z);
            double bound=Math.Max(1,(solar.X+lunar.Z)*(1.6/(4*Math.PI*.16)+contribution/(4*Math.PI)));
            Assert.InRange(Vector3.Distance(actual,expected)/bound,0,3e-6);
            // This authored cloud covers every scheduled position for each nonzero light.
            // The wrapper observes full-density calls rather than inferring them from budgets.
            Assert.Equal(count*(((mask&1)!=0?1:0)+((mask&2)!=0?1:0)),(int)result[402].X);
        }
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4,float>(Enumerable.Repeat(new Vector4(float.MaxValue,0,-100,100),width*width).ToArray().AsSpan()).ToArray());
        volume.CaptureMap(width,default,1,CloudVolumeSettings.Default with {Detail=false,OctaveContribution=1,OctaveOcclusion=0});
        volume.CaptureLighting(new AtmosphereLighting(sun,new(30),Vector3.Zero,Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
            {Moon=moon,Lunar=Vector3.Zero,CalibrationGeneration=1,LookupStorageScale=LightingStorage.PersistentScale},0,Vector4.Zero,Vector3.Zero);
        Vector4[] dense=program.Run(volume,native,origin,sun,0,10);
        Assert.Equal(count,(int)dense[402].X); // Saturation must not introduce an undeclared shadow exit.
        Assert.InRange(Math.Abs(dense[400].X-30/(4*Math.PI)),0,.00001);
        Assert.True(float.IsFinite(dense[400].X));
    }

    /// <summary>Nonzero procedural noise and sharp support use the same shared density at each fixed quadratic midpoint.</summary>
    [Fact]
    public void ProceduralShadowDensityMatchesIndependentFixedLattice()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        const int width=81;
        using var native=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
        using var volume=new CloudVolumeParameters();
        var columns=new Vector4[width*width];
        for(int z=0;z<width;z++)for(int x=0;x<width;x++)
            columns[z*width+x]=x<2||x>=40&&x<44?new(.2f,0,-1,1):Vector4.Zero;
        native.UploadDataImmediate(MemoryMarshal.Cast<Vector4,float>(columns.AsSpan()).ToArray());
        foreach(int count in new[]{2,10,64})foreach(float distance in new[]{50f,1500f,3000f,10000f})
        {
            volume.CaptureMap(width,default,1,CloudVolumeSettings.Default with {LightRaySampleMax=count,ShadowDistance=distance});
            foreach(var (origin,direction) in new (Vector3,Vector3)[]{(new(25,0,25),Vector3.UnitY),
                (new(25,0,25),Vector3.Normalize(new(1,.005f,.1f))),
                (new(2174,0,25),Vector3.Normalize(new(-1,-.005f,.1f))),
                (new(25,49.5f,25),Vector3.UnitX),(new(-25,0,25),Vector3.UnitX)})
            {
                var positions=new float[count];var weights=new double[count];
                for(int i=0;i<count;i++)
                {
                    double near=distance*i*i/(double)(count*count),far=distance*(i+1)*(i+1)/(double)(count*count);
                    positions[i]=(float)((near+far)/2);weights[i]=far-near;
                }
                Vector4[] densities=program.Run(volume,native,origin,direction,0,6,positions);
                double expected=Enumerable.Range(0,count).Sum(i=>densities[i].Z*weights[i]);
                Vector4[] result=program.Run(volume,native,origin,direction,0,7);
                float actual=result[402].Z;int measured=(int)result[402].X;
                Assert.InRange(measured,0,count);Assert.True(float.IsFinite(actual)&&actual>=0);
                Assert.True(Math.Abs(actual-expected)<=Math.Max(1,expected)*3e-5,$"N={count}, D={distance}, origin={origin}, direction={direction}, actual={actual}, scheduled={expected}");
                for(int i=0;i<measured;i++)
                {
                    int index=Array.FindIndex(positions,p=>Math.Abs(p-result[i].X)<Math.Max(.002,p*2e-6));
                    Assert.True(index>=0,"Actual full-detail queries must belong to the independent fixed lattice.");
                    Assert.InRange(Math.Abs(result[i].Y-densities[index].Z),0,2e-6);
                }
            }
        }
    }
    #endregion

}
