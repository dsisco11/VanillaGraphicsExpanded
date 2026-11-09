using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using System;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Integration;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Qualifies generated grading texels, cached ownership and actual quantized final presentation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ColorGradingLutTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{


    // One immutable executable belongs to this test; all source, exposure and grading inputs refresh per draw.
    private FinalDisplayShaderProgram? finalProgram;
    #region Public API
    /// <summary>Generated corners, axes and interior samples agree with a separate double-precision grading and film response.</summary>
    [Theory]
    [InlineData(1f, 1f, 1f, 0f)]
    [InlineData(1f, 1f, 1f, .41573521f)]
    [InlineData(1.3f, .8f, .7f, -.25f)]
    public void GeneratedTexelsMatchIndependentReference(float gamma, float extraGamma, float gain, float contrast)
    {
        // Arrange: Generate one LUT for the authored controls and choose corners, axes, and interior lattice nodes.
        EnsureShaderTestAvailable();
        using var owner = new ColorGradingLut();
        Vector4 controls = new(gamma, extraGamma, gain, contrast);
        // Act: Generate the production 3D transform for these controls.
        var texture = owner.Prepare(Programs.Api, controls);
        // Assert: Descriptor, alpha, and selected RGB texels match the independent double-precision reference.
        Assert.Equal(TextureDimension.Three, texture.TextureTarget);
        Assert.Equal(TextureFormat.Rgba16f, texture.InternalFormat);
        Assert.Equal(64, texture.Width);
        Assert.Equal(64, texture.Height);
        Assert.Equal(64, texture.Depth);
        ImageAssertions.Statistics.Inspect(texture, 0, 1).AssertFiniteRange();
        ImageAssertions.Comparisons.CompareConstant(texture, 1, channelMask: 8).AssertEqual();
        var expected = new Dictionary<int, Vector3>();
        int[] selected = Enumerable.Range(0, 16).Select(i => i * 4).Append(63).ToArray();
        foreach (int z in selected) foreach (int y in selected) foreach (int x in selected)
            expected.Add((z * 64 + y) * 64 + x, FilmicGradingReference.Display(new(FilmicGradingReference.Node(x),
                FilmicGradingReference.Node(y), FilmicGradingReference.Node(z)), controls));
        ImageAssertions.Comparisons.CompareSelected(texture, expected, .001f).AssertEqual();
        ImageAssertions.Comparisons.Compare(texture, [0f,0f,0f,1f], selection: new(new(0),0,0,0,1,1)).AssertEqual();
    }

    /// <summary>Unchanged controls retain their texture through resize; changes and reload replace only complete images.</summary>
    [Fact]
    public void CacheRetainsUnchangedControlsAndRejectsInvalidReplacement()
    {
        // Arrange: Prepare a neutral transform and snapshot its complete image before resize or invalid input.
        EnsureShaderTestAvailable();
        using var owner = new ColorGradingLut();
        Vector4 neutral = new(1, 1, 1, 0);
        var first = owner.Prepare(Programs.Api, neutral);
        // Act: Repeat preparation, resize, reject invalid controls, change controls, invalidate, and dispose.
        for (int i = 0; i < 8; i++) Assert.Same(first, owner.Prepare(Programs.Api, neutral));
        ScreenResourceManager.HandleScreenResize();
        // Assert: Unchanged controls retain the image, failed replacement preserves it, and accepted transitions advance ownership.
        Assert.Same(first, owner.Prepare(Programs.Api, neutral));
        Assert.Equal(1, owner.Generation);
        using var previous = new Assertions.GpuImageSnapshot(first);
        Assert.Throws<ArgumentException>(() => owner.Prepare(Programs.Api, new(1, 1, 1, float.NaN)));
        Assert.True(first.IsValid); ImageAssertions.Comparisons.CompareHistory(first, previous).AssertEqual(); Assert.Equal(1, owner.Generation);
        var changed = owner.Prepare(Programs.Api, new(1, 1, 1, .416f));
        Assert.NotSame(first, changed); Assert.Equal(2, owner.Generation);
        owner.Invalidate();
        var reloaded = owner.Prepare(Programs.Api, new(1, 1, 1, .416f));
        Assert.NotSame(changed, reloaded); Assert.Equal(3, owner.Generation);
        owner.Dispose();
        Assert.False(changed.IsValid); Assert.False(reloaded.IsValid);
        var nextWorld = owner.Prepare(Programs.Api, neutral);
        Assert.True(nextWorld.IsValid); Assert.NotSame(reloaded, nextWorld); Assert.Equal(4, owner.Generation);
    }

    /// <summary>A failed packaged compute reload leaves the previous complete transform intact and never labels it current.</summary>
    [Fact]
    [Trait("Scenario", "HeadlessRuntime")]
    public void FailedShaderReloadRetainsPreviousCompleteTexture()
    {
        // Arrange: Prepare and snapshot a complete LUT, then force the packaged shader input read to fail during reload.
        EnsureShaderTestAvailable();
        using var assets = new BinaryShaderApiFixture();
        using var platform = new EngineShaderPlatformScope();
        var lifetime = ClientRenderingLifetime.Create(assets.Api, GpuShaderProgramLibraries.Require(assets.Api));
        lifetime.Attach(() => { });
        using var owner = new ColorGradingLut();
        try
        {
            Vector4 neutral = new(1, 1, 1, 0);
            var first = owner.Prepare(assets.Api, neutral);
            using var previous = new Assertions.GpuImageSnapshot(first);
            ClientRenderingLifetime.BeginNativeReload();
            owner.Invalidate();
            ClientRenderingLifetime.CompleteNativeReload();
            assets.BeforeRead = _ => throw new IOException("controlled grading reload failure");
            // Act: Demand the invalidated transform during a failing reload, then authorize a later explicit retry.
            Assert.Throws<InvalidOperationException>(() => owner.Prepare(assets.Api, neutral));
            // Assert: Failure retains the old complete image; retry publishes a distinct complete replacement.
            Assert.True(first.IsValid); ImageAssertions.Comparisons.CompareHistory(first, previous).AssertEqual(); Assert.Equal(1, owner.Generation);
            assets.BeforeRead = null;
            // Failed demand preparation is deliberately latched for this reload.
            // A subsequent explicit reload authorizes a new executable attempt.
            ClientRenderingLifetime.BeginNativeReload(); owner.Invalidate(); ClientRenderingLifetime.CompleteNativeReload();
            var replacement = owner.Prepare(assets.Api, neutral);
            Assert.NotSame(first, replacement); Assert.Equal(2, owner.Generation);
            ImageAssertions.Comparisons.CompareHistory(replacement, previous).AssertEqual();
        }
        finally { owner.Dispose(); lifetime.Shutdown(); }
    }

    /// <summary>Actual final output preserves the LUT contract across dark, middle, daylight and colored lamp inputs.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.41573521f)]
    public void FinalQuantizationMatchesIndependentLookup(float contrast)
    {
        // Arrange: Grade dark, midrange, daylight, colored, and extreme radiance at several exposure values.
        EnsureShaderTestAvailable();
        Vector4 controls = new(1, 1, 1, contrast);
        using var owner = new ColorGradingLut();
        var lookup = owner.Prepare(Programs.Api, controls);
        Vector3[] colors = [Vector3.Zero, new(.00004f, .00003f, .00006f), new(.0006f),
            new(.01f), new(.18f), new(1), new(4, 2, .5f), new(100, 80, 60), new(65536)];
        // Act: Render each color set through the actual final shader with the same cached LUT.
        foreach (float ev in new[] { -2f, 0f, 4f })
        {
            using var actual=Final(colors,lookup,controls,ev);
            var expectedPixels=new float[colors.Length*4];var tolerances=new float[expectedPixels.Length];
            for(int pixel=0;pixel<colors.Length;pixel++)
            {
                Vector3 expected=FilmicGradingReference.Lookup(colors[pixel]*MathF.Pow(2,ev),controls);
                for(int channel=0;channel<3;channel++)
                {
                    expectedPixels[pixel*4+channel]=FilmicGradingReference.Quantize(expected[channel],pixel);
                    tolerances[pixel*4+channel]=pixel==0?0:1.001f/255;
                }
                expectedPixels[pixel*4+3]=1;
            }
            // Assert: Quantized output matches the independent filmic lookup and exposure does not rebuild the LUT.
            ImageAssertions.Comparisons.CompareFloating(actual.Texture,expectedPixels,tolerances).AssertEqual();
            Assert.Equal(1, owner.Generation); // Exposure is sampled before the lookup; it does not rebuild its transform.
        }
    }

    /// <summary>A logarithmic neutral ramp remains monotonic and the cached transform stays close to direct film grading.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.41573521f)]
    public void NeutralGradientHasBoundedInterpolationError(float contrast)
    {
        // Arrange: Create a 65-step logarithmic neutral ramp spanning the authored contrast range.
        EnsureShaderTestAvailable();
        using var owner = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1, contrast);
        Vector3[] colors = Enumerable.Range(0, 65).Select(i => new Vector3(MathF.Pow(2, -16 + i * .375f))).ToArray();
        // Act: Grade the ramp through the generated LUT and final output path.
        using var actual=Final(colors,owner.Prepare(Programs.Api,controls),controls,0);
        var expected=new float[colors.Length];var tolerances=new float[expected.Length];
        for(int pixel=0;pixel<colors.Length;pixel++)
        {
            float reference=FilmicGradingReference.Display(colors[pixel],controls).X;
            expected[pixel]=FilmicGradingReference.Quantize(reference,pixel);
            tolerances[pixel]=6.001f/255;
        }
        // Assert: The ramp stays within independent interpolation and chroma tolerances and remains monotonic.
        ImageAssertions.Comparisons.CompareComponentFloating(actual.Texture,expected,tolerances,1).AssertEqual();
        ImageAssertions.Predicates.Predicate(actual.Texture, MaterialPredicates.ColorGradingLutTests_NeutralGradientHasBoundedInterpolationError,parameters:new(1.001f/255,0,0,0)).AssertEqual();
        output.WriteLine($"Neutral ramp remains monotonic within 6.001/255 reference and 1.001/255 chroma tolerances at contrast {contrast:R}.");
    }
    /// <summary>The actual generated galaxy survives saved positive contrast after filmic LUT sampling and RGBA8 output.</summary>
    [Fact]
    public void GeneratedGalaxySurvivesPositiveContrastInFinalOutput()
    {
        // Arrange: Generate the deterministic galaxy, capture its brightest physical sample, and prepare saved positive contrast.
        EnsureShaderTestAvailable();
        var source = CelestialSource.Create(new(3, 64, 4096, 7));
        using var generation = new CelestialGalaxyGeneration(Programs.Create<CelestialGenerationProgram>(), source);
        generation.Submit();
        CelestialResourceSet? generated = null;
        var clock = Stopwatch.StartNew();
        while (generated is null && clock.Elapsed < TimeSpan.FromSeconds(20)) { generated = generation.TryComplete(); if (generated is null) Thread.Sleep(1); }
        // Act: Render generated radiance through both a one-pixel final probe and the actual fullscreen galaxy view.
        Assert.NotNull(generated);
        using var resources = generated;
        var projection = ImageAssertions.Projections;
        using var decoded=projection.Apply(resources!.Galaxy, MaterialProjections.ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput,new(source.StorageScale,0,0,0));
        Vector3 brightest=ImageAssertions.Selection.Brightest(decoded).Color;
        using var owner = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1, .41573521f);
        var lookup = owner.Prepare(Programs.Api, controls);
        using var final=Final([brightest],lookup,controls,4);
        Assert.True(ImageAssertions.Statistics.Inspect(final.Texture,channelMask:7).Maximum>=1f/255);
        Vector3 reference = FilmicGradingReference.Lookup(brightest * 16, controls);
        output.WriteLine($"Generated peak galaxy nits {brightest}; independent encoded LUT {reference}; final RGBA8 maximum {ImageAssertions.Statistics.Inspect(final.Texture,channelMask:7).Maximum:R}.");
        using var view=DrawGalaxyView(resources!);
        Vector3 visible=ImageAssertions.Selection.Brightest(view.Texture).Color;
        using var withAlpha=projection.Apply(view.Texture, MaterialProjections.ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput_2);
        using var captured=new GpuImageSnapshot(withAlpha);using var scene=captured.ToPlane();
        using var attenuated=DrawFinal(scene,lookup,controls,4);
        uint nonblack=ImageAssertions.Predicates.Predicate(attenuated.Texture, MaterialPredicates.ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput_3).MatchingTexels;
        output.WriteLine($"Actual fullscreen galaxy draw, clear atmosphere, four orientations: peak nits {visible}; final RGBA8 maximum {ImageAssertions.Statistics.Inspect(attenuated.Texture,channelMask:7).Maximum:R}, nonblack pixels {nonblack}/{scene.Width*scene.Height}.");
        using var neutralOwner = new ColorGradingLut();
        var neutralLookup = neutralOwner.Prepare(Programs.Api,new(1,1,1,0));
        foreach (float ev in new[] {2f,4f}) foreach (bool savedContrast in new[] {false,true})
        {
            var settings = new Vector4(1,1,1,savedContrast?.41573521f:0);
            using var image=DrawFinal(scene,savedContrast?lookup:neutralLookup,settings,ev);
            uint count=ImageAssertions.Predicates.Predicate(image.Texture, MaterialPredicates.ColorGradingLutTests_GeneratedGalaxySurvivesPositiveContrastInFinalOutput_3).MatchingTexels;
            output.WriteLine($"Actual galaxy-only final EV {ev}, contrast {settings.W:R}: maximum {ImageAssertions.Statistics.Inspect(image.Texture,channelMask:7).Maximum*255:R}/255; nonblack {count}/{scene.Width*scene.Height}.");
        }
        // Assert: The actual view yields visible final pixels and a calibrated catalog star remains brighter than the probe.
        Assert.True(nonblack > 0);
        // Use the calibrated catalog's real flux at a controlled pixel solid
        // angle. This is a presentation check, not a new billboard footprint test.
        var star = source.Stars[0];
        Vector3 starRadiance = star.Color * star.FluxLux / .0001f;
        using var starFinal=Final([starRadiance],lookup,controls,4);
        Assert.True(ImageAssertions.Statistics.Inspect(starFinal.Texture,channelMask:7).Maximum>ImageAssertions.Statistics.Inspect(final.Texture,channelMask:7).Maximum);
        output.WriteLine($"Calibrated catalog star at 0.0001sr: {starRadiance}; final RGBA8 maximum {ImageAssertions.Statistics.Inspect(starFinal.Texture,channelMask:7).Maximum:R}.");
    }

    #endregion

    #region Protected API
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("predicate-material", 0, 0);
        programs.Prepare("predicate-observation", 0, 0);
        programs.Prepare("projection-material", 0, 0);
        programs.Prepare("projection-observation", 0, 0);
    }
    #endregion

    #region Private
