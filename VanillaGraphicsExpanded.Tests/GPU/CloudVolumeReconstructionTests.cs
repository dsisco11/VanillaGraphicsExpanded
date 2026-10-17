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
    /// <summary>Shared-array views preserve complete outputs against independently allocated storage using identical shader binaries.</summary>
    [Theory]
    [InlineData("static")][InlineData("detail-lit")][InlineData("mixed")][InlineData("moving-edge")][InlineData("cloud-edge")]
    public void ArrayPayloadMatchesIndependentBlockStorage(string scene)
    {
        EnsureShaderTestAvailable();using var shared=CreateScene(9,7);using var independent=CreateScene(9,7,independentStorage:true);
        for(uint frame=0;frame<7;frame++)
        {
            float shift=frame*.015f;
            shared.Capture(frame,.125f,10,1,shift,scene);independent.Capture(frame,.125f,10,1,shift,scene);
            shared.Render(frame,frame>0);independent.Render(frame,frame>0);
            var expected=independent.ReadDefault();var actual=shared.ReadDefault();
            for(int layer=0;layer<6;layer++)Assert.Equal(expected[layer],actual[layer]);
            expected=independent.ReadTraceRecord();actual=shared.ReadTraceRecord();
            for(int layer=0;layer<3;layer++)Assert.Equal(expected[layer],actual[layer]);
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
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
                bool illuminated = Enumerable.Range(0, 1).Any(layer => original[layer].Where((value, index) =>
                    index % 4 != 3 && Math.Abs(value - ambient[layer][index]) > 1e-6f).Any());
                if (sources == 1) observedSolar |= illuminated;
                if (sources == 2) observedLunar |= illuminated;
                view.UseHierarchy(hierarchy); view.RenderTrace(frame); var accelerated = view.ReadTraceRecord();
                for (int layer = 0; layer < 3; layer++) Assert.Equal(original[layer], accelerated[layer]);
                view.UseHierarchy(hierarchy, mismatch: true); view.RenderTrace(frame); var fallback = view.ReadTraceRecord();
                for (int layer = 0; layer < 3; layer++) Assert.Equal(original[layer], fallback[layer]);
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
            observedCloud |= original[0].Where((_, i) => i % 4 == 3).Any(value => value < .99999f);
            view.UseHierarchy(hierarchy);
            view.RenderTrace(frame); var accelerated = view.ReadTraceRecord();
            for (int layer = 0; layer < 3; layer++) Assert.Equal(original[layer], accelerated[layer]);
            view.UseHierarchy(hierarchy, mismatch: true);
            view.RenderTrace(frame); var fallback = view.ReadTraceRecord();
            for (int layer = 0; layer < 3; layer++) Assert.Equal(original[layer], fallback[layer]);
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
            Assert.InRange(Math.Abs(reconstructed[layer][i]-(i%4==3?.24f:.4f)),0,.00001);
        // The native Glow blend squares source alpha on a cleared target. Resolve must derive it from transmission.
        for(int pixel=0;pixel<reconstructed[1].Length;pixel++)
        {
            float opacity=1-reconstructed[1][pixel];
            for(int channel=0;channel<3;channel++) Assert.Equal(0,reconstructed[2][pixel*4+channel]);
            Assert.InRange(Math.Abs(reconstructed[2][pixel*4+3]-opacity*opacity),0,.00001);
        }
        var patterned=view.ResolveAuthoredHistory(patterned:true);
        Assert.InRange(Math.Abs(patterned[1][1]-.82f),0,.00001);
        for(int channel=0;channel<3;channel++) Assert.InRange(Math.Abs(patterned[0][3+channel]-.88f),0,.00001);
        // Native Glow uses SrcAlpha for alpha as well as RGB, so a cleared target stores squared alpha.
        Assert.InRange(Math.Abs(patterned[2][7]-.18f*.18f),0,.00001);
        for(int layer=3;layer<6;layer++) for(int channel=0;channel<4;channel++)
            Assert.InRange(Math.Abs(patterned[layer][4+channel]-(channel==3?.024f:.04f)),0,.00001);
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
        EnsureShaderTestAvailable();var program=Programs.Create<CloudTraceProgram>();var temporalProgram=Programs.Create<CloudTemporalProgram>();
        using var lifetime=new GraphicsPipelineLifetime();
        foreach(var extent in new[]{(9,7),(10,8),(5,3)})
        {
            var size=CloudVolumeStorage.Dimensions(extent.Item1,extent.Item2);
            var storage=new CloudVolumeStorage(size.Width,size.Height,"Tests.Cloud.Resize");
            int owner=storage.Array.TextureId;
            Assert.Equal(9,storage.Array.Depth);
            Assert.Equal(3,storage.Blocks.Length);
            var views=storage.Blocks.Select(block=>block.View.TextureId).ToArray();
            var framebuffers=storage.Blocks.Select(block=>block.Target.FboId).ToArray();
            for(int block=0;block<3;block++)
            {
                var surface=storage.Blocks[block];
                CloudPayloadReadback.AssertAttachmentLayout(surface);
                Assert.Equal(block*3,surface.View.BaseLayer);Assert.Equal(3,surface.View.LayerCount);
                var a=surface.Prepare(lifetime,program);var temporal=surface.Prepare(lifetime,temporalProgram);
                var b=surface.Prepare(lifetime,program);Assert.Same(temporal.Pipeline,surface.Prepare(lifetime,temporalProgram).Pipeline);
                Assert.Same(a.Pipeline,b.Pipeline);Assert.Equal(size.Width,surface.Target.Width);Assert.Equal(size.Height,surface.Target.Height);
                for(int layer=0;layer<3;layer++)
                {
                    float value=10*block+layer;
                    CloudPayloadReadback.Upload(surface,layer,Enumerable.Repeat(value,size.Width*size.Height*4).ToArray());
                }
            }
            // Distinct writes through the three disjoint views must not overwrite another block.
            for(int block=0;block<3;block++)
            for(int layer=0;layer<3;layer++)
                Assert.All(CloudPayloadReadback.Read(storage.Blocks[block])[layer],value=>Assert.Equal(10*block+layer,value));
            storage.Dispose();Assert.False(GL.IsTexture(owner));
            Assert.All(framebuffers,id=>Assert.False(GL.IsFramebuffer(id)));Assert.All(views,id=>Assert.False(GL.IsTexture(id)));
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
    [InlineData("valid",.415f)][InlineData("invalid",.4f)][InlineData("depth",.4f)][InlineData("nan",.4f)][InlineData("motion",.4f)][InlineData("weather",.4f)][InlineData("buckets",.4f)]
    public void TemporalWeightsAndRejectionAreNumericallyObservable(string scenario,float expected)
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(6,6);
        view.Capture(0,2,10,1,0,"static"); var images=view.ProbeHistory(scenario);
        Assert.InRange(Math.Abs(images[1][4]-expected),0,.00001);
        Assert.InRange(Math.Abs(images[0][4*3]-expected),0,.00001);
        Assert.InRange(Math.Abs(images[2][4*4+3]-(1-expected)),0,.00001);
        for(int i=3;i<6;i++)
        {
            Assert.InRange(Math.Abs(images[i][4*4]-expected*10*(1-expected)),0,.0001);
            Assert.InRange(Math.Abs(images[i][4*4+3]-(1-expected)*(1-expected)),0,.00001);
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
    private CloudReconstructionScene CreateScene(int width,int height,bool mipmappedOpaque=false,int nativeMapWidth=4,bool independentStorage=false)=>new(width,height,Programs.Create<CloudVolumeProgram>(),Programs.Create<CloudTraceProgram>(),
        Programs.Create<CloudTemporalProgram>(),Programs.Create<CloudResolveProgram>(),mipmappedOpaque,nativeMapWidth,independentStorage);

    #endregion
}
