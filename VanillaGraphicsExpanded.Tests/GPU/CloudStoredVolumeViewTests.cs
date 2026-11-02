using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Tests.GPU.Assertions;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System.Numerics;
using VanillaGraphicsExpanded.PBR.Clouds.Volume;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies stored-field OIT arithmetic, independent clipping/encoding and compiled fallback paths.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
public sealed class CloudStoredVolumeViewTests(HeadlessGLFixture fixture,Xunit.ITestOutputHelper output):LumOnShaderFunctionalTestBase(fixture)
{


    private readonly string gpu=fixture.GLRenderer;
    #region Public API
    /// <summary>Checks typed borrowed view pixels against the same owned inputs.</summary>
    [Fact]
    public void BorrowedInputsMatchOwnedOutput()
    {
        EnsureShaderTestAvailable();
        var settings=CloudVolumeViewSettings.Default;
        using var ownedScene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,18,14);
        using var expected=ownedScene.DrawAndCapture(settings);
        using var borrowedScene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,18,14);
        OpenGlStateCache.Current.InvalidateAll();
        using var actual=borrowedScene.DrawBorrowedInputs(settings);
        for(int image=0;image<expected.Count;image++)ImageAssertions.Comparisons.CompareHistory(actual[image].Texture,expected[image]).AssertEqual();
        Assert.Equal(OpenTK.Graphics.OpenGL.ErrorCode.NoError,OpenTK.Graphics.OpenGL.GL.GetError());
    }

    /// <summary>Checks actual trace/filter agreement, odd-edge coverage and bounded refresh across the periodic schedule.</summary>
    [Fact]
    public void RefreshSchedulesCoverOddTilesWithoutStarvation()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,18,14);
        scene.SetMaterialDepthRamp();
        var settings=CloudVolumeViewSettings.Default with{TraceDivisor=2};
        // Exercise two complete periods of the delivered schedule, including unsigned wrap.
        {
            scene.ReleaseViewTargets();
            scene.AdvanceFrame(absoluteFrame:uint.MaxValue-32);scene.Draw(settings);
            using var expectedDepth=RHICommandContext.Current.CreateDynamicTexture2D(new()
            {
                Dimension=TextureDimension.Two,Format=TextureFormat.R32f,Width=9,Height=7,
                MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest
            }).RequireResource();
            expectedDepth.UploadDataImmediate(Enumerable.Range(0,63)
                .Select(pixel=>.2f+((pixel/9)*2*18+(pixel%9)*2)*.001f).ToArray());
            using var initial=RHICommandContext.Current.CreateDynamicTexture2D(new()
            {
                Dimension=TextureDimension.Two,Format=TextureFormat.Rgba32f,Width=9,Height=7,
                MipLevels=1,Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest
            }).RequireResource();
            initial.UploadDataImmediate(Enumerable.Range(0,63).SelectMany(_=>new[]{-1f,0,0,0}).ToArray());
            var projection = ImageAssertions.Projections;
            GpuTexture state=initial;
            var replay=new List<GpuImageSnapshot>();
            try
            {
                for(int frame=0;frame<64;frame++)
                {
                    scene.AdvanceFrame();scene.Draw(settings);Assert.True(scene.HistoryCompatible);
                    var receipt=Assert.IsAssignableFrom<GpuTexture>(scene.HistoryDiagnostics);
                    var observation=ImageAssertions.Predicates.Predicate(receipt, CloudPredicates.CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation,scene.TraceReceipts,expectedDepth,state,new(frame,0,0,0),mappedSources:true);
                    observation.AssertEqual();Assert.Equal(20u,observation.MatchingTexels);
                    if(frame<32)replay.Add(new(receipt));
                    else ImageAssertions.Comparisons.CompareHistory(receipt,replay[frame-32],channelMask:2).AssertEqual();
                    var next=projection.Apply(state, CloudProjections.CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation_2,
                        new(frame,0,0,0),receipt);
                    if(!ReferenceEquals(state,initial))state.Dispose();
                    state=next;
                    if(frame%4==3)
                    {
                        ImageAssertions.Statistics.Inspect(state,1,1,channelMask:2).AssertFiniteRange();
                        next=projection.Apply(state, CloudProjections.CloudStoredVolumeViewTests_RefreshSchedulesCoverOddTilesWithoutStarvation_3);
                        state.Dispose();state=next;
                    }
                }
            }
            finally
            {
                if(!ReferenceEquals(state,initial))state.Dispose();
                foreach(var image in replay)image.Dispose();
            }
        }
    }
    /// <summary>Measures complete screen integration at fixed resolution and fields across the five view profiles.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void RecordQualityProfileViewCost()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,1920,1080);
        using var timer=GpuTimerQuery.Create("Tests.Cloud.Quality.View");
        foreach(int level in Enumerable.Range(0,5))
        {
            var profile=CloudVolumeQuality.Resolve(level);var times=new List<double>();
            var settings=CloudVolumeViewSettings.Default with{Quality=level,Stop=profile.ViewStop};
            for(int sample=0;sample<4;sample++)
            {
                scene.AdvanceFrame();scene.PrepareDraw(settings);
                timer.Begin();Assert.True(scene.DrawOnly());timer.End();
                double ms=timer.GetResultNanoseconds()/1e6;if(sample>0)times.Add(ms);
            }
            times.Sort();output.WriteLine($"Quality {level}: complete view median={times[1]:F4}ms, 1920x1080, quarter trace, identical authored fields, target bytes={scene.ViewTargetBytes}; one warmup/three samples, preparation/readback excluded, CPU submission and uncontrolled contention included.");
        }
    }
    /// <summary>The actual full-view shader consumes stored density and Q plus D once, with independent valid calibrations.</summary>
    [Fact]
    public void StoredViewUsesVoxelTransportAndNativeProducts()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs);
        var settings=CloudVolumeViewSettings.Default with{Detail=false,Stop=0,TraceDivisor=2};
        using var baseline=scene.DrawAndCapture(settings);
        scene.Draw(settings); // Warm both disjoint temporal destinations before checking stable preparation.