/// <summary>Draws actual generated source through the production fullscreen program and clear-air external transmission.</summary>
    private GpuImageSnapshot DrawGalaxyView(CelestialResourceSet resources)
    {
        const int size = 64;
        var shader = Programs.Create<CelestialGalaxyProgram>();
        float p = 1 / MathF.Tan(1);
        using var camera = TestFrameCamera.CreateFromProjection(new Matrix4x4(p,0,0,0,0,p,0,0,0,0,-1,-1,0,0,-.2f,0), size, size);
        shader.Frame = camera; shader.GalaxyMap = resources.Galaxy;
        using var transmission = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = 512, Height = 1, Format = TextureFormat.Rgba32f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = null }, [new RHITextureInitialData(new(new(), 0, 0, 0, 512, 1), new RHIPixelEncoding(TextureFormatHelper.GetPixelFormat(TextureFormat.Rgba32f), PixelComponentType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((AtmosphereExternalTransmission.Build(.001f, 1, TestContext.Current.CancellationToken).ToArray()).AsSpan()).ToArray())]).RequireResource();
        shader.ExternalTransmission = transmission;
        using var color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var glow = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, size), Height = System.Math.Max(1, size), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var target = DynamicGpuFramebuffer.CreateMRT([color,glow])!;
        using var geometry = new ArrayGraphicsGeometry(CelestialDrawPass.FullscreenLayout,PrimitiveTopology.Triangles,new Dictionary<int,GpuVbo>(),3);
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(AttachmentIndex:1, Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        var metadata = RHICommandContext.Current.DescribeRenderTargets(pass);
        using var pipeline = RHICommandContext.Current.CreateGraphicsPipeline(new(shader.Shader,CelestialDrawPass.FullscreenLayout,
            metadata,DynamicPipelineState.Viewport)).RequireResource();
        float peak=0;
        GpuImageSnapshot? brightestView=null;
        try
        {
        for (int orientation = 0; orientation < 4; orientation++)
        {
            shader.DrawInputs.Capture(Matrix4x4.CreateRotationY(orientation * MathF.PI / 2),
                1 / resources.Source.StorageScale,AtmosphereSkyMapping.Horizon(.001f));
            {
    var commands=RHICommandContext.Current;
    using var scope25=commands.EnterBoundary(new("Tests.GradingGalaxy",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope25.Run(()=>{
                commands.BeginRenderPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new(){Viewport=commands.PassViewport});
                commands.BindResources(shader.CaptureBindings()); commands.Draw(geometry.Inputs,new(0,3)); commands.EndRenderPass();
            });
}
            var candidate=ImageAssertions.Selection.Brightest(color);
            if(candidate.Luminance>peak)
            {
                peak=candidate.Luminance;
                brightestView?.Dispose();brightestView=new GpuImageSnapshot(color);
            }
            ImageAssertions.Comparisons.CompareConstant(glow, 0).AssertEqual();
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        return brightestView??throw new InvalidOperationException("The generated galaxy must produce a visible view.");
        }
        catch { brightestView?.Dispose();throw; }
    }

    /// <summary>Runs the production final fragment on tiny radiance inputs into an actual normalized RGBA8 image.</summary>
    private GpuImageSnapshot Final(Vector3[] colors, DynamicTexture3D lookup, Vector4 controls, float ev, int width = 0)
    {
        width = width == 0 ? colors.Length : width;
        int height = colors.Length / width;
        float[] input = colors.SelectMany(color => new[] { color.X, color.Y, color.Z, .37f }).ToArray();
        using var source = TestFramework.CreateTexture(width, height, TextureFormat.Rgba32f, input);
        var result=DrawFinal(source,lookup,controls,ev);
        try { ImageAssertions.Comparisons.Compare(source,input).AssertEqual();return result; }
        catch { result.Dispose();throw; }
    }

    /// <summary>Runs final grading against an existing GPU scene input while preserving every source texel.</summary>
    private GpuImageSnapshot DrawFinal(DynamicTexture2D source,DynamicTexture3D lookup,Vector4 controls,float ev)
    {
        int width=source.Width,height=source.Height;
        using var previous=new GpuImageSnapshot(source);
        using var zero = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var target = TestFramework.CreateTestGBuffer(width, height, TextureFormat.Rgba8);
        using var frame = TestFrameCamera.CreateIdentity(width, height);
        using var exposure = ExposureTestStorage.Create(ev);
        using var draw = new PostprocessDraw();
        var program = finalProgram??=Programs.Create<FinalDisplayShaderProgram>();
        program.FrameInputs = frame; program.SceneImage = source; program.BloomImage = zero; program.ShaftImage = zero;
        program.GradingLookup = lookup; program.ExposureBuffer = exposure;
        program.Capture(false, new(controls, Vector4.Zero, Vector4.Zero), new(ev, 0, 0, 0));
        var pipeline = draw.Prepare(program,target.PostprocessPass());
        {
    var commands=RHICommandContext.Current;
    using var scope26=commands.EnterBoundary(new("Tests.GradingFinal",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([pipeline],value=>value),operations:BoundaryOperations.RenderPasses));
    scope26.Run(()=>draw.Submit(commands, pipeline,target.PostprocessPass(), program.CaptureBindings()));
}
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        ImageAssertions.Comparisons.CompareHistory(source,previous).AssertEqual();
        return new GpuImageSnapshot(target[0]);
    }

    #endregion
}
