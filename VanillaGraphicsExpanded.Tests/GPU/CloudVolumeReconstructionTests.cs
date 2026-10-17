using System.Collections.Immutable;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Clouds;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.MathTools;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Executes reduced tracing, physical history and native OIT submission against the actual reference executable.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeReconstructionTests(HeadlessGLFixture fixture,ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    #region Reference comparisons
    /// <summary>Solar/lunar directions preserve self-shadowed illumination and every payload channel under combined view/light skipping.</summary>
    [Theory]
    [InlineData(0, false)]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, false)]
    [InlineData(2, true)]
    public void HierarchicalSecondaryLightingMatchesOriginal(int directions, bool detail)
    {
        EnsureShaderTestAvailable();
        using var hierarchy = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var view = CreateScene(9, 7, nativeMapWidth: 21);
        Vector3 sun = directions == 0 ? Vector3.UnitY : directions == 1 ? Vector3.UnitX : Vector3.Normalize(new(1, .2f, .35f));
        Vector3 moon = directions == 0 ? -Vector3.UnitY : directions == 1 ? Vector3.UnitZ : Vector3.Normalize(new(-.4f, .6f, -1));
        bool observedSolar = false, observedLunar = false;
        foreach (uint frame in new uint[] { 0, 3, 4 })
        {
            view.Capture(frame, .125f, 10, 1, 0, detail ? "detail-lit" : "lit");
            view.AuthorHierarchyColumns(foreground: true);
            view.SetDirectionalLights(sun, moon, 0);
            view.UseHierarchy(null); view.RenderTrace(frame); var ambient = view.ReadTraceRecord();
            foreach (int sources in new[] { 1, 2, 3 })
            {
                view.SetDirectionalLights(sun, moon, sources);
                view.UseHierarchy(null); view.RenderTrace(frame); var original = view.ReadTraceRecord();
                bool illuminated = Enumerable.Range(3, 3).Any(layer => original[layer].Where((value, index) =>
                    index % 4 != 3 && Math.Abs(value - ambient[layer][index]) > 1e-6f).Any());
                if (sources == 1) observedSolar |= illuminated;
                if (sources == 2) observedLunar |= illuminated;
                view.UseHierarchy(hierarchy); view.RenderTrace(frame); var accelerated = view.ReadTraceRecord();
                for (int layer = 0; layer < 7; layer++) Assert.Equal(original[layer], accelerated[layer]);
                view.UseHierarchy(hierarchy, mismatch: true); view.RenderTrace(frame); var fallback = view.ReadTraceRecord();
                for (int layer = 0; layer < 7; layer++) Assert.Equal(original[layer], fallback[layer]);
            }
        }
        Assert.True(observedSolar && observedLunar, "Both self-shadowed directional sources must affect the tested illumination.");
    }
    /// <summary>Actual reduced transport preserves every OIT/depth channel and PMJ frame with certified view skipping or DDA fallback.</summary>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void HierarchicalViewMatchesOriginalReducedTransport(bool detail, bool foreground)
    {
        EnsureShaderTestAvailable();
        using var hierarchy = new CloudHierarchy(() => CloudHierarchyComputeShader.Create(Programs.Api));
        using var view = CreateScene(9, 7, nativeMapWidth: 21);
        bool observedCloud = false;
        foreach (uint frame in new uint[] { 0, 1, 3, 4, 511 })
        {
            view.Capture(frame, .125f, 10, 1, 0, detail ? "detail-lit" : "static");
            view.AuthorHierarchyColumns(foreground);
            view.UseHierarchy(null);
            view.RenderTrace(frame); var original = view.ReadTraceRecord();
            observedCloud |= original[1].Any(value => value < .99999f);
            view.UseHierarchy(hierarchy);
            view.RenderTrace(frame); var accelerated = view.ReadTraceRecord();
            for (int layer = 0; layer < 7; layer++) Assert.Equal(original[layer], accelerated[layer]);
            view.UseHierarchy(hierarchy, mismatch: true);
            view.RenderTrace(frame); var fallback = view.ReadTraceRecord();
            for (int layer = 0; layer < 7; layer++) Assert.Equal(original[layer], fallback[layer]);
        }
        Assert.True(observedCloud, "The comparison must exercise non-neutral cloud transport.");
    }
    /// <summary>Resolve reconstructs supplied physical payloads independently of cloud map density and rejects foreground occlusion.</summary>
    [Fact]
    public void FullViewResolveUsesReducedPayloadWithoutDensityEvaluation()
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(9,7);
        view.Capture(0,0,10,.125f,0,"static");
        var reconstructed=view.ResolveAuthoredHistory();
        Assert.All(reconstructed[1],value=>Assert.InRange(Math.Abs(value-.4f),0,.00001));
        Assert.All(reconstructed[0],value=>Assert.InRange(Math.Abs(value-.6f),0,.00001));
        // The authored history contains physical RGB 8, encoded once with this view's one-eighth storage scale.
        for(int layer=3;layer<6;layer++)
        for(int i=0;i<reconstructed[layer].Length;i++)
            Assert.InRange(Math.Abs(reconstructed[layer][i]-(i%4==3?.6f:1f)),0,.00001);
        var patterned=view.ResolveAuthoredHistory(patterned:true);
        Assert.InRange(Math.Abs(patterned[1][1]-.12f),0,.00001);
        for(int channel=0;channel<3;channel++) Assert.InRange(Math.Abs(patterned[0][3+channel]-.18f),0,.00001);
        // Native Glow uses SrcAlpha for alpha as well as RGB, so a cleared target stores squared alpha.
        Assert.InRange(Math.Abs(patterned[2][7]-.18f*.18f),0,.00001);
        for(int layer=3;layer<6;layer++) for(int channel=0;channel<4;channel++)
            Assert.InRange(Math.Abs(patterned[layer][4+channel]-(channel==3?.18f:.3f)),0,.00001);
        view.Capture(1,0,10,.125f,0,"moving-edge");
        var clipped=view.ResolveAuthoredHistory();
        for(int y=0;y<7;y++) for(int x=0;x<9;x++)
            Assert.InRange(Math.Abs(clipped[1][y*9+x]-((x+y)%3==0?1f:.4f)),0,.00001);
    }
    /// <summary>Detailed density samples vary across matching pixel phases and replay deterministically.</summary>
    [Fact]
    public void PmjTraceChangesAcrossCyclesAndReplaysExactly()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"detail");view.Render(0,false);var first=view.ReadTrace();var reference=view.ReadReference();
        view.Capture(4,2,10,1,0,"detail");view.Render(4,false);var next=view.ReadTrace();
        Assert.Contains(Enumerable.Range(0,first[1].Length),i=>Math.Abs(first[1][i]-next[1][i])>1e-6f);
        for(int layer=0;layer<6;layer++)Assert.Equal(reference[layer],view.ReadReference()[layer]);
        view.Capture(0,2,10,1,0,"detail");view.Render(0,false);var replay=view.ReadTrace();
        for(int layer=0;layer<6;layer++)Assert.Equal(first[layer],replay[layer]);
        uint wrap=4u*CloudPmjJitterTexture.CycleLength;
        view.Capture(wrap,2,10,1,0,"detail");view.Render(wrap,false);var cycled=view.ReadTrace();
        for(int layer=0;layer<6;layer++)Assert.Equal(first[layer],cycled[layer]);
        Assert.All(next.SelectMany(layer=>layer),value=>Assert.True(float.IsFinite(value)&&value>=0));
    }
    /// <summary>Homogeneous extinction remains unchanged when jitter advances at a matching pixel phase.</summary>
    [Fact]
    public void PmjPreservesHomogeneousTransmissionAtMatchingPixelPhases()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"static");view.Render(0,false);var first=view.ReadTrace();
        view.Capture(4,2,10,1,0,"static");view.Render(4,false);var next=view.ReadTrace();
        for(int layer=0;layer<6;layer++)Assert.Equal(first[layer],next[layer]);
    }
    /// <summary>The second sampling dimension changes secondary lighting without moving view-density samples.</summary>
    [Fact]
    public void PmjLightDimensionIsIndependentOfViewExtinction()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"detail-lit");view.SetJitterDimensions(.4f,.1f);view.Render(0,false);var first=view.ReadTrace();
        view.SetJitterDimensions(.4f,.9f);view.Render(0,false);var next=view.ReadTrace();
        Assert.Equal(first[1],next[1]);
        Assert.Contains(Enumerable.Range(0,first[3].Length),i=>Math.Abs(first[3][i]-next[3][i])>1e-6f);
    }
    /// <summary>Small deterministic scenes bound temporal/spatial residuals independently of the common transport model.</summary>
    [Theory]
    [InlineData("static")][InlineData("thin")][InlineData("thin-height")][InlineData("lit")][InlineData("terrain")][InlineData("liquid")]
    [InlineData("camera")][InlineData("rotation")][InlineData("motion")][InlineData("weather")][InlineData("lighting")][InlineData("mixed")][InlineData("odd")]
    [InlineData("particle-before")][InlineData("particle-within")][InlineData("particle-behind")]
    [InlineData("aerial")][InlineData("underwater")]
    public void DefaultReconstructsAllBucketsAgainstReference(string scene)
    {
        EnsureShaderTestAvailable();
        using var view=CreateScene(scene=="odd"?9:8,scene=="odd"?7:8);
        float maxRadiance=0,maxAttenuation=0;
        for(uint frame=0;frame<8;frame++)
        {
            float shift=scene=="motion"?(frame%4)*.2f:scene=="camera"?-(frame%4)*.2f:0;
            bool discontinuity=scene=="lighting"&&frame==4;
            float extinction=scene=="thin"?.015f:scene=="weather"&&frame>=4?.1f:2;
            float environment=scene=="lighting"&&frame>=4?40:10;
            float scale=frame>=4?.125f:1; // physical history must not retain the old frozen scene encoding
            view.Capture(frame,extinction,environment,scale,shift,scene);
            view.Render(frame,frame>0&&!discontinuity);
            var actual=view.ReadDefault(); var reference=view.ReadReference();
            for(int layer=0;layer<6;layer++)
            {
                for(int i=0;i<actual[layer].Length;i++)
                {
                    float a=actual[layer][i],b=reference[layer][i];
                    Assert.True(float.IsFinite(a)&&a>=0);
                    float error=Math.Abs(a-b);
                    if(layer is 0 or 1) { maxAttenuation=Math.Max(maxAttenuation,error); Assert.InRange(a,0,1); }
                    else maxRadiance=Math.Max(maxRadiance,error/Math.Max(1,environment*scale));
                    AssertWithinSpatialReference(actual[layer],reference[layer],i,layer==0?3:layer==1?1:4,
                        scene=="odd"?9:8,scene=="odd"?7:8,layer<2?.021f:.045f*Math.Max(1,environment*scale));
                }
            }
        }
        output.WriteLine($"{scene}: max attenuation absolute={maxAttenuation:G7}; max payload error / max(1,environment*scale)={maxRadiance:G7}");
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Measures stochastic quadrature against dense midpoint transport separately from deterministic reconstruction.</summary>
    [Fact]
    public void PmjEnsembleApproachesDenseReference()
    {
        const int cycleCount=CloudPmjJitterTexture.CycleLength;
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.SetReferenceSamples(8);view.Capture(0,2,10,1,0,"detail");view.RenderReference();var eight=view.ReadReference();
        view.SetReferenceSamples(16);view.Capture(0,2,10,1,0,"detail");view.RenderReference();var dense=view.ReadReference();
        var sums=new double[16];double single=0,prefix=0;float maxReferenceDelta=0;
        var payloadSums=Enumerable.Range(0,6).Select(layer=>new double[16*(layer==0?3:layer==1?1:4)]).ToArray();
        for(int y=0;y<4;y++)for(int x=0;x<4;x++)
            maxReferenceDelta=Math.Max(maxReferenceDelta,Math.Abs(eight[1][y*2*8+x*2]-dense[1][y*2*8+x*2]));
        for(uint cycle=0;cycle<cycleCount;cycle++)
        {
            uint frame=cycle*4;view.Capture(frame,2,10,1,0,"detail");view.RenderTrace(frame);var payload=view.ReadTrace();var sample=payload[1];
            for(int layer=0;layer<6;layer++)for(int i=0;i<payload[layer].Length;i++)payloadSums[layer][i]+=payload[layer][i];
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)
            {
                int i=y*4+x;double target=dense[1][y*2*8+x*2];sums[i]+=sample[i];
                if(cycle==0)single+=Math.Pow(sample[i]-target,2);
                if(cycle==7)prefix+=Math.Pow(sums[i]/8-target,2);
            }
        }
        double error=0,maxError=0;
        for(int y=0;y<4;y++)for(int x=0;x<4;x++)
        {
            double residual=Math.Abs(sums[y*4+x]/cycleCount-dense[1][y*2*8+x*2]);
            error+=residual*residual;maxError=Math.Max(maxError,residual);
        }
        output.WriteLine($"PMJ{cycleCount}: reference8vs16max={maxReferenceDelta:G9}; singleRMSE={Math.Sqrt(single/16):G9}; prefix8RMSE={Math.Sqrt(prefix/16):G9}; ensembleRMSE={Math.Sqrt(error/16):G9}; ensembleMax={maxError:G9}");
        for(int layer=0;layer<6;layer++)
        {
            if(layer==2)continue; // Trace Glow is straight alpha; native blended reference Glow stores squared alpha.
            int components=layer==0?3:layer==1?1:4;double delta=0,residual=0;
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)for(int c=0;c<components;c++)
            {
                int a=(y*4+x)*components+c,b=(y*2*8+x*2)*components+c;
                delta=Math.Max(delta,Math.Abs(eight[layer][b]-dense[layer][b]));
                residual=Math.Max(residual,Math.Abs(payloadSums[layer][a]/cycleCount-dense[layer][b]));
            }
            output.WriteLine($"Aligned layer{layer}: ref8vs16max={delta:G9}; ensemble128MaxResidual={residual:G9}");
        }
        // Keep the established attenuation budget; verify the dense reference consumes less than one quarter.
        Assert.InRange(maxReferenceDelta,0,.021f/4);
        Assert.InRange(maxError,0,.021);
        Assert.True(error<prefix&&prefix<single,"Progressive averaging must improve beyond both one sample and the eight-sample prefix.");
    }
    /// <summary>Measures the real bounded temporal filter separately from offline ensemble integration.</summary>
    [Fact]
    public void PmjTemporalNoiseReductionIsObservable()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"detail");view.RenderReference();var midpointBins=view.ReadReference()[0];
        view.SetReferenceSamples(16);view.Capture(0,2,10,1,0,"detail");view.RenderReference();var dense=view.ReadReference()[1];
        double raw=0,filtered=0,meanResidual=0,rawBins=0,filteredBins=0;int count=0,changed=0,footprintFailures=0;float maxFootprintResidual=0;
        for(uint frame=0;frame<64;frame++)
        {
            view.Capture(frame,2,10,1,0,"detail");view.Render(frame,frame>0);
            if(frame<16)continue;
            var tracePayload=view.ReadTrace();var historyPayload=view.ReadHistory();
            var current=tracePayload[1];var history=historyPayload[1];
            var resolved=view.ReadDefault()[1];
            for(int y=0;y<8;y++)for(int x=0;x<8;x++)
            {
                float low=dense[y*8+x],high=low;
                for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
                {
                    float target=dense[Math.Clamp(y+dy,0,7)*8+Math.Clamp(x+dx,0,7)];
                    low=Math.Min(low,target);high=Math.Max(high,target);
                }
                float residual=Math.Max(0,Math.Max(low-resolved[y*8+x],resolved[y*8+x]-high));
                maxFootprintResidual=Math.Max(maxFootprintResidual,residual);if(residual>.021)footprintFailures++;
            }
            int phaseX=(int)(frame&1),phaseY=(int)((frame>>1)&1);
            for(int y=0;y<4;y++)for(int x=0;x<4;x++)
            {
                int i=y*4+x;double target=dense[(y*2+phaseY)*8+x*2+phaseX];
                raw+=Math.Pow(current[i]-target,2);filtered+=Math.Pow(history[i]-target,2);meanResidual+=history[i]-target;
                for(int c=0;c<3;c++)
                {
                    double bucketTarget=midpointBins[((y*2+phaseY)*8+x*2+phaseX)*3+c];
                    rawBins+=Math.Pow(tracePayload[0][i*3+c]-bucketTarget,2);
                    filteredBins+=Math.Pow(historyPayload[0][i*3+c]-bucketTarget,2);
                }
                if(Math.Abs(current[i]-history[i])>1e-6)changed++;count++;
            }
        }
        output.WriteLine($"Temporal post-warmup: rawRMSE={Math.Sqrt(raw/count):G9}; historyRMSE={Math.Sqrt(filtered/count):G9}; meanResidual={meanResidual/count:G9}; blendedRecords={changed}/{count}");
        output.WriteLine($"Full-view post-warmup attenuation footprint: maxResidual={maxFootprintResidual:G9}; recordsOutsideExistingBudget={footprintFailures}/{48*64}");
        output.WriteLine($"Temporal bins relative to same-budget midpoint: rawRMSE={Math.Sqrt(rawBins/(count*3)):G9}; historyRMSE={Math.Sqrt(filteredBins/(count*3)):G9}");
        Assert.True(filtered<raw,"The actual bounded temporal filter must reduce this static scene's quadrature error.");
        Assert.True(filteredBins<rawBins,"Bounded history must reduce bucket error relative to the unchanged finite-budget model.");
        Assert.InRange(Math.Abs(meanResidual/count),0,.021);
        Assert.True(changed>0);
    }
    /// <summary>Bounds ensemble full-view payload compatibility with the existing finite-budget bucket model.</summary>
    [Fact]
    public void PmjFullViewEnsemblePreservesFiniteBudgetPayload()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"detail");view.RenderReference();var midpoint=view.ReadReference();
        view.SetReferenceSamples(16);view.Capture(0,2,10,1,0,"detail");view.RenderReference();var dense=view.ReadReference();
        var sums=midpoint.Select(values=>new double[values.Length]).ToArray();
        float worst=0;string location="";
        for(uint frame=0;frame<128;frame++)
        {
            view.Capture(frame,2,10,1,0,"detail");view.Render(frame,frame>0);var actual=view.ReadDefault();
            for(int layer=0;layer<6;layer++)for(int i=0;i<actual[layer].Length;i++)
            {
                Assert.True(float.IsFinite(actual[layer][i])&&actual[layer][i]>=0);
                if(layer<2)Assert.InRange(actual[layer][i],0,1);
                sums[layer][i]+=actual[layer][i];
                if(layer>1)continue;
                float residual=FootprintResidual(actual[layer][i],midpoint[layer],i,layer==0?3:1,8,8);
                if(residual>worst){worst=residual;location=$"frame{frame}/layer{layer}/element{i}: actual={actual[layer][i]:G9},midpoint={midpoint[layer][i]:G9},dense={dense[layer][i]:G9}";}
            }
        }
        output.WriteLine($"Full-view worst startup+steady attenuation footprint residual={worst:G9} at {location}");
        for(int layer=0;layer<6;layer++)
        {
            int components=layer==0?3:layer==1?1:4;double point=0;float denseFootprint=0,midpointFootprint=0;
            for(int i=0;i<sums[layer].Length;i++)
            {
                float mean=(float)(sums[layer][i]/128);
                point=Math.Max(point,Math.Abs(mean-dense[layer][i]));
                denseFootprint=Math.Max(denseFootprint,FootprintResidual(mean,dense[layer],i,components,8,8));
                midpointFootprint=Math.Max(midpointFootprint,FootprintResidual(mean,midpoint[layer],i,components,8,8));
            }
            output.WriteLine($"Full-view mean layer{layer}: densePointMax={point:G9}; denseFootprintMax={denseFootprint:G9}; midpointFootprintMax={midpointFootprint:G9}");
            // Changing sample count changes the native bucket partition itself. The four-sample
            // midpoint is the compatibility target; dense transmission is checked independently.
            Assert.InRange(midpointFootprint,0,layer<2?.021f:.045f*10);
        }
    }
    /// <summary>Opaque hierarchy coarse levels never alter precise cloud tracing, reference clipping or full-view reconstruction.</summary>
    [Theory]
    [InlineData("static")][InlineData("terrain")][InlineData("liquid")][InlineData("mixed")]
    public void OpaqueHierarchyAlwaysSamplesMipZero(string scene)
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(9,7,mipmappedOpaque:true);
        view.Capture(0,2,10,1,0,scene);view.SetOpaqueCoarseDepth(0);view.Render(0,false);
        var reconstructed=view.ReadDefault();var reference=view.ReadReference();var history=view.ReadHistory();
        view.SetOpaqueCoarseDepth(1);view.Render(0,false);
        var changed=view.ReadDefault();var changedReference=view.ReadReference();var changedHistory=view.ReadHistory();
        for(int layer=0;layer<6;layer++)
        {
            Assert.Equal(reconstructed[layer],changed[layer]);
            Assert.Equal(reference[layer],changedReference[layer]);
            Assert.Equal(history[layer],changedHistory[layer]);
        }
        Assert.Contains(changed[1],value=>value<1);
        if(scene is "terrain" or "mixed") Assert.True(changedReference[1].Max()>changedReference[1].Min());
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Foreground particles remain visible occluders even when corrected receiver depth contains only distant terrain.</summary>
    [Fact]
    public void ParticleVisibilityClipsTracingAndAuthoredResolve()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(9,7);
        view.Capture(0,2,10,1,0,"static");view.Render(0,false);
        var unoccluded=view.ReadReference();view.SetVisibilityForeground();
        var resolved=view.ResolveAuthoredHistory();
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)
            Assert.InRange(Math.Abs(resolved[1][y*9+x]-((x+y)%3==0?1f:.4f)),0,.00001);
        view.Render(0,false);
        var reference=view.ReadReference();var reconstructed=view.ReadDefault();
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)
        {
            int index=y*9+x;
            // This camera starts inside the cloud: nearer visibility removes the rear interval,
            // while the front interval remains valid and must not be erased.
            if((x+y)%3==0) Assert.True(reference[1][index]>unoccluded[1][index]);
            else Assert.Equal(unoccluded[1][index],reference[1][index]);
            AssertWithinSpatialReference(reconstructed[1],reference[1],index,1,9,7,.021f);
        }
    }
    /// <summary>New opaque silhouettes cannot inherit a cloud behind a previous depth, even inside one reduced pixel.</summary>
    [Fact]
    public void NewlyExposedAndOccludedPixelsUseCurrentDepth()
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(9,7);
        view.Capture(0,2,10,1,0,"static"); view.Render(0,false);
        view.Capture(1,2,10,1,0,"moving-edge"); view.Render(1,true);
        var actual=view.ReadDefault(); var reference=view.ReadReference();
        for(int i=0;i<actual[1].Length;i++)
        {
            if(reference[1][i]==1) Assert.Equal(1,actual[1][i]);
            AssertWithinSpatialReference(actual[1],reference[1],i,1,9,7,.021f);
        }
        view.Capture(2,2,10,1,0,"static"); view.Render(2,true);
        Assert.All(view.ReadDefault()[1],value=>Assert.True(value<1));
    }
    /// <summary>The complete reduced path remains independent of LumOn and uses the same physical publication.</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void BothPbrModesReconstructTheSamePhysicalResponse(bool lumOn)
    {
        EnsureShaderTestAvailable();
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = lumOn;
            using var view = CreateScene(8, 8);
            view.Capture(0, 2, 10, .125f, 0, "aerial"); view.Render(0, false);
            view.Capture(1, 2, 10, .125f, 0, "aerial"); view.Render(1, true);
            var actual = view.ReadDefault(); var reference = view.ReadReference();
            Assert.Contains(actual[1], value => value < 1);
            Assert.Contains(actual[3], value => value > 0);
            for (int layer = 0; layer < 6; layer++)
            for (int i = 0; i < actual[layer].Length; i++)
                AssertWithinSpatialReference(actual[layer],reference[layer],i,layer==0?3:layer==1?1:4,8,8,
                    layer<2?.021f:.045f*1.25f);
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }
    /// <summary>Reduced physical radiance is unaffected by exposure storage; only final accumulated RGB is scaled.</summary>
    [Fact]
    public void HistoryRemainsPhysicalAndEmptyMapCannotGhost()
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"static"); view.Render(0,false); var first=view.ReadHistory();
        view.Capture(1,2,10,.125f,0,"static"); view.Render(1,true); var second=view.ReadHistory();
        Assert.InRange(Math.Abs(first[3].Max()-second[3].Max()),0,.04);
        Assert.True(second[3].Max()>1); // stored physical accumulation, not the new one-eighth scene scale
        view.Capture(2,0,10,.125f,0,"static"); view.Render(2,false);
        Assert.All(view.ReadDefault()[1],value=>Assert.Equal(1,value));
        Assert.All(view.ReadDefault()[3],value=>Assert.Equal(0,value));
    }
    #endregion
    #region History and storage
    /// <summary>Size changes can replace complete owned sets, and stable frames retain the same pipeline and backing images.</summary>
    [Fact]
    public void ReducedTargetsReuseStableStorageAndRetireEveryAttachment()
    {
        EnsureShaderTestAvailable();var program=Programs.Create<CloudTraceProgram>();
        using var lifetime=new GraphicsPipelineLifetime();
        foreach(var extent in new[]{(9,7),(10,8),(5,3)})
        {
            var size=CloudVolumeSurface.Dimensions(extent.Item1,extent.Item2);
            var surface=new CloudVolumeSurface(size.Width,size.Height,"Tests.Cloud.Resize");
            var images=surface.Images.Select(image=>image.TextureId).ToArray();int framebuffer=surface.Target.FboId;
            var a=surface.Prepare(lifetime,program);var b=surface.Prepare(lifetime,program);
            Assert.Same(a.Pipeline,b.Pipeline);Assert.Equal(size.Width,surface.Target.Width);Assert.Equal(size.Height,surface.Target.Height);
            surface.Dispose();Assert.False(GL.IsFramebuffer(framebuffer));Assert.All(images,id=>Assert.False(GL.IsTexture(id)));
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Repeated compatible static samples converge geometrically rather than retaining an unbounded stale contribution.</summary>
    [Fact]
    public void StaticHistoryConvergesWithBoundedAge()
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(6,6);view.Capture(0,2,10,1,0,"static");
        var images=view.ProbeHistory("valid");
        for(int step=1;step<=8;step++)
        {
            double expected=.4+.02*Math.Pow(.75,step);
            Assert.InRange(Math.Abs(images[1][4]-expected),0,.00001);
            if(step<8)images=view.AdvanceProbeHistory();
        }
        output.WriteLine($"Static history residual after8 accepted samples: {Math.Abs(images[1][4]-.4):G7}; analytic residual={.02*Math.Pow(.75,8):G7}");
    }
    /// <summary>Accepted history uses identical bounded weights for every OIT channel; incompatible records reject atomically.</summary>
    [Theory]
    [InlineData("valid",.415f)][InlineData("invalid",.4f)][InlineData("depth",.4f)][InlineData("nan",.4f)][InlineData("motion",.4f)][InlineData("weather",.4f)]
    public void TemporalWeightsAndRejectionAreNumericallyObservable(string scenario,float expected)
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(6,6);
        view.Capture(0,2,10,1,0,"static"); var images=view.ProbeHistory(scenario);
        Assert.InRange(Math.Abs(images[1][4]-expected),0,.00001);
        Assert.InRange(Math.Abs(images[0][4*3]-expected),0,.00001);
        Assert.InRange(Math.Abs(images[2][4*4+3]-expected),0,.00001);
        for(int i=3;i<6;i++)
        {
            Assert.InRange(Math.Abs(images[i][4*4]-expected*10),0,.0001);
            Assert.InRange(Math.Abs(images[i][4*4+3]-expected),0,.00001);
        }
    }
    #endregion
    #endregion

    #region Private
    /// <summary>Measures excess outside the same bilinear neighborhood envelope used by deterministic reconstruction checks.</summary>
    private static float FootprintResidual(float actual,float[] reference,int index,int components,int width,int height)
    {
        int pixel=index/components,x=pixel%width,y=pixel/width,channel=index%components;
        float low=reference[index],high=low;
        for(int dy=-1;dy<=1;dy++)for(int dx=-1;dx<=1;dx++)
        {
            float sample=reference[(Math.Clamp(y+dy,0,height-1)*width+Math.Clamp(x+dx,0,width-1))*components+channel];
            low=Math.Min(low,sample);high=Math.Max(high,sample);
        }
        return Math.Max(0,Math.Max(low-actual,actual-high));
    }
    /// <summary>Bounds approximation by the one-pixel bilinear footprint, preserving the original residual tolerance outside its reference envelope.</summary>
    private static void AssertWithinSpatialReference(float[] actual,float[] reference,int index,int components,int width,int height,float tolerance)
    {
        int pixel=index/components,x=pixel%width,y=pixel/width,channel=index%components;
        float low=reference[index],high=low;
        for(int dy=-1;dy<=1;dy++) for(int dx=-1;dx<=1;dx++)
        {
            int sample=(Math.Clamp(y+dy,0,height-1)*width+Math.Clamp(x+dx,0,width-1))*components+channel;
            low=Math.Min(low,reference[sample]);high=Math.Max(high,reference[sample]);
        }
        Assert.True(actual[index]>=low-tolerance&&actual[index]<=high+tolerance,
            $"Pixel({x},{y}) channel{channel}: {actual[index]:G9} outside reference footprint [{low:G9},{high:G9}] +/- {tolerance:G9}");
    }
    /// <summary>Creates independent owned executables, persistent reduced storage and a tiny view without native engine state.</summary>
    private CloudScene CreateScene(int width,int height,bool mipmappedOpaque=false,int nativeMapWidth=4)=>new(width,height,Programs.Create<CloudVolumeProgram>(),Programs.Create<CloudTraceProgram>(),
        Programs.Create<CloudTemporalProgram>(),Programs.Create<CloudResolveProgram>(),mipmappedOpaque,nativeMapWidth);

    /// <summary>Owns synthetic native inputs and executes the same three pass structure as the runtime.</summary>
    private sealed class CloudScene : IDisposable
    {
        private readonly int width,height,lowWidth,lowHeight;
        private readonly CloudVolumeProgram reference;
        private readonly CloudTraceProgram trace;
        private readonly CloudTemporalProgram temporal;
        private readonly CloudResolveProgram resolve;
        private readonly List<IDisposable> owned=[];
        private readonly VgeFrameUniformBuffer camera;
        private readonly TestFrameLightingPublication storage;
        private readonly DynamicTexture2D map,opaque,visibility,liquid;
        private readonly DynamicTexture3D aerial,loss;
        private readonly CloudVolumeSurface traced,historyA,historyB,result,expected;
        private readonly GraphicsPipelineLifetime lifetime;
        private readonly ArrayGraphicsGeometry geometry;
        private readonly float[] projection=[4,0,0,0,0,4,0,0,0,0,-1.00020002f,-1,0,0,-.200020002f,0];
        private readonly float[] inverse=new float[16];
        private CloudVolumeSurface? last;
        private float shift,lastShift;
        private readonly float[] previousProjection=new float[16],currentProjection=new float[16];
        private float surfaceDepth;
        private bool detail,underwater;
        private int referenceSamples=4;
        private AtmosphereLighting? capturedLighting;
        private CloudPlacement capturedPlacement;

        #region Public API
        #region View preparation
        /// <summary>Allocates small deterministic targets and independently authored column/depth/atmospheric fixtures.</summary>
        internal CloudScene(int width,int height,CloudVolumeProgram reference,CloudTraceProgram trace,CloudTemporalProgram temporal,CloudResolveProgram resolve,bool mipmappedOpaque,int nativeMapWidth)
        {
            this.width=width;this.height=height;this.reference=reference;this.trace=trace;this.temporal=temporal;this.resolve=resolve;
            (lowWidth,lowHeight)=CloudVolumeSurface.Dimensions(width,height);
            camera=Own(new VgeFrameUniformBuffer()); storage=Own(new TestFrameLightingPublication());
            map=Own(DynamicTexture2D.Create(nativeMapWidth,nativeMapWidth,PixelInternalFormat.Rgba32f));
            int levels=1;for(int extent=Math.Max(width,height);extent>1;extent>>=1)levels++;
            opaque=Own(mipmappedOpaque?DynamicTexture2D.CreateMipmapped(width,height,PixelInternalFormat.R32f,levels):
                DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
            visibility=Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
            liquid=Own(DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f));
            aerial=Own(DynamicTexture3D.Create(1,2,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
            loss=Own(DynamicTexture3D.Create(1,1,1,PixelInternalFormat.Rgba32f,textureTarget:TextureTarget.Texture3D));
            aerial.UploadDataImmediate(new float[8],0,0,0,1,2,1,0);loss.UploadDataImmediate(new float[4],0,0,0,1,1,1,0);
            traced=Own(new CloudVolumeSurface(lowWidth,lowHeight,"Tests.Cloud.Trace"));
            historyA=Own(new CloudVolumeSurface(lowWidth,lowHeight,"Tests.Cloud.HistoryA"));historyB=Own(new CloudVolumeSurface(lowWidth,lowHeight,"Tests.Cloud.HistoryB"));
            result=Own(new CloudVolumeSurface(width,height,"Tests.Cloud.Default"));expected=Own(new CloudVolumeSurface(width,height,"Tests.Cloud.Reference"));
            lifetime=Own(new GraphicsPipelineLifetime());
            geometry=Own(new ArrayGraphicsGeometry(CloudVolumeRenderer.Layout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),proceduralVertices:3));
            Mat4f.Invert(inverse,projection);
            reference.Frame=camera;trace.Frame=camera;temporal.Frame=camera;resolve.Frame=camera;
            reference.Lighting=storage.Buffer;trace.Lighting=storage.Buffer;resolve.Lighting=storage.Buffer;
            trace.PmjJitter=Own(CloudPmjJitterTexture.Create());
        }
        /// <summary>Seeds adversarial coarse levels and enables mip selection so an accidental implicit LOD becomes observable.</summary>
        internal void SetOpaqueCoarseDepth(float depth)
        {
            using var binding=StateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,opaque.TextureId);
            GL.TexParameter(TextureTarget.Texture2D,TextureParameterName.TextureMinFilter,(int)TextureMinFilter.NearestMipmapNearest);
            for(int level=1;level<opaque.MipLevels;level++)
            {
                int mipWidth=Math.Max(1,width>>level),mipHeight=Math.Max(1,height>>level);
                GL.TexSubImage2D(TextureTarget.Texture2D,level,0,0,mipWidth,mipHeight,PixelFormat.Red,PixelType.Float,
                    Enumerable.Repeat(depth,mipWidth*mipHeight).ToArray());
            }
        }
        /// <summary>Authors surviving foreground particles independently of the corrected material-depth input.</summary>
        internal void SetVisibilityForeground()
        {
            float[] depths=new float[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)depths[y*width+x]=Depth((x+y)%3==0?20:1000);
            visibility.UploadDataImmediate(depths);
        }
        /// <summary>Captures one explicit cloud/camera state, including per-pixel opaque and liquid discontinuities.</summary>
        internal void Capture(uint frame,float extinction,float environment,float scale,float offset,string scene)
        {
            shift=offset; detail=scene is "detail" or "detail-lit"; underwater=scene=="underwater";
            float aerialLoss=underwater ? .99f : scene=="aerial" ? .25f : 0;
            float aerialScatter=underwater?100:scene=="aerial"?2:0;
            aerial.UploadDataImmediate([aerialScatter/16,aerialScatter/16,aerialScatter/16,0,0,0,0,0],0,0,0,1,2,1,0);
            loss.UploadDataImmediate([aerialLoss,aerialLoss,aerialLoss,0],0,0,0,1,1,1,0);
            surfaceDepth=scene=="particle-before"?20:scene=="particle-within"?60:scene=="particle-behind"?160:0;
            var view=Mat4f.Create();
            if(scene=="rotation")
            {
                float angle=(frame%4)*.003f,c=MathF.Cos(angle),s=MathF.Sin(angle);
                view=[c,0,-s,0,0,1,0,0,s,0,c,0,0,0,0,1];
            }
            var inverseView=new float[16];Mat4f.Invert(inverseView,view);Mat4f.Mul(currentProjection,projection,view);
            if(frame==0)currentProjection.CopyTo(previousProjection,0);
            camera.Capture(projection,view,inverse,inverseView,previousProjection,currentProjection,new(width,height),0,frame,Vector3.Zero,Vector3.Zero,0,new(.1f,1000));
            storage.Capture(scale,frame);
            float[] columns=new float[map.Width*map.Width*4];for(int i=0;i<map.Width*map.Width;i++){columns[i*4]=extinction;columns[i*4+2]=scene=="thin-height"?-.01f:-.5f;columns[i*4+3]=scene=="thin-height"?.01f:.5f;}
            map.UploadDataImmediate(columns);
            float[] depths=new float[width*height],liquids=new float[width*height];
            for(int y=0;y<height;y++)for(int x=0;x<width;x++)
            {
                bool foreground=(scene is "terrain" or "mixed")&&x<width/2 || scene=="moving-edge"&&(x+y)%3==0;
                depths[y*width+x]=Depth(foreground?20:1000);
                liquids[y*width+x]=Depth((scene is "liquid" or "mixed")&&y<height/2?60:1000);
            }
            opaque.UploadDataImmediate(depths);visibility.UploadDataImmediate(depths);liquid.UploadDataImmediate(liquids);
            var lighting=new AtmosphereLighting(Vector3.UnitY,scene is "lit" or "detail-lit"?new(30):Vector3.Zero,new(environment),Vector3.Zero,Vector3.Zero,ImmutableArray<float>.Empty)
                { Moon=-Vector3.UnitY,Lunar=scene is "lit" or "detail-lit"?new(3):Vector3.Zero,CalibrationGeneration=1,
                    LookupStorageScale=LightingStorage.PersistentScale,ReconstructionMaximumNits=aerialScatter };
            var placement=new CloudPlacement(0,0,0,0,0,0,offset,0,new(offset,0,-50),0);
            capturedLighting = lighting; capturedPlacement = placement;
            Configure(reference,reference.Parameters,placement,lighting);Configure(trace,trace.Parameters,placement,lighting);
            resolve.SceneDepth=opaque.TextureId;resolve.VisibilityDepth=visibility.TextureId;resolve.LiquidDepth=liquid.TextureId;
        }
        #endregion
        #region Submission and history probes
        /// <summary>Authors independent directional sources while preserving the captured medium, environment and quality selection.</summary>
        internal void SetDirectionalLights(Vector3 sun, Vector3 moon, int sources)
        {
            var original = capturedLighting ?? throw new InvalidOperationException("Capture lighting before changing directions.");
            var lighting = original with { Sun = sun, Moon = moon,
                Solar = (sources & 1) != 0 ? original.Solar : Vector3.Zero,
                Lunar = (sources & 2) != 0 ? original.Lunar : Vector3.Zero };
            Configure(reference, reference.Parameters, capturedPlacement, lighting);
            Configure(trace, trace.Parameters, capturedPlacement, lighting);
        }
        /// <summary>Authors sparse stepped clouds and optional opaque/visibility/liquid foreground clips independently of the hierarchy.</summary>
        internal void AuthorHierarchyColumns(bool foreground)
        {
            float[] columns = new float[map.Width * map.Width * 4];
            for (int z = 0; z < map.Width; z++)
            for (int x = 0; x < map.Width; x++)
            {
                if ((x + 2 * z) % 4 == 0) continue;
                int index = (z * map.Width + x) * 4;
                columns[index] = .125f;
                columns[index + 2] = (x + z) % 3 == 0 ? 1 : -.5f;
                columns[index + 3] = columns[index + 2] + 1;
            }
            map.UploadDataImmediate(columns);
            if (!foreground) return;
            SetVisibilityForeground();
            opaque.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 5 == 0 ? 20 : 1000)).ToArray());
            liquid.UploadDataImmediate(Enumerable.Range(0, width * height).Select(i => Depth(i % 7 == 0 ? 60 : 1000)).ToArray());
        }
        /// <summary>Binds only exact-source summaries; a deliberately mismatched certificate exercises explicit DDA fallback.</summary>
        internal void UseHierarchy(CloudHierarchy? hierarchy, bool mismatch = false)
        {
            if (hierarchy is null) { trace.SetHierarchy(null); return; }
            var input = new CloudMapInput(this, 1, 1, 1, 1, 1, 1,
                new(map.TextureId, 2, 3, 4, map.Width, 50, 5), default);
            Assert.True(hierarchy.TryBuild(input, () => true), hierarchy.Failure);
            bool available = hierarchy.TryGetCurrent(mismatch ? input with { Generation = 2 } : input, out var bounds);
            Assert.Equal(!mismatch, available);
            trace.SetHierarchy(available ? bounds : null);
        }
        /// <summary>Reads the complete traced OIT record including first/last cloud and foreground distances.</summary>
        internal float[][] ReadTraceRecord() => traced.Images.Select(image => image.ReadPixels()).ToArray();
        /// <summary>Selects the allowed dense midpoint budget independently of reduced tracing.</summary>
        internal void SetReferenceSamples(int count) { referenceSamples=count; }
        /// <summary>Executes deterministic full-resolution midpoint transport without changing history.</summary>
        internal void RenderReference() { Draw(reference,expected,true); }
        /// <summary>Executes one reduced sample phase without running temporal or spatial reconstruction.</summary>
        internal void RenderTrace(uint frame)
        {
            trace.Temporal.Capture(previousProjection,Vector3.Zero,lowWidth,lowHeight,frame,unchecked(frame-1),false);
            Draw(trace,traced,false);
        }
        /// <summary>Authors separate sampling dimensions to isolate view and secondary transport behavior.</summary>
        internal void SetJitterDimensions(float view,float light)
        {
            var values=Own(DynamicTexture2D.Create(1,1,PixelInternalFormat.Rg32f));
            values.UploadDataImmediate(new[]{view,light});trace.PmjJitter=values;
        }
        /// <summary>Supplies a known physical reduced payload while the independent density map remains empty.</summary>
        internal float[][] ResolveAuthoredHistory(bool patterned=false)
        {
            for(int layer=0;layer<7;layer++)
            {
                int components=layer==0?3:layer==1?1:4;
                float[] values=new float[lowWidth*lowHeight*components];
                for(int pixel=0;pixel<lowWidth*lowHeight;pixel++)
                for(int channel=0;channel<components;channel++)
                {
                    float factor=patterned?(float)(pixel%lowWidth+1)/lowWidth:1;
                    values[pixel*components+channel]=layer==6?(channel==0?50:channel==1?100:Depth(1000)):
                        factor*(layer==0?.6f:layer==1?.4f:layer==2?(channel==3?.6f:0):channel==3?.6f:8);
                }
                historyA.Images[layer].UploadDataImmediate(values);
            }
            resolve.Temporal.Capture(projection,Vector3.Zero,lowWidth,lowHeight,0,0,false);
            BindHistory(resolve,historyA); Draw(resolve,result,true); return ReadDefault();
        }
        /// <summary>Executes real reduced trace/history/resolve binaries and the direct reference, restoring inherited state after each pass.</summary>
        internal void Render(uint frame,bool valid)
        {
            var previous=(frame&1)==0?historyA:historyB;var next=(frame&1)==0?historyB:historyA;
            foreach(var p in new[]{trace.Temporal,temporal.Temporal,resolve.Temporal})
                p.Capture(previousProjection,new(lastShift-shift,0,0),lowWidth,lowHeight,frame,unchecked(frame-1),valid);
            BindHistory(temporal,previous);BindHistory(resolve,next);
            temporal.Current0=traced.Images[0].TextureId;temporal.Current1=traced.Images[1].TextureId;temporal.Current2=traced.Images[2].TextureId;
            temporal.Current3=traced.Images[3].TextureId;temporal.Current4=traced.Images[4].TextureId;temporal.Current5=traced.Images[5].TextureId;temporal.Current6=traced.Images[6].TextureId;
            Draw(trace,traced,false);Draw(temporal,next,false);Draw(resolve,result,true);Draw(reference,expected,true);last=next;lastShift=shift;currentProjection.CopyTo(previousProjection,0);
        }
        /// <summary>Authors distinguishable current/history payloads to prove interpolation and whole-record rejection on the GPU.</summary>
        internal float[][] ProbeHistory(string scenario)
        {
            for(int layer=0;layer<7;layer++)
            {
                int components=layer==0?3:layer==1?1:4;
                float[] a=new float[lowWidth*lowHeight*components],b=new float[a.Length];
                for(int pixel=0;pixel<lowWidth*lowHeight;pixel++)
                {
                    float current=pixel==4?.4f:pixel%2==0?.2f:.8f;
                    for(int channel=0;channel<components;channel++)
                    {
                        int index=pixel*components+channel;
                        a[index]=current*((layer>=3&&channel<3)?10:1);b[index]=.42f*((layer>=3&&channel<3)?10:1);
                        if(scenario=="weather"&&layer==1)b[index]=.8f;
                        if(layer==6)
                        {
                            a[index]=b[index]=channel==0?10:channel==1?20:1;
                            if(scenario=="depth"&&channel==2)b[index]=.9f;
                        }
                    }
                }
                if(scenario=="nan"&&layer==3)b[4*4]=float.NaN;
                traced.Images[layer].UploadDataImmediate(a);historyA.Images[layer].UploadDataImmediate(b);
            }
            temporal.Temporal.Capture(projection,scenario=="motion"?new(100,0,0):Vector3.Zero,lowWidth,lowHeight,0,0,scenario!="invalid");
            temporal.Current0=traced.Images[0].TextureId;temporal.Current1=traced.Images[1].TextureId;temporal.Current2=traced.Images[2].TextureId;
            temporal.Current3=traced.Images[3].TextureId;temporal.Current4=traced.Images[4].TextureId;temporal.Current5=traced.Images[5].TextureId;temporal.Current6=traced.Images[6].TextureId;
            BindHistory(temporal,historyA);Draw(temporal,historyB,false);last=historyB;return ReadHistory();
        }
        /// <summary>Advances a fixed trace through alternate history storage to expose the bounded temporal recurrence.</summary>
        internal float[][] AdvanceProbeHistory()
        {
            var next=ReferenceEquals(last,historyA)?historyB:historyA;
            BindHistory(temporal,last!);Draw(temporal,next,false);last=next;return ReadHistory();
        }
        #endregion
        #region Readback and disposal
        /// <summary>Reads test-only full-resolution reconstructed payloads.</summary>
        internal float[][] ReadDefault()=>result.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
        /// <summary>Reads test-only direct reference payloads for numerical comparison.</summary>
        internal float[][] ReadReference()=>expected.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
        /// <summary>Reads reduced transport independently of temporal accumulation.</summary>
        internal float[][] ReadTrace()=>traced.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
        /// <summary>Reads physical history to verify exposure-independent storage.</summary>
        internal float[][] ReadHistory()=>last!.Images.Take(6).Select(image=>image.ReadPixels()).ToArray();
        /// <summary>Releases fixture-owned resources in reverse dependency order.</summary>
        public void Dispose(){for(int i=owned.Count-1;i>=0;i--)owned[i].Dispose();}
        #endregion
        #endregion
        #region Private
        /// <summary>Records an owned fixture allocation without transferring any production resource.</summary>
        private T Own<T>(T resource) where T:IDisposable {owned.Add(resource);return resource;}
        /// <summary>Supplies identical physical transport inputs to all tracing consumers.</summary>
        private void Configure(ICloudVolumeBindings program,CloudVolumeParameters parameters,CloudPlacement placement,AtmosphereLighting lighting)
        {
            parameters.CaptureMap(map.Width,placement,1,CloudVolumeSettings.Default with {Detail=detail,ViewSamples=ReferenceEquals(program,reference)?referenceSamples:4});
            parameters.CaptureLighting(lighting,underwater?1:0,new(underwater ? .02f : 0,0,0,0),Vector3.Zero);
            program.Map=map.TextureId;program.SceneDepth=opaque.TextureId;program.VisibilityDepth=visibility.TextureId;program.LiquidDepth=liquid.TextureId;program.AerialRadiance=aerial;program.AerialAttenuation=loss;
        }
        /// <summary>Binds an alternate physical-history set, including unfiltered depth metadata.</summary>
        private static void BindHistory(ICloudHistoryBindings program,CloudVolumeSurface surface)
        {
            program.History0=surface.Images[0].TextureId;program.History1=surface.Images[1].TextureId;program.History2=surface.Images[2].TextureId;
            program.History3=surface.Images[3].TextureId;program.History4=surface.Images[4].TextureId;program.History5=surface.Images[5].TextureId;program.History6=surface.Images[6].TextureId;
        }
        /// <summary>Uses replacement writes internally and the real six-target native blend policy for final composition.</summary>
        private void Draw(GpuProgram program,CloudVolumeSurface surface,bool oit)
        {
            RenderPassColor[] outputs=oit?[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1,1,1,1)),
                new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
                new(4,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(5,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]:CloudVolumeSurface.Outputs;
            if(oit&&surfaceDepth>0)
            {
                // Independently seed an ordinary translucent surface's native OIT payload at
                // before/within/behind depths; the cloud must preserve existing bucket composition.
                float bin=MathF.Log(surfaceDepth/30+1),alpha=.25f;
                float[] weights=Enumerable.Range(0,3).Select(i=>i==2&&bin>2?1:MathF.Exp(-MathF.Pow((bin-i)/.832f,2))).ToArray();
                var colors=new List<RenderPassColor>{new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1-alpha*weights[0],1-alpha*weights[1],1-alpha*weights[2],1)),
                    new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(1-alpha,0,0,0)),new(2,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,.2f))};
                for(int i=0;i<3;i++)
                {
                    float a=alpha*MathF.Exp(-surfaceDepth/3000)*weights[i];
                    colors.Add(new(i+3,AttachmentLoad.Clear,Clear:ColorClearValue.Float(3*a,2*a,a,a)));
                }
                outputs=colors.ToArray();
            }
            var pass=new RenderPassDesc(surface.Target,outputs);using var metadata=new RenderPassTargets(pass);
            using var pipeline=new GraphicsPipeline(lifetime,new(program.GraphicsIdentity!,CloudVolumeRenderer.Layout,metadata.Signature,DynamicPipelineState.Viewport,
                blending:oit?LiquidPipelineStates.SurfaceBlending:null),program);
            int vao=GL.GetInteger(GetPName.VertexArrayBinding),fbo=GL.GetInteger(GetPName.DrawFramebufferBinding);
            Assert.True(GraphicsCommandContext.TryRun("Tests.Cloud.Reconstruction",[pipeline],true,commands=>{
                commands.BeginPass(pass);commands.SetPipeline(pipeline);commands.SetDynamicState(new(){Viewport=commands.PassViewport});commands.Draw(geometry,new(0,3));commands.EndPass();}));
            Assert.Equal(vao,GL.GetInteger(GetPName.VertexArrayBinding));Assert.Equal(fbo,GL.GetInteger(GetPName.DrawFramebufferBinding));
        }
        /// <summary>Independently authors perspective depth for the controlled projection.</summary>
        private static float Depth(float distance)=>(1.00020002f-.200020002f/distance)*.5f+.5f;
        #endregion
    }
    #endregion
}