#if DEBUG
        int prepared=scene.ViewPassPreparations;
#endif
        AssertTransmission(scene,settings,baseline[1]);
#if DEBUG
        Assert.Equal(prepared,scene.ViewPassPreparations);
#endif
        // Replacing equal-format storage refreshes the surface realization without rebuilding its compatible pipeline.
        scene.RefreshOutputMetadata();AssertTransmission(scene,settings,baseline[1]);
#if DEBUG
        Assert.Equal(prepared,scene.ViewPassPreparations);
#endif
        scene.ReleaseViewTargets();AssertTransmission(scene,settings,baseline[1]);
#if DEBUG
        Assert.Equal(prepared+2,scene.ViewPassPreparations);
#endif
        Assert.InRange(Math.Abs(GpuLocalRead.Scalar(baseline[1].Texture,0)-MathF.Exp(-.0078125f*100)),0,2e-5f);
        float phase0=.64f/(4*MathF.PI*MathF.Pow(1.36f,1.5f));float phase1=1.6f/(4*MathF.PI*.4f*.4f);
        float source=.9f*(30*.25f*phase1+3*.75f*phase0)+12;
        for(int bucket=0;bucket<3;bucket++)
        {
            float[] image=baseline[bucket+3].Texture.ReadPixelsRegion(0,0,1,1);
            Assert.InRange(Math.Abs(image[3]-(1-GpuLocalRead.Scalar(baseline[1].Texture,0))*(1-GpuLocalRead.Scalar(baseline[0].Texture,bucket))),0,2e-5f);
            Assert.InRange(Math.Abs(image[0]/image[3]-source),0,3e-4f);
        }
        using var encoded=scene.DrawAndCapture(settings,scale:2);ImageAssertions.Comparisons.CompareHistory(encoded[0].Texture,baseline[0]).AssertEqual();ImageAssertions.Comparisons.CompareHistory(encoded[1].Texture,baseline[1]).AssertEqual();
        Assert.InRange(Math.Abs(GpuLocalRead.Scalar(encoded[3].Texture,0)-2*GpuLocalRead.Scalar(baseline[3].Texture,0)),0,2e-5f);
        using var disabled=scene.DrawAndCapture(settings with{SelfShadowing=false,Diffuse=false});
        float fallback=.9f*(30*phase1+3*phase0)+9;
        Assert.InRange(Math.Abs(GpuLocalRead.Scalar(disabled[3].Texture,0)/GpuLocalRead.Scalar(disabled[3].Texture,3)-fallback),0,3e-4f);
        foreach(var depths in new[]{(50f,100f,100f),(100f,50f,100f),(100f,100f,50f)})
        {
            scene.SetDepths(depths.Item1,depths.Item2,depths.Item3);
            scene.Draw(settings);using var depthOutput=scene.CaptureOutput();
            Assert.InRange(Math.Abs(GpuLocalRead.Scalar(depthOutput[1].Texture,0)-MathF.Exp(-.0078125f*50)),0,2e-5f);
        }
        scene.SeedDensity(0);using var empty=scene.DrawAndCapture(settings);Assert.Equal(1,GpuLocalRead.Scalar(empty[1].Texture,0));for(int image=2;image<empty.Count;image++)ImageAssertions.Comparisons.CompareConstant(empty[image].Texture,0).AssertEqual();
        AssertUniformOutput(scene,settings,.25f);
        scene.SeedDensity(float.NaN);
        AssertUniformOutput(scene,settings,.25f);
        scene.ReleaseViewTargets(); // The shared asset now belongs to the replacement resolution fixture.
        using(var edge=new CloudStoredVolumeViewScene(ImageAssertions, Programs,5,3))
        {
            // Resolution changes retire only screen-sized products and preserve cloud-space density.
            edge.SetMaterialDepthColumns(2,.1f,100);
            var retained=edge.StoredDensity;
            foreach(int divisor in new[]{2,4,1,2})
            {
                var selection=settings with{TraceDivisor=divisor};edge.SeedDensity(.0078125f);
                using var clipped=edge.DrawAndCapture(selection);for(int image=0;image<clipped.Count;image++)ImageAssertions.Statistics.Inspect(clipped[image].Texture).AssertFiniteRange();
                Assert.Equal(divisor==1?0:108*((5+divisor-1)/divisor)*((3+divisor-1)/divisor)+
                    44*((5+divisor*2-1)/(divisor*2))*((3+divisor*2-1)/(divisor*2)),edge.ViewTargetBytes);
                Assert.Same(retained,edge.StoredDensity);
                if(divisor!=4)ImageAssertions.Predicates.Predicate(clipped[1].Texture, CloudPredicates.CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts).AssertEqual();
                edge.SeedDensity(0);
                AssertUniformOutput(edge,selection,.25f);
                edge.SeedDensity(float.NaN);
                AssertUniformOutput(edge,selection,.25f);
            }
        }
        using(var fog=new CloudStoredVolumeViewScene(ImageAssertions, Programs,fogColor:Vector3.UnitY,fogMinimum:1){Underwater=1})
        {
            // A fully selected medium uses the green shared-frame fog without changing cloud extinction.
            using var covered=fog.DrawAndCapture(settings);ImageAssertions.Comparisons.CompareHistory(covered[1].Texture,baseline[1]).AssertEqual();
            for(int bucket=0;bucket<3;bucket++)
            {
                float[] image=covered[bucket+3].Texture.ReadPixelsRegion(0,0,1,1);Assert.Equal(0,image[0]);Assert.Equal(0,image[2]);
                Assert.InRange(Math.Abs(image[1]/image[3]-32),0,3e-4f);
            }
        }
        scene.Atmosphere=scene.Atmosphere with{CalibrationGeneration=0};scene.Draw(settings,succeeds:false);
        using(var temporal=new CloudStoredVolumeViewScene(ImageAssertions, Programs,16,16))
        {
            temporal.Draw(settings);Assert.False(temporal.HistoryCompatible);
            for(int frame=0;frame<4;frame++)
            {
                temporal.AdvanceFrame();temporal.Draw(settings,scale:frame%2+1);
                Assert.True(temporal.HistoryCompatible);
                var receipt=temporal.TraceReceipts;
                Assert.Equal(16u,ImageAssertions.Predicates.Predicate(receipt, CloudPredicates.CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_2).MatchingTexels); // One exact fresh ray per 2x2 reduced tile.
            }
            Assert.True(ImageAssertions.Predicates.Predicate(temporal.HistoryReceipts, CloudPredicates.CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_3).MatchingTexels>0);
            temporal.AdvanceFrame(materialMotion:.1f);temporal.Draw(settings);
            Assert.True(temporal.HistoryCompatible);
            Assert.True(ImageAssertions.Predicates.Predicate(temporal.HistoryReceipts, CloudPredicates.CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_3).MatchingTexels>0);
            temporal.AdvanceFrame(yaw:.001f);temporal.Draw(settings);
            Assert.True(temporal.HistoryCompatible);
            Assert.True(ImageAssertions.Predicates.Predicate(temporal.HistoryReceipts, CloudPredicates.CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_3).MatchingTexels>0);
            temporal.SetDepths(50,50,50);temporal.AdvanceFrame();temporal.Draw(settings);
            ImageAssertions.Statistics.Inspect(temporal.HistoryReceipts,0,1,channelMask:8).AssertFiniteRange();
            // Removed density is observed at each pixel's next scheduled fresh ray; no global
            // content reset is required. Across changing sweeps every pixel is revisited within
            // seven frames; this is an exact removal/coverage contract, not an image-quality gate.
            temporal.SeedDensity(0);
            for(int phase=0;phase<7;phase++){temporal.AdvanceFrame();temporal.Draw(settings);}
            using var cleared=temporal.CaptureOutput();ImageAssertions.Comparisons.CompareConstant(cleared[1].Texture,1).AssertEqual();
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
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,16,16);
        var settings=CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=2};scene.Draw(settings);
        var projection = ImageAssertions.Projections;
        using var original=new GpuImageSnapshot(scene.HistoryCloud);
        using var originalMetadata=new GpuImageSnapshot(scene.HistoryReceipts);
        using var authored=projection.Apply(original.Texture, CloudProjections.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight);
        using var authoredMetadata=projection.Apply(originalMetadata.Texture, CloudProjections.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_2);
        var diagnostic=new float[64*4];
        scene.SeedHistory(authored,authoredMetadata,diagnostic);scene.AdvanceFrame();scene.Draw(settings);
        var actual=scene.HistoryCloud;var receipts=scene.HistoryReceipts;var info=scene.HistoryDiagnostics!;
        var carriedObservation=ImageAssertions.Predicates.Predicate(info, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_3,receipts,actual,authored);
        carriedObservation.AssertEqual();uint carried=carriedObservation.MatchingTexels;
        var freshObservation=ImageAssertions.Predicates.Predicate(info, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_4,receipts);
        freshObservation.AssertEqual();uint fresh=freshObservation.MatchingTexels;
        Assert.Equal(16u,fresh);Assert.True(carried>0);
        // Diagnostic attachments are optional; changing them keeps the physical images,
        // independent-observation confidence and required age alive across frame boundaries.
        scene.CaptureDiagnostics=false;scene.AdvanceFrame();scene.Draw(settings);
        Assert.True(scene.HistoryCompatible);Assert.Null(scene.HistoryDiagnostics);
        Assert.Equal(92*8*8+44*4*4,scene.ViewTargetBytes);
        Assert.True(ImageAssertions.Predicates.Predicate(scene.HistoryReceipts, CloudPredicates.CloudStoredVolumeViewTests_StoredViewUsesVoxelTransportAndNativeProducts_3).MatchingTexels>0);
        scene.CaptureDiagnostics=true;scene.AdvanceFrame();scene.Draw(settings);
        Assert.True(scene.HistoryCompatible);Assert.NotNull(scene.HistoryDiagnostics);
        Assert.Equal(108*8*8+44*4*4,scene.ViewTargetBytes);
        // One independently authored observation cannot define a neighborhood range. Its
        // radiance change retains .8 old weight and confidence5 when support is unchanged.
        float depth=GpuLocalRead.Scalar(originalMetadata.Texture,0);
        ImageAssertions.Comparisons.CompareConstant(originalMetadata.Texture,depth,channelMask:1).AssertEqual();
        var metadata=new float[64*4];
        var priorCloud=new float[metadata.Length];var priorBins=Enumerable.Repeat(.5f,8*8*3).ToArray();
        for(int i=0;i<metadata.Length;i+=4)
        {
            priorCloud[i]=priorCloud[i+1]=priorCloud[i+2]=1;priorCloud[i+3]=.5f;
            metadata[i]=depth;metadata[i+1]=1;metadata[i+2]=10;metadata[i+3]=4;
        }
        scene.SeedHistory(priorCloud,metadata,new float[metadata.Length],priorBins);
        var rayCloud=new float[4*4*4];var rayMeta=new float[rayCloud.Length];
        rayCloud[0]=rayCloud[1]=rayCloud[2]=2;rayCloud[3]=.5f;
        rayMeta[0]=metadata[0];rayMeta[1]=1;rayMeta[2]=10;
        using var single=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        using var ordinary=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta,diagnostics:false);
        ImageAssertions.Predicates.Predicate(single.Cloud, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5,ordinary.Cloud).AssertEqual();
        ImageAssertions.Predicates.Predicate(single.Bins, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_6,ordinary.Bins).AssertEqual();
        ImageAssertions.Predicates.Predicate(single.Metadata, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_5,ordinary.Metadata).AssertEqual();
        Assert.Null(ordinary.Diagnostics);ImageAssertions.Predicates.Predicate(single.Age, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_7,ordinary.Age).AssertEqual();
        var selected=AssertObserved(single,single.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_18);
        Assert.Equal(1u,selected.MatchingTexels);
        // A uniform current RGB change retains compatible old illumination;
        // all sixteen fresh locations use the same declared observation response as one ray.
        var uniformRays=new float[rayCloud.Length];var uniformMeta=new float[rayMeta.Length];
        for(int p=0;p<16;p++){Array.Copy(rayCloud,0,uniformRays,p*4,4);Array.Copy(rayMeta,0,uniformMeta,p*4,4);}
        using var uniformStep=scene.FilterAuthored(uniformRays,Enumerable.Repeat(.5f,4*4*3).ToArray(),uniformMeta);
        var uniformFresh=ImageAssertions.Predicates.Predicate(uniformStep.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_8,uniformStep.Cloud,uniformStep.Metadata);
        uniformFresh.AssertEqual();Assert.Equal(16u,uniformFresh.MatchingTexels);
        var uniformCarry=ImageAssertions.Predicates.Predicate(uniformStep.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_9,
            uniformStep.Cloud,uniformStep.Metadata);
        uniformCarry.AssertEqual();Assert.Equal(48u,uniformCarry.MatchingTexels);
        rayCloud[0]=rayCloud[1]=rayCloud[2]=0;
        using var reactive=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        AssertObserved(reactive,single.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_19);
        rayCloud[3]=.25f;
        using var changedOpacity=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        AssertObserved(changedOpacity,single.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_20);
        rayCloud[3]=0;rayMeta[1]=2;rayMeta[2]=0;
        using var empty=scene.FilterAuthored(rayCloud,Enumerable.Repeat(1f,4*4*3).ToArray(),rayMeta);
        AssertObserved(empty,single.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_21);
        rayCloud[3]=.5f;rayMeta[1]=1;rayMeta[2]=10;
        for(int i=3;i<diagnostic.Length;i+=4)diagnostic[i]=7;
        scene.SeedHistory(priorCloud,metadata,diagnostic,priorBins);
        using var expired=scene.FilterAuthored(rayCloud,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta);
        Assert.Equal(0u,ImageAssertions.Predicates.Predicate(expired.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_10).MatchingTexels);
        Assert.True(ImageAssertions.Predicates.Predicate(expired.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_11).MatchingTexels>0);
        // Exactly centred history has one nonzero bilinear tap. Invalid adjacent taps must
        // not reject it; the static centre also has no distance or camera-depth ambiguity.
        using var centre=new CloudStoredVolumeViewScene(ImageAssertions, Programs,6,6);centre.Draw(settings);
        using var centreMeta=projection.Apply(centre.HistoryReceipts, CloudProjections.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_12);
        centre.SeedHistory(centre.HistoryCloud,centreMeta,new float[9*4]);
        centre.AdvanceFrame();centre.Draw(settings);
        Assert.Equal(0,GpuLocalRead.Scalar(centre.HistoryDiagnostics!,18));
        Assert.True(GpuLocalRead.Scalar(centre.HistoryReceipts,19)>=4);
        // With a rotated but now stationary view, every carried pixel owns its exact age.
        // Matrix roundoff must not let a nearly zero neighboring weight age a fresh pixel.
        centre.AdvanceFrame(cameraCut:true,yaw:.001f);centre.Draw(settings);
        using var stationaryMeta=projection.Apply(centre.HistoryReceipts, CloudProjections.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_2);
        var stationaryAge=new float[9*4];
        for(int pixel=0;pixel<9;pixel++)stationaryAge[pixel*4+3]=pixel%2==0?0:6;
        centre.SeedHistory(centre.HistoryCloud,stationaryMeta,stationaryAge);
        centre.AdvanceFrame();centre.Draw(settings);
        var stationary=ImageAssertions.Predicates.Predicate(centre.HistoryDiagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_13,centre.HistoryReceipts);
        stationary.AssertEqual();Assert.True(stationary.MatchingTexels>0);
        // Subpixel material reprojection straddles a contributing/empty boundary. The selected
        // compatible centre must retain its complete physical tuple and its own age/confidence;
        // rejected empty taps must not dilute radiance, opacity or bucket transmissions.
        var edgeCloud=(float[])priorCloud.Clone();var edgeMeta=(float[])metadata.Clone();
        var edgeBins=(float[])priorBins.Clone();var edgeAge=new float[metadata.Length];
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            int p=y*8+x;edgeAge[p*4+3]=2;
            if(x<4)continue;
            Array.Clear(edgeCloud,p*4,4);edgeMeta[p*4+1]=2;edgeMeta[p*4+2]=0;
            Array.Fill(edgeBins,1f,p*3,3);
        }
        var edgeRays=new float[4*4*4];var edgeRayMeta=new float[edgeRays.Length];
        for(int p=0;p<16;p++)
        {
            edgeRays[p*4]=edgeRays[p*4+1]=edgeRays[p*4+2]=1;edgeRays[p*4+3]=.5f;
            edgeRayMeta[p*4]=metadata[0];edgeRayMeta[p*4+1]=1;edgeRayMeta[p*4+2]=10;
        }
        scene.SeedHistory(edgeCloud,edgeMeta,edgeAge,edgeBins);
        using var boundary=scene.FilterAuthored(edgeRays,Enumerable.Repeat(.5f,4*4*3).ToArray(),edgeRayMeta,
            translation:new Vector3(.625f,0,0));
        ImageAssertions.Predicates.Predicate(boundary.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_14,boundary.Cloud,boundary.Bins,boundary.Metadata).AssertEqual();
        ImageAssertions.Predicates.Predicate(boundary.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_15,boundary.Age).AssertEqual();
        // The same nearest candidate cannot bypass a foreground mismatch or expired observation.
        for(int p=0;p<64;p++)edgeMeta[p*4]=0;
        scene.SeedHistory(edgeCloud,edgeMeta,edgeAge,edgeBins);
        using var occluded=scene.FilterAuthored(edgeRays,Enumerable.Repeat(.5f,4*4*3).ToArray(),edgeRayMeta,
            translation:new Vector3(.625f,0,0));
        Assert.Equal(0u,ImageAssertions.Predicates.Predicate(occluded.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_10).MatchingTexels);
        for(int p=0;p<64;p++){edgeMeta[p*4]=metadata[0];edgeAge[p*4+3]=7;}
        scene.SeedHistory(edgeCloud,edgeMeta,edgeAge,edgeBins);
        using var oldEdge=scene.FilterAuthored(edgeRays,Enumerable.Repeat(.5f,4*4*3).ToArray(),edgeRayMeta,
            translation:new Vector3(.625f,0,0));
        Assert.Equal(0u,ImageAssertions.Predicates.Predicate(oldEdge.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_10).MatchingTexels);
        // A failed own ray cannot be hidden by retained information or a nearby valid prefix.
        using var invalid=scene.FilterAuthored(new float[4*4*4],Enumerable.Repeat(1f,4*4*3).ToArray(),new float[4*4*4]);
        var failed=invalid.Diagnostics!;var failedMeta=invalid.Metadata;
        Assert.True(ImageAssertions.Predicates.Predicate(failed, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_16).MatchingTexels>0);
        ImageAssertions.Predicates.Predicate(failed, CloudPredicates.CloudStoredVolumeViewTests_TemporalCarriesOnlyRealObservationsAndReportsEffectiveWeight_17,failedMeta).AssertEqual();
    }
    /// <summary>Moving history carries corresponding cloud support rather than unrelated same-screen metadata.</summary>
    [Fact]
    public void TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,16,16);
        scene.SetDepths(1000,1000,1000);
        scene.Draw(CloudVolumeViewSettings.Default with{Detail=false,TraceDivisor=2});
        var prior=new float[8*8*4];var metadata=new float[prior.Length];
        var bins=Enumerable.Repeat(1f,8*8*3).ToArray();
        for(int y=0;y<8;y++)for(int x=0;x<8;x++)
        {
            int p=y*8+x;metadata[p*4]=1;metadata[p*4+1]=x<4||x%2==1?1:2;metadata[p*4+3]=4;
            if(x>=4&&x%2==1)metadata[p*4+2]=99;
            if(x>=4)continue;
            metadata[p*4+2]=10;prior[p*4]=2;prior[p*4+1]=3;prior[p*4+2]=4;prior[p*4+3]=.5f;
            bins[p*3]=.2f;bins[p*3+1]=.4f;bins[p*3+2]=.6f;
        }
        scene.SeedHistory(prior,metadata,new float[prior.Length],bins);
        var rays=new float[4*4*4];var rayMeta=new float[rays.Length];
        for(int p=0;p<16;p++)
        {
            rays[p*4]=2;rays[p*4+1]=3;rays[p*4+2]=4;rays[p*4+3]=.5f;
            rayMeta[p*4]=1;rayMeta[p*4+1]=1;rayMeta[p*4+2]=10;
        }
        // A positive previous yaw maps current right-hand pixels onto the old cloud band.
        // Translation additionally changes the camera-relative distance even when its source
        // history distance is ten; the current neighbor is independent correspondence evidence.
        foreach(var motion in new[]{(Yaw:.55f,Translation:Vector3.Zero),
            (Yaw:0f,Translation:new Vector3(-4,0,0)),(Yaw:.45f,Translation:new Vector3(-1,0,1))})
        {
            using var result=scene.FilterAuthored(rays,Enumerable.Repeat(.5f,4*4*3).ToArray(),rayMeta,
                translation:motion.Translation,previousYaw:motion.Yaw);
            var distances=new float[64];
            for(int y=0;y<8;y++)for(int x=0;x<8;x++)
            {
                var direction=Vector3.Normalize(new Vector3((x+.5f)/4-1,(y+.5f)/4-1,-1));
                float along=Vector3.Dot(direction,motion.Translation);
                distances[y*8+x]=MathF.Sqrt(100-motion.Translation.LengthSquared()+along*along)-along;
            }
            using var expectedDistance=RHICommandContext.Current.CreateDynamicTexture2D(new()
            {
                Dimension=TextureDimension.Two,Format=TextureFormat.R32f,Width=8,Height=8,MipLevels=1,
                Storage=ResourceStorageMode.Immutable,Filter=TextureFilterMode.Nearest
            }).RequireResource();
            expectedDistance.UploadDataImmediate(distances);

            var mapped=ImageAssertions.Predicates.Predicate(result.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance,result.Metadata,result.Cloud,expectedDistance);
            mapped.AssertEqual();Assert.True(mapped.MatchingTexels>0,
                $"No corresponding cloud survived yaw {motion.Yaw}, translation {motion.Translation}.");
            ImageAssertions.Predicates.Predicate(result.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_2,result.Metadata,result.Bins,result.Age).AssertEqual();
            uint wrongClass=ImageAssertions.Predicates.Predicate(result.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_3,result.Metadata).MatchingTexels;
            uint wrongDistance=ImageAssertions.Predicates.Predicate(result.Diagnostics!, CloudPredicates.CloudStoredVolumeViewTests_TemporalMotionCarriesMappedClassificationAndCurrentFrameDistance_4,result.Metadata).MatchingTexels;
            Assert.True(wrongClass>0&&wrongDistance>0,"Both unrelated empty and unrelated distant same-screen support must be corrected.");
            output.WriteLine($"Motion yaw={motion.Yaw}, translation={motion.Translation}: mapped wrong class={wrongClass}, wrong distance={wrongDistance}.");
        }
    }
    /// <summary>Records a bounded authored view workload without a speed or approximation threshold.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void RecordStoredViewCost()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs,3440,1377);
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
            output.WriteLine($"Stored view 3440x1377 sigma={sigma} history={reuse}: median {times[1]:F4} ms; samples {string.Join(", ",times.Select(t=>t.ToString("F4")))} ms. CPU submission median {cpuTimes[1]:F4} ms; samples {string.Join(", ",cpuTimes.Select(t=>t.ToString("F4")))} ms; allocated median {allocations[1]} bytes; samples {string.Join(", ",allocations)} bytes. GPU: {gpu}.");
            var receipt=scene.TraceReceipts!.ReadPixels();var history=scene.HistoryReceipts!.ReadPixels();
            output.WriteLine($"Trace receipts: fresh={receipt.Where((_,i)=>i%4==1).Count(v=>v>=1)}, visits={receipt.Where((_,i)=>i%4==3).Sum()}, admitted history={history.Where((_,i)=>i%4==3).Count(v=>v>1)}/{history.Length/4}; targets={scene.ViewTargetBytes}bytes.");
            var contribution=scene.HistoryDiagnostics!.ReadPixels();
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
                var raw=scene.TraceReceipts!.ReadPixels();var retained=scene.HistoryReceipts!.ReadPixels();
                output.WriteLine($"Changing {scenario}[{observation}]: history={scene.HistoryCompatible}, total={milliseconds:F4}ms, fresh={raw.Where((_,i)=>i%4==1).Count(v=>v>=1)}, visits={raw.Where((_,i)=>i%4==3).Sum()}, admitted={retained.Where((_,i)=>i%4==3).Count(v=>v>1)}/{retained.Length/4}; repeated same-frame observation forces fresh baseline.");
            }
        }
        output.WriteLine("Actual typed compact quarter-resolution trace plus full-resolution spatial reconstruction, 119 bytes per reduced pixel with diagnostic capture, compact raw trace and two histories (ceil edges additional), exact occupied-cell intervals, authored uniform extinction .0078125/empty0, direct .25/.75 and diffuse12, depths100, detail disabled, default shadows/diffuse/stop. One warmup and three stationary samples; changing-input diagnostics are single pairs. External preparation, clears and readbacks excluded; internal clears and trace/temporal/composition included; no live FPS or appearance qualification; contention uncontrolled.");
    }
    /// <summary>The complete native-generation-to-stored-view path owns and retires only its independent resources.</summary>
    [Fact]
    public void StoredSolutionInitializesRefinesDrawsAndRetires()
    {
        EnsureShaderTestAvailable();using var scene=new CloudStoredVolumeViewScene(ImageAssertions, Programs);
        var solution=new CloudVolumeSolution(Programs.Api,false);
        try
        {
            using var value=scene.DrawAndCapture(CloudVolumeViewSettings.Default with{Detail=false},solution:solution);
            Assert.True(solution.Storage.Ready);Assert.True(solution.Storage.StorageBytes>0);
            for(int image=0;image<value.Count;image++)ImageAssertions.Statistics.Inspect(value[image].Texture,0).AssertFiniteRange();
            var first=solution.Drawable!;
            scene.Draw(CloudVolumeViewSettings.Default with{Detail=false},solution:solution);
            Assert.True(solution.Drawable!.Revision>first.Revision);Assert.Same(first.Density,solution.Drawable.Density);
            scene.Draw(CloudVolumeViewSettings.Default with{Detail=false,LightingRaysPerUpdate=163840},solution:solution);
            Assert.Same(first.Density,solution.Drawable.Density); // Throughput changes retain compatible geometry.
            var retained=solution.Drawable;
            var low=CloudVolumeViewSettings.Default with{Detail=true,Quality=0,Stop=.02f,LightingRaysPerUpdate=20480};
            solution.Reconfigure(low);Assert.Same(retained,solution.Drawable);
            scene.Draw(low,solution:solution);Assert.NotSame(retained.MaterialIdentity,solution.Drawable!.MaterialIdentity);
            solution.Reconfigure(CloudVolumeViewSettings.Default with{Detail=false});
            scene.Draw(CloudVolumeViewSettings.Default with{Detail=false},solution:solution);
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
    /// <summary>Compares the entire transmission image after a fresh draw with its independently retained baseline.</summary>
    private void AssertTransmission(CloudStoredVolumeViewScene scene,CloudVolumeViewSettings settings,GpuImageSnapshot expected)
    {
        scene.Draw(settings);using var actual=scene.CaptureOutput();
        ImageAssertions.Comparisons.CompareHistory(actual[1].Texture,expected).AssertEqual();
    }
    /// <summary>Requires every attachment and every texel to retain the original uniform clear value.</summary>
    private void AssertUniformOutput(CloudStoredVolumeViewScene scene,CloudVolumeViewSettings settings,float value)
    {
        scene.Draw(settings,clear:value);using var images=scene.CaptureOutput();
        for(int image=0;image<images.Count;image++)ImageAssertions.Comparisons.CompareConstant(images[image].Texture,value).AssertEqual();
    }
    /// <summary>Checks every direction selected by the original one-ray control and proves it was actually selected.</summary>
    private GpuPredicateResult AssertObserved(CloudTemporalObservation actual,GpuTexture selected,GpuAssertionPredicate expectation)
    {
        var result=ImageAssertions.Predicates.Predicate(actual.Diagnostics!,expectation,
            selected,actual.Cloud,actual.Metadata);
        result.AssertEqual();Assert.True(result.MatchingTexels>0);return result;
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.GPU.Assertions.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-cloud", 0, 0);
        programs.Prepare("projection-cloud", 0, 0);
    }
    #endregion
}
