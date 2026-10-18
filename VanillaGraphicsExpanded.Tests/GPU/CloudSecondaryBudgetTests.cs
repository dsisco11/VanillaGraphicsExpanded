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

    /// <summary>Checks distinct quadratic-budget and native-support boundaries against independent scalar density.</summary>
    [Fact]
    public void AnchoredQuadraticScheduleMatchesIndependentScalarField()
    {
        EnsureShaderTestAvailable();
        using var program = new CloudTraversalProbe(Programs.Api);
        // Compile the program once; each independent case owns its native map and parameter state.
        foreach (var (width, pattern, distance, count, detail, ray) in new (int, int, float, int, bool, int)[]
        {
            (21, 0, 1500, 10, false, 0), // Empty support rejects density calls.
            (1, 1, 1500, 10, false, 0), // Native exit must not rescale intervals.
            (200, 1, 10000, 64, false, 1), // Maximum distance/count and grazing light.
            (21, 1, 50, 2, false, 0), // Minimum distance/count retain full interval weights.
            (81, 2, 3000, 10, false, 0), // Distant occluder stays on the origin-anchored lattice.
            (21, 1, 1500, 10, false, 3), // Off-map entry does not shift later samples.
            (21, 1, 1500, 10, false, 2), // Negative grazing direction crosses native boundaries.
            (21, 1, 1500, 10, false, 4), // Vertical ray is parallel to the horizontal grid.
            (21, 1, 1500, 10, false, 6), // Origin above native height support.
            (21, 3, 1500, 64, false, 0), // Invalid tuples reject; tiny positive density remains valid.
            (1, 1, 50, 64, true, 0), // Fine intervals hit the exposed-face erosion region.
            (21, 1, 1500, 10, true, 5) // Negative vertical ray with detail enabled.
        })
        {
            using var native = DynamicTexture2D.Create(width, width, PixelInternalFormat.Rgba32f);
            using var volume = new CloudVolumeParameters();
            Vector4[] columns = Columns(width, pattern);
            native.UploadDataImmediate(MemoryMarshal.Cast<Vector4, float>(columns.AsSpan()).ToArray());
            volume.CaptureMap(width, default, 1, CloudVolumeSettings.Default with
            { Detail = detail, DetailAmplitude = 0, LightRaySampleMax = count, ShadowDistance = distance });
            var (origin, direction) = Rays(width).ElementAt(ray);

            // The scalar oracle uses the declared complete distance, never the clipped domain length.
            double expected = 0;
            for (int i = 0; i < count; i++)
            {
                double a = distance * i * i / (double)(count * count);
                double b = distance * (i + 1) * (i + 1) / (double)(count * count);
                expected += Density(columns, width, origin + direction * (float)((a + b) * .5), detail) * (b - a);
            }
            float actual = Trace(program, volume, native, origin, direction);
            Assert.True(float.IsFinite(actual) && actual >= 0);
            Assert.InRange(lastRecords.Length, 0, count);
            if (pattern == 3) Assert.True(actual > 0, "Tiny positive extinction must not be treated as empty.");
            Assert.True(Math.Abs(actual - expected) <= Math.Max(1, expected) * 2e-5,
                $"width={width}; pattern={pattern}; distance={distance}; count={count}; detail={detail}; ray={ray}; actual={actual}; expected={expected}; gpuDensityCalls={lastRecords.Length}");
        }
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

