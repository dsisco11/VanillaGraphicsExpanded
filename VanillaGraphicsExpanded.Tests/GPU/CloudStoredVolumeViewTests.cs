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
        scene.Draw(settings); // Warm both disjoint temporal destinations before checking stable preparation.
        int prepared=scene.ViewPassPreparations;
        Assert.Equal(baseline[1],scene.Draw(settings)[1]);
        Assert.Equal(prepared,scene.ViewPassPreparations);
        // Replacing an attachment on the same framebuffer must refresh only its native pass.
        scene.RefreshOutputMetadata();Assert.Equal(baseline[1],scene.Draw(settings)[1]);
        Assert.Equal(prepared+1,scene.ViewPassPreparations);
        scene.ReleaseViewTargets();Assert.Equal(baseline[1],scene.Draw(settings)[1]);
        Assert.Equal(prepared+3,scene.ViewPassPreparations);
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
                Assert.Equal(divisor==1?0:108*((5+divisor-1)/divisor)*((3+divisor-1)/divisor)+
                    44*((5+divisor*2-1)/(divisor*2))*((3+divisor*2-1)/(divisor*2)),edge.ViewTargetBytes);
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
        using(var temporal=new CloudStoredVolumeViewScene(Programs,16,16))
        {
            temporal.Draw(settings);Assert.False(temporal.HistoryCompatible);
            for(int frame=0;frame<4;frame++)
            {
                temporal.AdvanceFrame();temporal.Draw(settings,scale:frame%2+1);
                Assert.True(temporal.HistoryCompatible);
                var receipt=temporal.TraceReceipts;int fresh=0;
                for(int pixel=0;pixel<receipt.Length/4;pixel++)if(receipt[pixel*4+1]>=1)fresh++;
                Assert.Equal(16,fresh); // One exact fresh ray per 2x2 reduced tile.
            }
            Assert.Contains(temporal.HistoryReceipts.Where((_,i)=>i%4==3),count=>count>1);
            temporal.AdvanceFrame(materialMotion:.1f);temporal.Draw(settings);
            Assert.True(temporal.HistoryCompatible);
            Assert.Contains(temporal.HistoryReceipts.Where((_,i)=>i%4==3),count=>count>1);
            temporal.AdvanceFrame(yaw:.001f);temporal.Draw(settings);
            Assert.True(temporal.HistoryCompatible);
            Assert.Contains(temporal.HistoryReceipts.Where((_,i)=>i%4==3),count=>count>1);
            temporal.SetDepths(50,50,50);temporal.AdvanceFrame();temporal.Draw(settings);
            Assert.All(temporal.HistoryReceipts.Where((_,i)=>i%4==3),count=>Assert.InRange(count,0,1));
            // Removed density is observed at each pixel's next scheduled fresh ray; no global
            // content reset is required. The four-phase sweep removes all retained contribution.
            temporal.SeedDensity(0);float[][] cleared=[];
            for(int phase=0;phase<4;phase++){temporal.AdvanceFrame();cleared=temporal.Draw(settings);}
            Assert.All(cleared[1],value=>Assert.Equal(1,value));
            temporal.AdvanceFrame(cameraCut:true);temporal.Draw(settings);Assert.False(temporal.HistoryCompatible);
            temporal.ReleaseViewTargets();temporal.AdvanceFrame();temporal.Draw(settings);
            Assert.False(temporal.HistoryCompatible);
            temporal.AdvanceFrame();temporal.Draw(settings);Assert.True(temporal.HistoryCompatible);
            temporal.AdvanceFrame();temporal.Draw(settings with{LightingRaysPerUpdate=327680});Assert.True(temporal.HistoryCompatible);
            temporal.ReplaceViewMaterialIdentity();temporal.AdvanceFrame();temporal.Draw(settings);
            Assert.False(temporal.HistoryCompatible);
        }
    }
    /// <summary>Distinguishes carried information from fresh spatial fallback and verifies actual weight/count arithmetic.</summary>
    [Fact]
    public void TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(Programs,16,16);
        var settings=CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=2};scene.Draw(settings);
        var original=scene.HistoryCloud;var metadata=scene.HistoryReceipts;
        var authored=(float[])original.Clone();var diagnostic=new float[metadata.Length];
        for(int i=0;i<metadata.Length;i+=4){authored[i]+=1;metadata[i+3]=4;}
        scene.SeedHistory(authored,metadata,diagnostic);scene.AdvanceFrame();scene.Draw(settings);
        var actual=scene.HistoryCloud;var receipts=scene.HistoryReceipts;var info=scene.HistoryDiagnostics;
        int carried=0,fresh=0;
        for(int i=0;i<info.Length;i+=4)
        {
            if(info[i+1]==2)
            {
                carried++;Assert.Equal(1,info[i]);Assert.Equal(4,receipts[i+3]);Assert.Equal(1,info[i+3]);
                for(int c=0;c<4;c++)Assert.InRange(Math.Abs(actual[i+c]-authored[i+c]),0,2e-5f);
            }
            if(info[i+1]==1)
            {
                fresh++;Assert.Equal(0,info[i+3]);
                // Half-float diagnostics round the weight; metadata retains float32 arithmetic.
                Assert.InRange(Math.Abs(receipts[i+3]-1/(1-info[i])),0,.02f);
                if(info[i]==0)Assert.Equal(1,receipts[i+3]);
            }
        }
        Assert.Equal(16,fresh);Assert.True(carried>0);
        // Diagnostic attachments are optional; changing them keeps the physical images,
        // independent-observation confidence and required age alive across frame boundaries.
        scene.CaptureDiagnostics=false;scene.AdvanceFrame();scene.Draw(settings);
        Assert.True(scene.HistoryCompatible);Assert.Empty(scene.HistoryDiagnostics);
        Assert.Equal(92*8*8+44*4*4,scene.ViewTargetBytes);
        Assert.Contains(scene.HistoryReceipts.Where((_,i)=>i%4==3),value=>value>1);
        scene.CaptureDiagnostics=true;scene.AdvanceFrame();scene.Draw(settings);
        Assert.True(scene.HistoryCompatible);Assert.NotEmpty(scene.HistoryDiagnostics);
        Assert.Equal(108*8*8+44*4*4,scene.ViewTargetBytes);
        // One independently authored observation cannot define a neighborhood range. Its
        // explicit half-radiance reactivity leaves .4 old weight and confidence5/3.
        var priorCloud=new float[metadata.Length];var priorBins=Enumerable.Repeat(.5f,8*8*3).ToArray();
        for(int i=0;i<metadata.Length;i+=4)
        {
            priorCloud[i]=priorCloud[i+1]=priorCloud[i+2]=1;priorCloud[i+3]=.5f;
            metadata[i+1]=1;metadata[i+2]=10;metadata[i+3]=4;
        }
        scene.SeedHistory(priorCloud,metadata,new float[metadata.Length],priorBins);
        var rayCloud=new float[4*4*4];var rayMeta=new float[rayCloud.Length];
        rayCloud[0]=rayCloud[1]=rayCloud[2]=2;rayCloud[3]=.5f;
        rayMeta[0]=metadata[0];rayMeta[1]=1;rayMeta[2]=10;
        var single=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        var ordinary=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta,diagnostics:false);
        for(int channel=0;channel<3;channel++)Assert.Equal(single[channel],ordinary[channel]);
        Assert.Empty(ordinary[3]);Assert.Equal(single[4],ordinary[4]);
        int observed=Enumerable.Range(0,64).Single(p=>single[3][p*4+1]==1)*4;
        Assert.InRange(Math.Abs(single[0][observed]-1.6f),0,2e-6f);
        Assert.InRange(Math.Abs(single[2][observed+3]-5f/3),0,2e-6f);
        Assert.Equal(0,single[3][observed+2]);
        rayCloud[0]=rayCloud[1]=rayCloud[2]=0;
        var reactive=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        Assert.Equal(0,reactive[3][observed]);Assert.Equal(1,reactive[2][observed+3]);Assert.Equal(11,reactive[3][observed+2]);
        rayCloud[3]=0;rayMeta[1]=2;rayMeta[2]=0;
        var empty=scene.FilterAuthored(rayCloud,Enumerable.Repeat(1f,4*4*3).ToArray(),rayMeta);
        Assert.Equal(2,empty[2][observed+1]);Assert.Equal(1,empty[2][observed+3]);
        Assert.Equal(0,empty[0][observed+3]);Assert.Equal(4,empty[3][observed+2]);
        rayCloud[3]=.5f;rayMeta[1]=1;rayMeta[2]=10;
        for(int i=3;i<diagnostic.Length;i+=4)diagnostic[i]=7;
        scene.SeedHistory(priorCloud,metadata,diagnostic,priorBins);
        var expired=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        Assert.DoesNotContain(2,expired[3].Where((_,i)=>i%4==1));
        Assert.Contains(8,expired[3].Where((_,i)=>i%4==2));
        // Exactly centred history has one nonzero bilinear tap. Invalid adjacent taps must
        // not reject it; the static centre also has no distance or camera-depth ambiguity.
        using var centre=new CloudStoredVolumeViewScene(Programs,6,6);centre.Draw(settings);
        var centreCloud=centre.HistoryCloud;var centreMeta=centre.HistoryReceipts;
        for(int i=0;i<centreMeta.Length;i+=4)centreMeta[i+3]=i==16?4:0;
        centre.SeedHistory(centreCloud,centreMeta,new float[centreMeta.Length]);
        centre.AdvanceFrame();centre.Draw(settings);
        Assert.Equal(0,centre.HistoryDiagnostics[18]);
        Assert.True(centre.HistoryReceipts[19]>=4);
        // A failed own ray cannot be hidden by retained information or a nearby valid prefix.
        var invalid=scene.FilterAuthored(new float[4*4*4],Enumerable.Repeat(1f,4*4*3).ToArray(),new float[4*4*4]);
        var failed=invalid[3];var failedMeta=invalid[2];
        Assert.Contains(9,failed.Where((_,i)=>i%4==2));
        for(int i=0;i<failed.Length;i+=4)if(failed[i+2]==9)Assert.Equal(0,failedMeta[i+1]);
    }
    /// <summary>Records a bounded authored view workload without a speed or approximation threshold.</summary>
    [Fact]
    public void RecordStoredViewCost()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(Programs,3440,1377);
        using var timer=GpuTimerQuery.Create("Tests.Cloud.StoredVolume.ViewCost");
        foreach(float sigma in new[]{.0078125f,0f})
        foreach(bool reuse in new[]{false,true})
        {
            scene.SeedDensity(sigma);var times=new List<double>();var cpuTimes=new List<double>();var allocations=new List<long>();
            // Preparation and clear precede each query; synchronous test query reads follow it.
            for(int i=0;i<4;i++)
            {
                if(reuse)scene.AdvanceFrame();
                scene.PrepareDraw(CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=4});
                timer.Begin();long allocatedBefore=GC.GetAllocatedBytesForCurrentThread();long started=System.Diagnostics.Stopwatch.GetTimestamp();
                bool drawn=scene.DrawOnly();
                double cpuMilliseconds=System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;long allocated=GC.GetAllocatedBytesForCurrentThread()-allocatedBefore;
                timer.End();Assert.True(drawn);
                double milliseconds=timer.GetResultNanoseconds()/1e6;
                if(i>0){times.Add(milliseconds);cpuTimes.Add(cpuMilliseconds);allocations.Add(allocated);}
            }
            times.Sort();cpuTimes.Sort();allocations.Sort();
            output.WriteLine($"Stored view 3440x1377 sigma={sigma} history={reuse}: median {times[1]:F4} ms; samples {string.Join(", ",times.Select(t=>t.ToString("F4")))} ms. CPU submission median {cpuTimes[1]:F4} ms; samples {string.Join(", ",cpuTimes.Select(t=>t.ToString("F4")))} ms; allocated median {allocations[1]} bytes; samples {string.Join(", ",allocations)} bytes. Pass preparations {scene.ViewPassPreparations}. GPU: {gpu}.");
            var receipt=scene.TraceReceipts;var history=scene.HistoryReceipts;
            output.WriteLine($"Trace receipts: fresh={receipt.Where((_,i)=>i%4==1).Count(v=>v>=1)}, visits={receipt.Where((_,i)=>i%4==3).Sum()}, admitted history={history.Where((_,i)=>i%4==3).Count(v=>v>1)}/{history.Length/4}; targets={scene.ViewTargetBytes}bytes.");
            var contribution=scene.HistoryDiagnostics;
            output.WriteLine($"Actual prior contribution: {contribution.Where((_,i)=>i%4==0).Count(v=>v>0)}/{contribution.Length/4}; carried={contribution.Where((_,i)=>i%4==1).Count(v=>v==2)}, fresh={contribution.Where((_,i)=>i%4==1).Count(v=>v==1)}, fallback={contribution.Where((_,i)=>i%4==1).Count(v=>v==3)}; maximum observation age={contribution.Where((_,i)=>i%4==3).DefaultIfEmpty().Max()}.");
            using var trace=GpuTimerQuery.Create("Tests.Cloud.View.Trace");
            using var temporal=GpuTimerQuery.Create("Tests.Cloud.View.Temporal");
            using var compose=GpuTimerQuery.Create("Tests.Cloud.View.Compose");
            scene.SetPassTimers([trace,temporal,compose]);
            try
            {
                if(reuse)scene.AdvanceFrame();scene.PrepareDraw(CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=4});
                Assert.True(scene.DrawOnly());
                output.WriteLine($"Separate pass queries: trace={trace.GetResultNanoseconds()/1e6:F4}ms, temporal={temporal.GetResultNanoseconds()/1e6:F4}ms, compose={compose.GetResultNanoseconds()/1e6:F4}ms; query overhead included, not the independent total above.");
            }
            finally{scene.SetPassTimers(null);}
        }
        scene.SeedDensity(.0078125f);
        foreach(string scenario in new[]{"camera","wind","weather/light","depth edge","same-allocation replacement"})
        {
            // These single paired diagnostics resolve changing-input coverage; the stationary
            // medians above remain the performance comparison, not a broad acceptance matrix.
            scene.AdvanceFrame();scene.PrepareDraw(CloudVolumeViewSettings.Default with{Detail=false});Assert.True(scene.DrawOnly());
            if(scenario=="weather/light"){scene.SeedDensity(.00390625f);scene.Atmosphere=scene.Atmosphere with{Solar=new Vector3(31),Environment=new Vector3(11)};}
            if(scenario=="depth edge")scene.SetMaterialDepthColumns(1720,50,100);
            if(scenario=="same-allocation replacement")scene.ReplaceViewMaterialIdentity();
            scene.AdvanceFrame(materialMotion:scenario=="wind"?.1f:0,yaw:scenario=="camera"?.001f:0);
            for(int observation=0;observation<2;observation++)
            {
                scene.PrepareDraw(CloudVolumeViewSettings.Default with{Detail=false});
                timer.Begin();Assert.True(scene.DrawOnly());timer.End();double milliseconds=timer.GetResultNanoseconds()/1e6;
                var raw=scene.TraceReceipts;var retained=scene.HistoryReceipts;
                output.WriteLine($"Changing {scenario}[{observation}]: history={scene.HistoryCompatible}, total={milliseconds:F4}ms, fresh={raw.Where((_,i)=>i%4==1).Count(v=>v>=1)}, visits={raw.Where((_,i)=>i%4==3).Sum()}, admitted={retained.Where((_,i)=>i%4==3).Count(v=>v>1)}/{retained.Length/4}; repeated same-frame observation forces fresh baseline.");
            }
        }
        output.WriteLine("Actual typed compact quarter-resolution trace plus full-resolution spatial reconstruction, 119 bytes per reduced pixel with diagnostic capture, compact raw trace and two histories (ceil edges additional), exact occupied-cell intervals, authored uniform extinction .0078125/empty0, direct .25/.75 and diffuse12, depths100, detail disabled, default shadows/diffuse/stop. One warmup and three stationary samples; changing-input diagnostics are single pairs. External preparation, clears and readbacks excluded; internal clears and trace/temporal/composition included; no live FPS or appearance qualification; contention uncontrolled.");
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
