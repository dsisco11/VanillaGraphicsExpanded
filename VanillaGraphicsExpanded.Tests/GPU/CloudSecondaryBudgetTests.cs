using System.Numerics;
using System.Runtime.InteropServices;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Separates deterministic shadow quadrature, finite-distance bias and independent native geometry.</summary>
[Collection("GPU")][Trait("Category", "GPU")]
public sealed class CloudSecondaryBudgetTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    private static Vector4[] lastRecords=[];
    #region Public API
    /// <summary>Exposes actual density calls to distinguish quadratic midpoint support aliasing from an unintended schedule.</summary>
    [Fact]
    public void ShortShadowSupportUsesAnchoredUnclippedIntervalWeights()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        using var native=DynamicTexture2D.Create(1,1,PixelInternalFormat.Rgba32f);native.UploadDataImmediate(new float[]{2,0,-.5f,.5f});
        using var volume=new CloudVolumeParameters();volume.CaptureMap(1,default,1,CloudVolumeSettings.Default with { Detail=false });
        foreach(float z in new[]{37.5f,12.5f})
        {
            float actual=Trace(program,volume,native,new(25,0,z),-Vector3.UnitZ,8);
            double exact=z*2/50;
            output.WriteLine($"Short support exit={z}; exactTau={exact:G12}; actualTau={actual:G12}; actualGpuDensityCalls={lastRecords.Length}; GPU(distance,density)=[{string.Join(";",lastRecords.Select(r=>$"({r.X:G9},{r.Y:G9})"))}]. Full quadratic interval weights are15,45,... even when the native support ends inside an interval.");
            Assert.InRange(lastRecords.Length,0,CloudVolumeSettings.Default.LightRaySampleMax);
            Assert.InRange(Math.Abs(actual-(z==37.5f?2.4:.6)),0,1e-5);
        }
    }

    /// <summary>Measures distant support separately from local sample aliasing against direct native-box optical depth.</summary>
    [Theory]
    [InlineData(10,1500f)][InlineData(64,3000f)][InlineData(64,10000f)]
    public void DistantOccluderMeetsIndependentPhysicalLimit(int count,float distance)
    {
        EnsureShaderTestAvailable();
        const int width=81;
        using var program = new CloudTraversalProbe(Programs.Api);
        using var native=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
        using var volume=new CloudVolumeParameters();
        Vector4[] columns=Columns(width,2);native.UploadDataImmediate(MemoryMarshal.Cast<Vector4,float>(columns.AsSpan()).ToArray());
        volume.CaptureMap(width,default,1,CloudVolumeSettings.Default with { Detail=false,OctaveContribution=0,LightRaySampleMax=count,ShadowDistance=distance });
        Vector3 origin=new(25,0,25),direction=Vector3.UnitX;
        float actual=Trace(program,volume,native,origin,direction);
        double bounded=Exact(columns,width,origin,direction,distance),complete=Exact(columns,width,origin,direction,100000);
        // An aligned source has the maximum HG response. Normalize by its completed
        // admitted direct-source bound; zero environment and octave isolate self-shadowing.
        double error=Math.Abs(Math.Exp(-actual)-Math.Exp(-complete));
        output.WriteLine($"Distant native occluder: count={count}; distance={distance}; tauGpu={actual:G12}; tauBoundedExact={bounded:G12}; tauCompleteExact={complete:G12}; normalizedSourceError={error:G12}; distanceOnlyError={Math.Abs(Math.Exp(-bounded)-Math.Exp(-complete)):G12}; samplingOnlyError={Math.Abs(Math.Exp(-actual)-Math.Exp(-bounded)):G12}");
        Assert.InRange(error,0,.02);
    }

    /// <summary>Verifies whole-ray quadratic weights without borrowing production traversal or sample positions.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void AnchoredQuadraticScheduleMatchesIndependentScalarField(bool detail)
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        double worstCoarse=0,worstDistance=0;
        foreach(int width in new[]{1,21,81,200})
        {
            using var native=DynamicTexture2D.Create(width,width,PixelInternalFormat.Rgba32f);
            using var volume=new CloudVolumeParameters();
            foreach(int pattern in new[]{0,1,2,3})
            {
                Vector4[] columns=Columns(width,pattern);
                native.UploadDataImmediate(MemoryMarshal.Cast<Vector4,float>(columns.AsSpan()).ToArray());
                foreach(float distance in new[]{1500f,3000f,10000f})
                foreach(int count in new[]{2,10,64})
                {
                    volume.CaptureMap(width,default,1,CloudVolumeSettings.Default with
                    { Detail=detail,DetailAmplitude=0,LightRaySampleMax=count,ShadowDistance=distance });
                    foreach(var (origin,direction) in Rays(width))
                    {
                        double expected=0;
                        for(int i=0;i<count;i++)
                        {
                            double a=distance*i*i/(double)(count*count),b=distance*(i+1)*(i+1)/(double)(count*count);
                            expected+=Density(columns,width,origin+direction*(float)((a+b)*.5),detail)*(b-a);
                        }
                        float actual=Trace(program,volume,native,origin,direction);
                        Assert.True(float.IsFinite(actual)&&actual>=0);
                        Assert.InRange(lastRecords.Length,0,count);
                        Assert.True(Math.Abs(actual-expected)<=Math.Max(1,expected)*2e-5,$"width={width}; pattern={pattern}; distance={distance}; count={count}; origin={origin}; direction={direction}; actual={actual}; expected={expected}; gpuDensityCalls={lastRecords.Length}");
                        if(!detail)
                        {
                            double bounded=Exact(columns,width,origin,direction,distance);
                            double complete=Exact(columns,width,origin,direction,100000);
                            worstCoarse=Math.Max(worstCoarse,Math.Abs(Math.Exp(-expected)-Math.Exp(-bounded)));
                            worstDistance=Math.Max(worstDistance,Math.Abs(Math.Exp(-bounded)-Math.Exp(-complete)));
                            // Uniform complete spans retain analytic optical depth; discontinuities
                            // deliberately expose finite sampling and distance limitations separately.
                            if(pattern==1&&Math.Abs(bounded-distance*.02)<1e-5)
                                Assert.InRange(Math.Abs(Math.Exp(-actual)-Math.Exp(-bounded)),0,2e-5);
                        }
                    }
                }
            }
        }
        output.WriteLine($"detail={detail}; maximum coarse-sampling transmission bias={worstCoarse:G9}; maximum finite-distance transmission bias={worstDistance:G9}. These are distinct approximation measurements, not hierarchy errors or quality acceptance.");
    }
    #endregion

    #region Private
    /// <summary>Calls actual production secondary integration with main-view depth deliberately irrelevant.</summary>
    private static float Trace(CloudTraversalProbe program,CloudVolumeParameters volume,DynamicTexture2D native,Vector3 origin,Vector3 direction,int mode=7)
    {
        Vector4[] records=program.Run(volume,native,origin,direction,0,mode);
        int measured=(int)records[402].X;Assert.InRange(measured,0,64);
        lastRecords=records.AsSpan(0,measured).ToArray();
        return records[402].Z;
    }

    /// <summary>Integrates direct native boxes in double precision, independently of bounded sample allocation.</summary>
    private static double Exact(Vector4[] columns,int width,Vector3 origin,Vector3 direction,float distance)
        =>CloudTraversalOracle.Intersections(columns,width,origin,direction,distance)
            .Sum(value=>(value.Far-value.Near)*columns[value.Z*width+value.X].X/50.0);

    /// <summary>Computes native support and exposed-face erosion independently with zero procedural amplitude.</summary>
    private static double Density(Vector4[] columns,int width,Vector3 p,bool detail)
    {
        int x=(int)Math.Floor(p.X/50),z=(int)Math.Floor(p.Z/50);
        if(!Occupied(columns,width,x,z,p.Y))return 0;
        Vector4 c=columns[z*width+x];double factor=1;
        if(detail)
        {
            double edge=Math.Min(p.Y-c.Z*50,c.W*50-p.Y);
            if(!Occupied(columns,width,x-1,z,p.Y))edge=Math.Min(edge,p.X-x*50);
            if(!Occupied(columns,width,x+1,z,p.Y))edge=Math.Min(edge,(x+1)*50-p.X);
            if(!Occupied(columns,width,x,z-1,p.Y))edge=Math.Min(edge,p.Z-z*50);
            if(!Occupied(columns,width,x,z+1,p.Y))edge=Math.Min(edge,(z+1)*50-p.Z);
            double u=Math.Clamp(edge/2,0,1);factor=u*u*(3-2*u);
        }
        return c.X/50.0*factor;
    }

    /// <summary>Rejects finite-domain, invalid-tuple and vertical misses before scalar density evaluation.</summary>
    private static bool Occupied(Vector4[] columns,int width,int x,int z,float y)
    {
        if(x<0||z<0||x>=width||z>=width)return false;
        Vector4 c=columns[z*width+x];return float.IsFinite(c.X)&&float.IsFinite(c.Y)&&float.IsFinite(c.Z)&&float.IsFinite(c.W)
            &&c.X>0&&c.W>c.Z&&y>=c.Z*50&&y<=c.W*50;
    }

    /// <summary>Authors uniform, sparse distant occluder, invalid and tiny-positive density cases.</summary>
    private static Vector4[] Columns(int width,int pattern)
    {
        var data=new Vector4[width*width];
        for(int z=0;z<width;z++)for(int x=0;x<width;x++)data[z*width+x]=pattern switch
        {
            0=>Vector4.Zero,1=>new(1,0,-100,100),
            2=>x>=Math.Min(width-1,40)&&x<Math.Min(width,44)?new(.1f,0,-1,1):Vector4.Zero,
            _=>x%3==0?new(float.NaN,0,-1,1):x%3==1?new(.1f,0,1,-1):new(1e-12f,0,-1,1)
        };
        return data;
    }

    /// <summary>Includes inside origins, off-map entry, solar/lunar grazing directions and finite-domain exits.</summary>
    private static IEnumerable<(Vector3 Origin,Vector3 Direction)> Rays(int width)
    {
        float extent=width*50;
        yield return(new(25,0,25),Vector3.UnitX);
        yield return(new(25,0,25),Vector3.Normalize(new(1,.005f,.1f)));
        yield return(new(extent-25,0,25),Vector3.Normalize(new(-1,-.005f,.1f)));
        yield return(new(-25,0,25),Vector3.UnitX);
        yield return(new(25,0,25),Vector3.UnitY);
        yield return(new(25,0,25),-Vector3.UnitY);
        yield return(new(25,5001,25),Vector3.UnitY);
    }
    #endregion
}

