using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies stored-field OIT arithmetic, independent clipping/encoding and compiled fallback paths.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudStoredVolumeViewTests(HeadlessGLFixture fixture,Xunit.ITestOutputHelper output):LumOnShaderFunctionalTestBase(fixture)
{
    private readonly string gpu=fixture.GLRenderer;
    #region Public API
    /// <summary>The actual full-view shader consumes stored density and Q plus D once, with independent valid calibrations.</summary>
    [Fact]
    public void StoredViewUsesVoxelTransportAndNativeProducts()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(Programs);
        var settings=CloudVolumeViewSettings.Default with{Detail=false,Stop=0,TraceDivisor=2};
        var baseline=scene.Draw(settings);
        Assert.InRange(Math.Abs(baseline[1][0]-MathF.Exp(-.0078125f*100)),0,2e-5f);
        float phase0=.64f/(4*MathF.PI*MathF.Pow(1.36f,1.5f));float phase1=1.6f/(4*MathF.PI*.4f*.4f);
        float source=.9f*(30*.25f*phase1+3*.75f*phase0)+12;
        for(int bucket=0;bucket<3;bucket++)
        {
            float[] image=baseline[bucket+3];
            Assert.InRange(Math.Abs(image[3]-(1-baseline[1][0])*(1-baseline[0][bucket])),0,2e-5f);
            Assert.InRange(Math.Abs(image[0]/image[3]-source),0,3e-4f);
        }
        var encoded=scene.Draw(settings,scale:2);Assert.Equal(baseline[0],encoded[0]);Assert.Equal(baseline[1],encoded[1]);
        Assert.InRange(Math.Abs(encoded[3][0]-2*baseline[3][0]),0,2e-5f);
        var disabled=scene.Draw(settings with{SelfShadowing=false,Diffuse=false});
        float fallback=.9f*(30*phase1+3*phase0)+9;
        Assert.InRange(Math.Abs(disabled[3][0]/disabled[3][3]-fallback),0,3e-4f);
        foreach(var depths in new[]{(50f,100f,100f),(100f,50f,100f),(100f,100f,50f)})
        {
            scene.SetDepths(depths.Item1,depths.Item2,depths.Item3);
            Assert.InRange(Math.Abs(scene.Draw(settings)[1][0]-MathF.Exp(-.0078125f*50)),0,2e-5f);
        }
        scene.SeedDensity(0);var empty=scene.Draw(settings);Assert.Equal(1,empty[1][0]);Assert.All(empty.Skip(2).SelectMany(v=>v),v=>Assert.Equal(0,v));
        Assert.All(scene.Draw(settings,clear:.25f).SelectMany(v=>v),v=>Assert.Equal(.25f,v));
        scene.SeedDensity(float.NaN);
        Assert.All(scene.Draw(settings,clear:.25f).SelectMany(v=>v),v=>Assert.Equal(.25f,v));
        using(var edge=new CloudStoredVolumeViewScene(Programs,5,3))
        {
            // Resolution changes retire only screen-sized products and preserve cloud-space density.
            edge.SetMaterialDepthColumns(2,.1f,100);
            var retained=edge.StoredDensity;
            foreach(int divisor in new[]{2,4,1,2})
            {
                var selection=settings with{TraceDivisor=divisor};edge.SeedDensity(.0078125f);
                var clipped=edge.Draw(selection);Assert.All(clipped.SelectMany(v=>v),v=>Assert.True(float.IsFinite(v)));
                Assert.Equal(divisor==1?0:((5+divisor-1)/divisor)*((3+divisor-1)/divisor)*36,edge.ViewTargetBytes);
                Assert.Same(retained,edge.StoredDensity);
                if(divisor!=4)for(int y=0;y<3;y++)Assert.True(clipped[1][y*5]>clipped[1][y*5+4]);
                edge.SeedDensity(0);
                Assert.All(edge.Draw(selection,clear:.25f).SelectMany(v=>v),v=>Assert.Equal(.25f,v));
                edge.SeedDensity(float.NaN);
                Assert.All(edge.Draw(selection,clear:.25f).SelectMany(v=>v),v=>Assert.Equal(.25f,v));
            }
        }
        using(var fog=new CloudStoredVolumeViewScene(Programs,fogColor:Vector3.UnitY,fogMinimum:1){Underwater=1})
        {
            // A fully selected medium uses the green shared-frame fog without changing cloud extinction.
            var covered=fog.Draw(settings);Assert.Equal(baseline[1],covered[1]);
            for(int bucket=0;bucket<3;bucket++)
            {
                float[] image=covered[bucket+3];Assert.Equal(0,image[0]);Assert.Equal(0,image[2]);
                Assert.InRange(Math.Abs(image[1]/image[3]-32),0,3e-4f);
            }
        }
        scene.Atmosphere=scene.Atmosphere with{CalibrationGeneration=0};scene.Draw(settings,succeeds:false);
    }
    /// <summary>Records a bounded authored view workload without a speed or approximation threshold.</summary>
    [Fact]
    public void RecordStoredViewCost()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(Programs,512,256);
        using var timer=GpuTimerQuery.Create("Tests.Cloud.StoredVolume.ViewCost");
        foreach(float sigma in new[]{.0078125f,0f})
        {
            scene.SeedDensity(sigma);var times=new List<double>();
            // Preparation and clear precede each query; synchronous test query reads follow it.
            for(int i=0;i<4;i++)
            {
                scene.PrepareDraw(CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=2});
                timer.Begin();bool drawn=scene.DrawOnly();timer.End();Assert.True(drawn);
                double milliseconds=timer.GetResultNanoseconds()/1e6;if(i>0)times.Add(milliseconds);
            }
            times.Sort();
            output.WriteLine($"Stored view 512x256 sigma={sigma}: median {times[1]:F4} ms; samples {string.Join(", ",times.Select(t=>t.ToString("F4")))} ms. GPU: {gpu}.");
        }
        output.WriteLine("Actual typed compact half-resolution trace plus full-resolution spatial reconstruction, 36 bytes per trace pixel, half-voxel occupied spacing, authored uniform extinction .0078125/empty0, direct .25/.75 and diffuse12, depths100, detail disabled, default shadows/diffuse/stop. One warmup and three samples. External preparation, clears and readbacks excluded; internal intermediate clears and both draws included; no live FPS or appearance qualification; contention uncontrolled.");
    }
    /// <summary>The complete native-generation-to-stored-view path owns and retires only its independent resources.</summary>
    [Fact]
    public void StoredSolutionInitializesRefinesDrawsAndRetires()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(Programs);
        var solution=new CloudVolumeSolution(Programs.Api,false);
        try
        {
            var value=scene.Draw(CloudVolumeViewSettings.Default with{Detail=false},solution:solution);
            Assert.True(solution.Storage.Ready);Assert.True(solution.Storage.StorageBytes>0);
            Assert.All(value.SelectMany(v=>v),v=>Assert.True(float.IsFinite(v)&&v>=0));
            var first=solution.Drawable!;
            scene.Draw(CloudVolumeViewSettings.Default with{Detail=false},solution:solution);
            Assert.True(solution.Drawable!.Revision>first.Revision);Assert.Same(first.Density,solution.Drawable.Density);
            scene.QueueUpdate(solution,CloudVolumeViewSettings.Default with{Detail=false},retireAndResubmit:true);
            var coherent=solution.Storage.Published.Diffuse;var encoding=solution.Storage.Published.Encoding;
            solution.ReconfigureLighting();
            Assert.False(solution.UpdatePending);Assert.Null(solution.Drawable);
            Assert.Same(coherent,solution.Storage.Published.Diffuse);Assert.Equal(encoding,solution.Storage.Published.Encoding);
            scene.Draw(CloudVolumeViewSettings.Default with{Detail=false},solution:solution);
            scene.QueueUpdate(solution,CloudVolumeViewSettings.Default with{Detail=false});
            var cap=solution.Admission.Encoding.Cap;
            solution.Admission.Observe(scene.Atmosphere with{Solar=Vector3.Zero,Lunar=Vector3.Zero,Environment=Vector3.Zero});
            Assert.Equal(cap,solution.Admission.Encoding.Cap);
        }
        finally{solution.Dispose();}
        Assert.False(solution.Storage.Ready);Assert.Equal(0,solution.Storage.StorageBytes);Assert.Null(solution.Drawable);Assert.False(solution.UpdatePending);
    }
    #endregion
}
