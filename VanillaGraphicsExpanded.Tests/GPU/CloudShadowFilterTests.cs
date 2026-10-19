using System.Numerics;
using System.Diagnostics;
using System.Text.Json;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks transmission filtering arithmetic and complete-footprint cache admission.</summary>
[Collection("GPU")]
[Trait("Category","GPU")]
public sealed class CloudShadowFilterTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Checks independent depth windows, nonlinear responses and discrete missing-tap fallback.</summary>
    [Fact]
    public void TransmissionUsesEachContributingRayAndRequiresCompleteCoverage()
    {
        EnsureContextValid();
        using var programs=new ComponentShaderPrograms();
        using var probe=new CloudTraversalProbe(programs.Api);
        using var volume=new CloudVolumeParameters();
        using var cache=DynamicTexture2D.Create(8,24,PixelInternalFormat.Rgba32f);
        var data=new float[8*24*4];
        // Every metadata texel starts as valid empty coverage. Four interior rays
        // then receive deliberately different chords and segment contributions.
        for(int pixel=0;pixel<64;pixel++) data[pixel*4+3]=1;
        cache.UploadDataImmediate(data);
        Assert.Equal(new Vector4(1,1,1,0),Query(probe,volume,cache,.5f,.5f,40,20));
        var starts=new[]{0f,10f,20f,30f};
        var amounts=new[]{0f,.25f,.5f,1f};
        for(int tap=0;tap<4;tap++)
        {
            int x=3+tap%2,y=3+tap/2,index=(y*8+x)*4;
            data[index]=starts[tap];data[index+1]=starts[tap]+80;data[index+2]=8*amounts[tap];
            for(int segment=0;segment<8;segment++)
                data[((y+(segment/4+1)*8)*8+x)*4+segment%4]=amounts[tap];
        }
        cache.UploadDataImmediate(data);
        foreach(var (u,v) in new[]{(.5f,.5f),(.46875f,.53125f),(3.5f/8,3.5f/8)})
        foreach(var (depth,distance) in new[]{(60f,20f),(25f,10f),(150f,20f),(40f,0f)})
        {
            double direct=0,octave=0;
            double fx=u*8-3.5,fy=v*8-3.5;
            for(int tap=0;tap<4;tap++)
            {
                double weight=(tap%2==0?1-fx:fx)*(tap/2==0?1-fy:fy);
                double overlap=Math.Max(0,Math.Min(depth,starts[tap]+80)-Math.Max(depth-distance,starts[tap]));
                double tau=amounts[tap]*overlap/10;
                direct+=weight*Math.Exp(-tau);octave+=weight*Math.Exp(-.5*tau);
            }
            Vector4 actual=Query(probe,volume,cache,u,v,depth,distance);
            Assert.Equal(1,actual.Z);
            // Four positive products and exponentials use binary32 on the GPU.
            Assert.InRange(Math.Abs(actual.X-direct),0,1e-6);
            Assert.InRange(Math.Abs(actual.Y-octave),0,1e-6);
        }
        // A constant field must reproduce its transmission for arbitrary weights,
        // including the zero-extinction octave and exact texel-center footprints.
        for(int pixel=0;pixel<64;pixel++)
        {
            data[pixel*4]=0;data[pixel*4+1]=80;data[pixel*4+2]=2;
            for(int segment=0;segment<8;segment++) data[(pixel+(segment/4+1)*64)*4+segment%4]=.25f;
        }
        cache.UploadDataImmediate(data);
        Vector4 constant=Query(probe,volume,cache,.46875f,.53125f,60,20);
        Assert.InRange(Math.Abs(constant.X-MathF.Exp(-.5f)),0,1e-6f);
        Assert.InRange(Math.Abs(constant.Y-MathF.Exp(-.25f)),0,1e-6f);
        Assert.Equal(1,probe.Run(volume,cache,new(.5f,.5f,0),new(20,0,0),60,15)[402].Y);
        foreach(float edge in new[]{0f,.02f,.98f,1f})
            Assert.Equal(0,Query(probe,volume,cache,edge,.5f,60,20).Z);
        Assert.Equal(1,Query(probe,volume,cache,.5f/8,.5f,60,20).Z);
        Assert.Equal(1,Query(probe,volume,cache,7.5f/8,.5f,60,20).Z);
        int invalid=(4*8+4)*4;
        foreach(float sourceFlag in new[]{0f,2f,float.NaN})
        {
            data[invalid+3]=sourceFlag;cache.UploadDataImmediate(data);
            Assert.Equal(0,Query(probe,volume,cache,.5f,.5f,60,20).Z);
            // The invalid adjacent record has zero weight at this exact center.
            Assert.Equal(1,Query(probe,volume,cache,3.5f/8,3.5f/8,60,20).Z);
        }
        data[invalid+3]=1;data[((4+8)*8+4)*4+3]=-1;cache.UploadDataImmediate(data);
        Assert.Equal(0,Query(probe,volume,cache,.5f,.5f,40,10).Z);
        GlDebug.ThrowIfErrors("Cloud shadow transmission filtering");
    }

    /// <summary>Reports isolated nearest/filtered lookup dispatch costs without performance or image-quality gates.</summary>
    [Fact]
    public void LookupCosts()
    {
        Assert.SkipUnless(Environment.GetEnvironmentVariable("VGE_RUN_CLOUD_FILTER_MEASUREMENT")=="1",
            "Explicit cloud filtering measurement required.");
        EnsureContextValid();
        string directory=Environment.GetEnvironmentVariable("VGE_CLOUD_COMPONENT_OUTPUT")
            ??throw new InvalidOperationException("Output directory required.");
        Directory.CreateDirectory(directory);
        using var programs=new ComponentShaderPrograms();
        using var probe=new CloudTraversalProbe(programs.Api);
        using var volume=new CloudVolumeParameters();
        using var cache=DynamicTexture2D.Create(256,768,PixelInternalFormat.Rgba32f);
        using var timer=GpuTimerQuery.Create("Tests.Cloud.ShadowLookup");
        var data=new float[256*768*4];
        for(int pixel=0;pixel<256*256;pixel++)
        {
            data[pixel*4+1]=80;data[pixel*4+2]=1.5f;data[pixel*4+3]=1;
            for(int segment=0;segment<8;segment++)
                data[(pixel+(segment/4+1)*256*256)*4+segment%4]=segment is 3 or 4?0:.25f;
        }
        cache.UploadDataImmediate(data);
        var nearest=new List<double>();var filtered=new List<double>();
        for(int round=-3;round<7;round++)
        foreach(bool useFilter in (round+3)%2==0?new[]{false,true}:new[]{true,false})
        {
            double elapsed=probe.MeasureShadowLookup(volume,cache,useFilter,timer);
            if(round>=0)(useFilter?filtered:nearest).Add(elapsed);
        }
        // After timing, replace the constant XY field with an empty/occupied step.
        // A raw sweep crosses neighbor-selection boundaries without imposing a
        // smoothness or approximation threshold on the filtering response.
        for(int pixel=0;pixel<256*256;pixel++)
        {
            float amount=pixel%256<128?0:.5f;
            data[pixel*4+2]=6*amount;
            for(int segment=0;segment<8;segment++)
                data[(pixel+(segment/4+1)*256*256)*4+segment%4]=segment is 3 or 4?0:amount;
        }
        cache.UploadDataImmediate(data);
        var boundarySweep=Enumerable.Range(0,13).Select(index=>
        {
            float u=(126.5f+index*.25f)/256;
            Vector4 point=Query(probe,volume,cache,(MathF.Floor(u*256)+.5f)/256,128.5f/256,60,40);
            Vector4 blend=Query(probe,volume,cache,u,128.5f/256,60,40);
            return new {u,nearestDirect=point.X,nearestOctave=point.Y,filteredDirect=blend.X,filteredOctave=blend.Y,
                nearestValid=point.Z,filteredValid=blend.Z};
        }).ToArray();
        var receipt=new {fixture.GLRenderer,fixture.GLVersion,width=256,segments=8,
            invocations=400,lookupsPerInvocation=64,warmups=3,pairs=7,
            nearestGpuMs=nearest,filteredGpuMs=filtered,boundarySweep,
            gamePids=Process.GetProcessesByName("Vintagestory").Select(process=>process.Id).ToArray(),
            qualification="Synthetic cache-only compute dispatch including activation; distinct from production trace/total costs. No timing or approximation-quality gate."};
        File.WriteAllText(Path.Combine(directory,"lookup.json"),JsonSerializer.Serialize(receipt,new JsonSerializerOptions{WriteIndented=true}));
        GlDebug.ThrowIfErrors("Cloud shadow lookup diagnostic");
    }
    #endregion

    #region Private
    /// <summary>Maps authored normalized footprint coordinates and light windows into the shared compute fixture.</summary>
    private static Vector4 Query(CloudTraversalProbe probe,CloudVolumeParameters volume,GpuTexture cache,
        float u,float v,float depth,float distance) =>
        probe.Run(volume,cache,new(u,v,0),new(distance,.5f,0),depth,15)[402];
    #endregion
}
