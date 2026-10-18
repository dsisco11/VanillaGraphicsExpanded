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

/// <summary>Executes reduced tracing, physical history and native OIT submission to verify algorithm and storage contracts.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class CloudVolumeReconstructionTests(HeadlessGLFixture fixture,ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    #region Transport and clipping
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
        GlDebug.ThrowIfErrors("Cloud reconstruction test");
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
    /// <summary>Actual reduced transport preserves every OIT/depth channel and primary-noise frame with certified view skipping or direct support fallback.</summary>
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
    public void PrimaryNoiseChangesAcrossFramesAndReplaysExactly()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        view.Capture(0,2,10,1,0,"detail");view.Render(0,false,includeReference:true);var first=view.ReadTrace();var reference=view.ReadReference();
        view.Capture(4,2,10,1,0,"detail");view.Render(4,false,includeReference:true);var next=view.ReadTrace();
        Assert.Contains(Enumerable.Range(0,first[1].Length),i=>Math.Abs(first[1][i]-next[1][i])>1e-6f);
        var nextReference=view.ReadReference();
        for(int layer=0;layer<6;layer++)Assert.Equal(reference[layer],nextReference[layer]);
        view.Capture(0,2,10,1,0,"detail");view.Render(0,false);var replay=view.ReadTrace();
        for(int layer=0;layer<6;layer++)Assert.Equal(first[layer],replay[layer]);
        uint wrap=8;
        view.Capture(wrap,2,10,1,0,"detail");view.Render(wrap,false);var cycled=view.ReadTrace();
        for(int layer=0;layer<6;layer++)Assert.Equal(first[layer],cycled[layer]);
        Assert.All(next.SelectMany(layer=>layer),value=>Assert.True(float.IsFinite(value)&&value>=0));
    }
    /// <summary>Homogeneous extinction remains unchanged when jitter advances at a matching pixel phase.</summary>
    [Fact]
    public void PrimaryNoisePreservesHomogeneousTransmissionAtMatchingPixelPhases()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(8,8);
        // The complete finite span must actually be homogeneous: the shorter static height
        // fixture is discontinuous near grazing rays and can legitimately change sample occupancy.
        view.Capture(0,2,10,1,0,"homogeneous");view.Render(0,false);var first=view.ReadTrace();
        view.Capture(4,2,10,1,0,"homogeneous");view.Render(4,false);var next=view.ReadTrace();
        for(int layer=0;layer<6;layer++)Assert.Equal(first[layer],next[layer]);
    }
    /// <summary>Varied scene and history transitions preserve finite payloads and bounded attenuation.</summary>
    [Theory]
    [InlineData("static")][InlineData("thin")][InlineData("thin-height")][InlineData("lit")][InlineData("terrain")][InlineData("liquid")]
    [InlineData("camera")][InlineData("rotation")][InlineData("motion")][InlineData("weather")][InlineData("lighting")][InlineData("mixed")][InlineData("odd")]
    [InlineData("particle-before")][InlineData("particle-within")][InlineData("particle-behind")]
    [InlineData("aerial")][InlineData("underwater")]
    public void SceneTransitionsKeepPayloadFiniteAndAttenuationBounded(string scene)
    {
        EnsureShaderTestAvailable();
        using var view=CreateScene(scene=="odd"?9:8,scene=="odd"?7:8);
        for(uint frame=0;frame<8;frame++)
        {
            float shift=scene=="motion"?(frame%4)*.2f:scene=="camera"?-(frame%4)*.2f:0;
            bool discontinuity=scene=="lighting"&&frame==4;
            float extinction=scene=="thin"?.015f:scene=="weather"&&frame>=4?.1f:2;
            float environment=scene=="lighting"&&frame>=4?40:10;
            float scale=frame>=4?.125f:1; // physical history must not retain the old frozen scene encoding
            view.Capture(frame,extinction,environment,scale,shift,scene);
            view.Render(frame,frame>0&&!discontinuity);
            var actual=view.ReadDefault();
            for(int layer=0;layer<6;layer++)
            {
                for(int i=0;i<actual[layer].Length;i++)
                {
                    float value=actual[layer][i];
                    Assert.True(float.IsFinite(value)&&value>=0);
                    if(layer is 0 or 1) Assert.InRange(value,0,1);
                }
            }
        }
        GlDebug.ThrowIfErrors("Cloud reconstruction test");
    }
    /// <summary>Opaque hierarchy coarse levels never alter precise cloud tracing, reference clipping or full-view reconstruction.</summary>
    [Theory]
    [InlineData("static")][InlineData("terrain")][InlineData("liquid")][InlineData("mixed")]
    public void OpaqueHierarchyAlwaysSamplesMipZero(string scene)
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(9,7,mipmappedOpaque:true);
        view.Capture(0,2,10,1,0,scene);view.SetOpaqueCoarseDepth(0);view.Render(0,false,includeReference:true);
        var reconstructed=view.ReadDefault();var reference=view.ReadReference();var history=view.ReadHistory();
        view.SetOpaqueCoarseDepth(1);view.Render(0,false,includeReference:true);
        var changed=view.ReadDefault();var changedReference=view.ReadReference();var changedHistory=view.ReadHistory();
        for(int layer=0;layer<6;layer++)
        {
            Assert.Equal(reconstructed[layer],changed[layer]);
            Assert.Equal(reference[layer],changedReference[layer]);
            Assert.Equal(history[layer],changedHistory[layer]);
        }
        Assert.Contains(changed[1],value=>value<1);
        if(scene is "terrain" or "mixed") Assert.True(changedReference[1].Max()>changedReference[1].Min());
        GlDebug.ThrowIfErrors("Cloud reconstruction test");
    }
    /// <summary>Foreground particles remain visible occluders even when corrected receiver depth contains only distant terrain.</summary>
    [Fact]
    public void ParticleVisibilityClipsTracingAndAuthoredResolve()
    {
        EnsureShaderTestAvailable();using var view=CreateScene(9,7);
        view.Capture(0,2,10,1,0,"static");view.Render(0,false,includeReference:true);
        var unoccluded=view.ReadReference();view.SetVisibilityForeground();
        var resolved=view.ResolveAuthoredHistory();
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)
            Assert.InRange(Math.Abs(resolved[1][y*9+x]-((x+y)%3==0?1f:.4f)),0,.00001);
        view.Render(0,false,includeReference:true);
        var reference=view.ReadReference();
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)
        {
            int index=y*9+x;
            // This camera starts inside the cloud: nearer visibility removes the rear interval,
            // while the front interval remains valid and must not be erased.
            if((x+y)%3==0) Assert.True(reference[1][index]>unoccluded[1][index]);
            else Assert.Equal(unoccluded[1][index],reference[1][index]);
        }
    }
    /// <summary>New opaque silhouettes cannot inherit a cloud behind a previous depth, even inside one reduced pixel.</summary>
    [Fact]
    public void NewlyExposedAndOccludedPixelsUseCurrentDepth()
    {
        EnsureShaderTestAvailable(); using var view=CreateScene(9,7);
        view.Capture(0,2,10,1,0,"static"); view.Render(0,false);
        view.Capture(1,2,10,1,0,"moving-edge"); view.Render(1,true);
        // Authored support at 50..100 is behind foreground depth 20; raw transport
        // can instead retain cloud in front of that depth when the camera is inside it.
        var actual=view.ResolveAuthoredHistory();
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)
            Assert.InRange(Math.Abs(actual[1][y*9+x]-((x+y)%3==0?1f:.4f)),0,.00001);
        view.Capture(2,2,10,1,0,"static"); view.Render(2,true);
        Assert.All(view.ReadDefault()[1],value=>Assert.True(value<1));
    }
    /// <summary>The complete reduced path produces finite physical payloads in both PBR modes.</summary>
    [Theory]
    [InlineData(false)][InlineData(true)]
    public void BothPbrModesProduceFinitePhysicalPayloads(bool lumOn)
    {
        EnsureShaderTestAvailable();
        bool? previous = PbrShaderLightingMode.GenerationLumOnEnabled;
        try
        {
            PbrShaderLightingMode.GenerationLumOnEnabled = lumOn;
            using var view = CreateScene(8, 8);
            view.Capture(0, 2, 10, .125f, 0, "aerial"); view.Render(0, false);
            view.Capture(1, 2, 10, .125f, 0, "aerial"); view.Render(1, true);
            var actual = view.ReadDefault();
            Assert.Contains(actual[1], value => value < 1);
            Assert.Contains(actual[3], value => value > 0);
            for (int layer = 0; layer < 6; layer++)
            for (int i = 0; i < actual[layer].Length; i++)
            {
                Assert.True(float.IsFinite(actual[layer][i])&&actual[layer][i]>=0);
                if(layer<2)Assert.InRange(actual[layer][i],0,1);
            }
        }
        finally { PbrShaderLightingMode.GenerationLumOnEnabled = previous; }
    }
    /// <summary>Reduced physical radiance is unaffected by exposure storage; only final accumulated RGB is scaled.</summary>
    [Fact]
    public void HistoryRemainsPhysicalAndEmptyMapCannotGhost()
    {
        EnsureShaderTestAvailable();using var unscaled=CreateScene(8,8);using var scaled=CreateScene(8,8);
        for(uint frame=0;frame<2;frame++)
        {
            // Identical samples and history admission isolate scene encoding from physical storage.
            unscaled.Capture(frame,2,10,1,0,"static");unscaled.Render(frame,frame>0);
            scaled.Capture(frame,2,10,frame==0?1:.125f,0,"static");scaled.Render(frame,frame>0);
            var expected=unscaled.ReadHistory();var actual=scaled.ReadHistory();
            for(int layer=0;layer<6;layer++)Assert.Equal(expected[layer],actual[layer]);
        }
        Assert.True(scaled.ReadHistory()[3].Max()>1);
        scaled.Capture(2,0,10,.125f,0,"static");scaled.Render(2,false);
        Assert.All(scaled.ReadDefault()[1],value=>Assert.Equal(1,value));
        Assert.All(scaled.ReadDefault()[3],value=>Assert.Equal(0,value));
    }
    #endregion
    #region History and storage
    /// <summary>Preserves exact physical-storage, final encoding and foreground rules independently of reconstruction quality comparisons.</summary>
    [Fact]
    public void PhysicalStorageEncodingAndForegroundRulesRemainExact()
    {
        EnsureShaderTestAvailable();using var unscaled=CreateScene(9,7);using var scaled=CreateScene(9,7);
        unscaled.Capture(0,2,10,1,0,"static");unscaled.Render(0,false);
        scaled.Capture(0,2,10,.125f,0,"static");scaled.Render(0,false);
        var a=unscaled.ReadTraceRecord();var b=scaled.ReadTraceRecord();
        for(int layer=0;layer<3;layer++)Assert.Equal(a[layer],b[layer]);
        var first=unscaled.ReadDefault();var second=scaled.ReadDefault();
        for(int layer=0;layer<6;layer++)for(int index=0;index<first[layer].Length;index++)
        {
            bool encodedRgb=layer>=3&&index%4!=3;
            Assert.InRange(Math.Abs(second[layer][index]-first[layer][index]*(encodedRgb?.125f:1)),0,.0001);
        }
        unscaled.SetVisibilityForeground();var authored=unscaled.ResolveAuthoredHistory();
        for(int y=0;y<7;y++)for(int x=0;x<9;x++)Assert.InRange(Math.Abs(authored[1][y*9+x]-((x+y)%3==0?1f:.4f)),0,.00001);
        unscaled.Capture(1,0,10,.125f,0,"static");unscaled.Render(1,false);
        Assert.All(unscaled.ReadDefault()[1],value=>Assert.Equal(1,value));
        Assert.All(unscaled.ReadDefault()[3],value=>Assert.Equal(0,value));
    }

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
            Assert.Equal(9,storage.Array.Depth);
            Assert.Equal(3,storage.Blocks.Length);
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
            {
                var payload=CloudPayloadReadback.Read(storage.Blocks[block]);
                for(int layer=0;layer<3;layer++)Assert.All(payload[layer],value=>Assert.Equal(10*block+layer,value));
            }
            storage.Dispose();Assert.False(storage.Array.IsValid);
            Assert.All(storage.Blocks,block=>{Assert.True(block.Target.IsDisposed);Assert.False(block.View.IsValid);});
        }
        GlDebug.ThrowIfErrors("Cloud reconstruction test");
    }
    /// <summary>Repeated compatible static samples follow the specified bounded temporal-weight recurrence.</summary>
    [Fact]
    public void StaticHistoryFollowsBoundedWeightRecurrence()
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
    /// <summary>Creates independent owned executables, persistent reduced storage and a tiny view without native engine state.</summary>
    private CloudReconstructionScene CreateScene(int width,int height,bool mipmappedOpaque=false,int nativeMapWidth=4,bool independentStorage=false)=>new(width,height,Programs,mipmappedOpaque,nativeMapWidth,independentStorage);

    #endregion
}
