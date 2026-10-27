using VanillaGraphicsExpanded.Rendering.RHI;
using System.Globalization;
using System.Text;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures raw bloom geometry separately from local exposure, grading and display quantization.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class BloomRadialDiagnosticsTests(HeadlessGLFixture fixture,ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Compares equal physical point sources through the raw half-float graph at two frozen storage scales.</summary>
    [Fact]
    public void RawHaloStoragePrecisionComparison()
    {
        if(Environment.GetEnvironmentVariable("VGE_RUN_BLOOM_RADIAL_DIAGNOSTICS")!="1")return;
        EnsureShaderTestAvailable();
        const int size=512,levels=5;
        using var assets=new BinaryShaderApiFixture();using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var publication=new TestFrameLightingPublication();using var owner=new BloomRenderer();using var draw=new PostprocessDraw();
        var settings=new PostprocessParameters(1,0,0,levels,0,1,16);
        var pipeline=owner.Prepare(assets.Api,draw,size,size,levels);
        float[]? reference=null;
        foreach(float scale in new[]{1f,1f/1024})
        {
            publication.Capture(scale);
            GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api,"pbr_bloom")!.FrameLightingInputs=publication.Buffer;
            float[] input=Impulse(size,size*.5-.5,size*.5-.5);
            for(int i=0;i<input.Length;i++)input[i]*=scale;
            using var source=TestFramework.CreateTexture(size,size,PixelInternalFormat.Rgba32f,input);
            {
    var c=RHICommandContext.Current;
    using var scope14=c.EnterBoundary(new("Tests.BloomPrecision",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope14.Run(()=>owner.Render(c,draw,source,(null,0),settings));
}
            float[] physical=owner.Texture.ReadPixels();
            for(int i=0;i<physical.Length;i+=4)physical[i]/=scale;
            if(reference is null){reference=physical;continue;}
            double peak=reference.Where((_,i)=>i%4==0).Max();
            foreach(double fraction in new[]{.01,.001,.0001,.00003,.00001,.000003,.000001})
            {
                var indices=Enumerable.Range(0,size/2*size/2).Where(i=>reference[i*4]>=peak*fraction/1.25&&reference[i*4]<=peak*fraction*1.25).ToArray();
                if(indices.Length==0)continue;
                double mean=indices.Average(i=>Math.Abs(physical[i*4]-reference[i*4])/reference[i*4]);
                double max=indices.Max(i=>Math.Abs(physical[i*4]-reference[i*4])/reference[i*4]);
                int zeros=indices.Count(i=>physical[i*4]==0),distinct=indices.Select(i=>physical[i*4]).Distinct().Count();
                output.WriteLine(FormattableString.Invariant($"PRECISION scale={scale:G9} fraction={fraction:G5} pixels={indices.Length} meanRelativeError={mean:F6} maxRelativeError={max:F6} zeroPixels={zeros} distinctPhysicalValues={distinct} physicalHalfQuantum={Math.Pow(2,-24)/scale:G9}"));
            }
            string directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","artifacts","BloomRadial"));
            Directory.CreateDirectory(directory);
            Report(reference,size/2,127.5,127.5,"precision-unit",Path.Combine(directory,"precision-unit"));
            Report(physical,size/2,127.5,127.5,"precision-1over1024",Path.Combine(directory,"precision-1over1024"));
            WriteLogImage(Path.Combine(directory,"precision-unit.pgm"),reference,size/2);
            WriteLogImage(Path.Combine(directory,"precision-1over1024.pgm"),physical,size/2);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }

    /// <summary>Records angular profiles and contour radii for actual pyramid outputs and recovered individual scales.</summary>
    [Fact]
    public void RawPyramidAxisAndDiagonalProfiles()
    {
        if(Environment.GetEnvironmentVariable("VGE_RUN_BLOOM_RADIAL_DIAGNOSTICS")!="1")return;
        EnsureShaderTestAvailable();
        bool detailed=Environment.GetEnvironmentVariable("VGE_RUN_BLOOM_RADIAL_DIAGNOSTICS")=="1";
        int size=detailed?512:128;
        string directory=Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,"..","..","..","..","artifacts","BloomRadial"));
        if(detailed)Directory.CreateDirectory(directory);
        using var assets=new BinaryShaderApiFixture();
        using var platform=new EngineShaderPlatformScope();
        Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
        using var publication=new TestFrameLightingPublication();
        GpuShaderProgramLibraries.Get<BloomShaderProgram>(assets.Api,"pbr_bloom")!.FrameLightingInputs=publication.Buffer;
        using var owner=new BloomRenderer();using var draw=new PostprocessDraw();
        double[] energies=[.25,.22,.19,.15,.11,.08];
        (double X,double Y)[] phases=[(0,0),(.25,.75),(5.25,3.5)];
        foreach(var phase in phases)
        {
            double sourceX=size*.5-.5+phase.X,sourceY=size*.5-.5+phase.Y;
            float[] pixels=Impulse(size,sourceX,sourceY);
            using var source=TestFramework.CreateTexture(size,size,PixelInternalFormat.Rgba32f,pixels);
            double cx=(sourceX+.5)/2-.5,cy=(sourceY+.5)/2-.5;
            double priorEnergy=0;float[]? prior=null;
            for(int levels=1;levels<=6;levels++)
            {
                var settings=new PostprocessParameters(1,0,0,levels,0,1,16);
                var pipeline=owner.Prepare(assets.Api,draw,size,size,levels);
                {
    var c=RHICommandContext.Current;
    using var scope15=c.EnterBoundary(new("Tests.BloomRadial",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope15.Run(()=>owner.Render(c,draw,source,(null,0),settings));
}
                float[] halo=owner.Texture.ReadPixels();int width=owner.Texture.Width;
                Assert.All(halo.Where((_,i)=>i%4==0),v=>Assert.True(float.IsFinite(v)&&v>=0));
                double energy=priorEnergy+energies[levels-1];
                var isolated=new float[halo.Length];double negative=0;
                for(int i=0;i<halo.Length;i+=4)
                {
                    double value=(halo[i]*energy-(prior is null?0:prior[i]*priorEnergy))/energies[levels-1];
                    negative=Math.Min(negative,value);isolated[i]=(float)Math.Max(0,value);
                }
                string tag=FormattableString.Invariant($"phase-{phase.X:F2}-{phase.Y:F2}-levels-{levels}");
                if(levels is 3 or 5 or 6)
                {
                    Report(halo,width,cx,cy,tag,detailed?Path.Combine(directory,tag):null);
                    if(detailed)WriteLogImage(Path.Combine(directory,tag+".pgm"),halo,width);
                }
                double sigma=(levels+2)/Math.Sqrt(33.4)*Math.Pow(2,levels-1);
                foreach(double fraction in new[]{.5,1d,1.5,2d,2.5})
                {
                    double r=sigma*fraction;
                    if(r>=Math.Min(Math.Min(cx,cy),Math.Min(width-1-cx,width-1-cy)))continue;
                    double axis=Directional(isolated,width,cx,cy,r,false),diagonal=Directional(isolated,width,cx,cy,r,true);
                    double fullPeak=Sample(halo,width,cx,cy);
                    output.WriteLine(FormattableString.Invariant($"ISOLATED {tag} r/sigma={fraction:F1} r={r:F3} axis={axis:G8} diagonal={diagonal:G8} axis/diagonal={(diagonal>0?axis/diagonal:double.NaN):F5} axis/fullPeak={(fullPeak>0?axis/fullPeak:0):G8} subtractionMin={negative:G8}"));
                }
                prior=halo;priorEnergy=energy;
            }
            Assert.Equal(pixels,source.ReadPixels());
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        output.WriteLine($"Raw diagnostic size={size}; detailed artifacts={detailed}; no final local gain, exposure multiplication, tone mapping or RGB8 quantization.");
    }
    #endregion

    #region Private
    #region Stimulus and sampling
    /// <summary>Distributes fixed radiant flux over neighboring texels to represent a movable subpixel point.</summary>
    private static float[] Impulse(int size,double x,double y)
    {
        var values=new float[size*size*4];int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);
        for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
        {
            float weight=(float)((dx==0?1-(x-ix):x-ix)*(dy==0?1-(y-iy):y-iy)*1024);
            int index=((iy+dy)*size+ix+dx)*4;values[index]=values[index+1]=values[index+2]=weight;
        }
        return values;
    }
    /// <summary>Interpolates the half-resolution red channel at a continuous output-texel coordinate.</summary>
    private static double Sample(float[] values,int width,double x,double y)
    {
        int ix=(int)Math.Floor(x),iy=(int)Math.Floor(y);double fx=x-ix,fy=y-iy,total=0;
        for(int dy=0;dy<2;dy++)for(int dx=0;dx<2;dx++)
        {
            int sx=Math.Clamp(ix+dx,0,width-1),sy=Math.Clamp(iy+dy,0,width-1);
            total+=values[(sy*width+sx)*4]*(dx==0?1-fx:fx)*(dy==0?1-fy:fy);
        }
        return total;
    }
    /// <summary>Averages opposing cardinal or diagonal directions to distinguish radial shape from one-sided centering.</summary>
    private static double Directional(float[] values,int width,double x,double y,double radius,bool diagonal)
    {
        double total=0;
        for(int i=0;i<4;i++)
        {
            double angle=(i*.5+(diagonal?.25:0))*Math.PI;
            total+=Sample(values,width,x+radius*Math.Cos(angle),y+radius*Math.Sin(angle));
        }
        return total*.25;
    }
    #endregion
    #region Diagnostic artifacts
    /// <summary>Records radial crossings, angular ratios and linear profiles without asserting an aesthetic threshold.</summary>
    private void Report(float[] values,int width,double cx,double cy,string tag,string? path)
    {
        double peak=Sample(values,width,cx,cy);
        double maximum=Math.Min(Math.Min(cx,cy),Math.Min(width-1-cx,width-1-cy));
        var csv=new StringBuilder("radius,axis,diagonal,axis_over_diagonal,axis_over_peak,diagonal_over_peak\n");
        foreach(double fraction in new[]{.1,.03,.01,.003,.001,.0003,.0001,.00003,.00001,.000001})
        {
            double axis=Crossing(false),diagonal=Crossing(true);
            output.WriteLine(FormattableString.Invariant($"CONTOUR {tag} fraction={fraction:G5} axisRadius={axis:F4} diagonalRadius={diagonal:F4} diagonal/axis={(axis>0?diagonal/axis:double.NaN):F5} peak={peak:G8}"));
            /// <summary>Finds the first monotone outward threshold crossing within the image's unclamped interior.</summary>
            double Crossing(bool diagonal)
            {
                double previous=peak;
                for(double r=.25;r<=maximum;r+=.25)
                {
                    double current=Directional(values,width,cx,cy,r,diagonal);
                    if(current<=peak*fraction)return r-.25+.25*(previous-peak*fraction)/Math.Max(previous-current,1e-30);
                    previous=current;
                }
                return double.NaN;
            }
        }
        for(double r=0;r<=maximum;r+=.25)
        {
            double axis=Directional(values,width,cx,cy,r,false),diagonal=Directional(values,width,cx,cy,r,true);
            csv.AppendLine(string.Create(CultureInfo.InvariantCulture,$"{r:G9},{axis:G12},{diagonal:G12},{(diagonal>0?axis/diagonal:double.NaN):G12},{axis/peak:G12},{diagonal/peak:G12}"));
        }
        if(path is not null)File.WriteAllText(path+".csv",csv.ToString());
    }
    /// <summary>Writes dependency-free log grayscale over seven decades; the raw numeric profiles remain authoritative.</summary>
    private static void WriteLogImage(string path,float[] values,int width)
    {
        double peak=values.Where((_,i)=>i%4==0).Max();
        using var file=File.Create(path);byte[] header=Encoding.ASCII.GetBytes($"P5\n{width} {width}\n255\n");file.Write(header);
        var gray=new byte[width*width];
        for(int i=0;i<gray.Length;i++)
        {
            double relative=Math.Max(values[i*4]/peak,1e-7);
            gray[i]=(byte)Math.Clamp(Math.Round((Math.Log10(relative)+7)/7*255),0,255);
        }
        file.Write(gray);
    }
    #endregion
    #endregion
}
