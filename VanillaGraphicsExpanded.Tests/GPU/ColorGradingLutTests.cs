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
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies generated grading texels, cached ownership and actual quantized final presentation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ColorGradingLutTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Generated corners, axes and interior samples agree with a separate double-precision grading and film response.</summary>
    [Theory]
    [InlineData(1f, 1f, 1f, 0f)]
    [InlineData(1f, 1f, 1f, .41573521f)]
    [InlineData(1.3f, .8f, .7f, -.25f)]
    public void GeneratedTexelsMatchIndependentReference(float gamma, float extraGamma, float gain, float contrast)
    {
        EnsureShaderTestAvailable();
        using var owner = new ColorGradingLut();
        Vector4 controls = new(gamma, extraGamma, gain, contrast);
        var texture = owner.Prepare(Programs.Api, controls);
        Assert.Equal(TextureTarget.Texture3D, texture.TextureTarget);
        Assert.Equal(PixelInternalFormat.Rgba16f, texture.InternalFormat);
        Assert.Equal(64, texture.Width);
        Assert.Equal(64, texture.Height);
        Assert.Equal(64, texture.Depth);
        float[] actual = Read(texture);
        Assert.All(actual, component => Assert.True(float.IsFinite(component) && component >= 0 && component <= 1));
        for (int pixel = 3; pixel < actual.Length; pixel += 4) Assert.Equal(1, actual[pixel]);
        double maximum = 0;
        int[] selected = Enumerable.Range(0, 16).Select(i => i * 4).Append(63).ToArray();
        foreach (int z in selected) foreach (int y in selected) foreach (int x in selected)
        {
            Vector3 expected = FilmicGradingReference.Display(new(FilmicGradingReference.Node(x),
                FilmicGradingReference.Node(y), FilmicGradingReference.Node(z)), controls);
            int index = ((z * 64 + y) * 64 + x) * 4;
            for (int channel = 0; channel < 3; channel++)
            {
                maximum = Math.Max(maximum, Math.Abs(actual[index + channel] - expected[channel]));
                Assert.InRange(actual[index + channel], 0, 1);
                Assert.InRange(Math.Abs(actual[index + channel] - expected[channel]), 0, .001f);
            }
            Assert.Equal(1, actual[index + 3]);
        }
        Assert.Equal(new[] { 0f, 0f, 0f, 1f }, actual.Take(4));
        output.WriteLine($"Generated LUT maximum channel deviation against independent double reference: {maximum:R}.");
    }

    /// <summary>Unchanged controls retain their texture through resize; changes and reload replace only complete images.</summary>
    [Fact]
    public void CacheRetainsUnchangedControlsAndRejectsInvalidReplacement()
    {
        EnsureShaderTestAvailable();
        using var owner = new ColorGradingLut();
        Vector4 neutral = new(1, 1, 1, 0);
        var first = owner.Prepare(Programs.Api, neutral);
        for (int i = 0; i < 8; i++) Assert.Same(first, owner.Prepare(Programs.Api, neutral));
        ScreenResourceManager.HandleScreenResize();
        Assert.Same(first, owner.Prepare(Programs.Api, neutral));
        Assert.Equal(1, owner.Generation);
        float[] previous = Read(first);
        Assert.Throws<ArgumentException>(() => owner.Prepare(Programs.Api, new(1, 1, 1, float.NaN)));
        Assert.True(first.IsValid); Assert.Equal(previous, Read(first)); Assert.Equal(1, owner.Generation);
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
    public void FailedShaderReloadRetainsPreviousCompleteTexture()
    {
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
            float[] previous = Read(first);
            ClientRenderingLifetime.BeginNativeReload();
            owner.Invalidate();
            ClientRenderingLifetime.CompleteNativeReload();
            assets.BeforeRead = _ => throw new IOException("controlled grading reload failure");
            Assert.Throws<InvalidOperationException>(() => owner.Prepare(assets.Api, neutral));
            Assert.True(first.IsValid); Assert.Equal(previous, Read(first)); Assert.Equal(1, owner.Generation);
            assets.BeforeRead = null;
            // Failed demand preparation is deliberately latched for this reload.
            // A subsequent explicit reload authorizes a new executable attempt.
            ClientRenderingLifetime.BeginNativeReload(); owner.Invalidate(); ClientRenderingLifetime.CompleteNativeReload();
            var replacement = owner.Prepare(assets.Api, neutral);
            Assert.NotSame(first, replacement); Assert.Equal(2, owner.Generation);
            Assert.Equal(previous, Read(replacement));
        }
        finally { owner.Dispose(); lifetime.Shutdown(); }
    }

    /// <summary>Actual final output preserves the LUT contract across dark, middle, daylight and colored lamp inputs.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.41573521f)]
    public void FinalQuantizationMatchesIndependentLookup(float contrast)
    {
        EnsureShaderTestAvailable();
        Vector4 controls = new(1, 1, 1, contrast);
        using var owner = new ColorGradingLut();
        var lookup = owner.Prepare(Programs.Api, controls);
        Vector3[] colors = [Vector3.Zero, new(.00004f, .00003f, .00006f), new(.0006f),
            new(.01f), new(.18f), new(1), new(4, 2, .5f), new(100, 80, 60), new(65536)];
        foreach (float ev in new[] { -2f, 0f, 4f })
        {
            float[] actual = Final(colors, lookup, controls, ev);
            for (int pixel = 0; pixel < colors.Length; pixel++)
            {
                Vector3 expected = FilmicGradingReference.Lookup(colors[pixel] * MathF.Pow(2, ev), controls);
                for (int channel = 0; channel < 3; channel++)
                {
                    float code = FilmicGradingReference.Quantize(expected[channel], pixel);
                    Assert.InRange(Math.Abs(actual[pixel * 4 + channel] - code), 0, 1.001f / 255);
                }
                Assert.Equal(1, actual[pixel * 4 + 3]);
            }
            Assert.Equal(new[] { 0f, 0f, 0f, 1f }, actual.Take(4));
            Assert.Equal(1, owner.Generation); // Exposure is sampled before the lookup; it does not rebuild its transform.
        }
    }

    /// <summary>A logarithmic neutral ramp remains monotonic and the cached transform stays close to direct film grading.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(.41573521f)]
    public void NeutralGradientHasBoundedInterpolationError(float contrast)
    {
        EnsureShaderTestAvailable();
        using var owner = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1, contrast);
        Vector3[] colors = Enumerable.Range(0, 65).Select(i => new Vector3(MathF.Pow(2, -16 + i * .375f))).ToArray();
        float[] actual = Final(colors, owner.Prepare(Programs.Api, controls), controls, 0);
        float maximum = 0, chromaMaximum = 0;
        for (int pixel = 0; pixel < colors.Length; pixel++)
        {
            float reference = FilmicGradingReference.Display(colors[pixel], controls).X;
            maximum = Math.Max(maximum, Math.Abs(actual[pixel * 4] - FilmicGradingReference.Quantize(reference, pixel)));
            if (pixel > 0) Assert.True(actual[pixel * 4] + 1f / 255 >= actual[(pixel - 1) * 4]);
            for (int channel = 1; channel < 3; channel++) chromaMaximum = Math.Max(chromaMaximum, Math.Abs(actual[pixel * 4 + channel] - actual[pixel * 4]));
        }
        output.WriteLine($"Neutral ramp maximum final code error against direct grading: {maximum * 255:R}/255 at contrast {contrast:R}.");
        output.WriteLine($"Neutral ramp maximum interpolation chroma: {chromaMaximum * 255:R}/255.");
        Assert.InRange(chromaMaximum, 0, 1.001f / 255);
        Assert.InRange(maximum, 0, 6.001f / 255);
    }

    /// <summary>The actual generated galaxy survives saved positive contrast after filmic LUT sampling and RGBA8 output.</summary>
    [Fact]
    public void GeneratedGalaxySurvivesPositiveContrastInFinalOutput()
    {
        EnsureShaderTestAvailable();
        var source = CelestialSource.Create(new(3, 64, 4096, 7));
        using var generation = new CelestialGalaxyGeneration(Programs.Create<CelestialGenerationProgram>(), source);
        generation.Submit();
        CelestialResourceSet? generated = null;
        var clock = Stopwatch.StartNew();
        while (generated is null && clock.Elapsed < TimeSpan.FromSeconds(20)) { generated = generation.TryComplete(); if (generated is null) Thread.Sleep(1); }
        Assert.NotNull(generated);
        using var resources = generated;
        Vector3 brightest = Vector3.Zero;
        for (int face = 0; face < 6; face++)
        {
            float[] pixels = new float[64 * 64 * 3];
            using var binding = StateCache.Current.BindTextureScope(TextureTarget.TextureCubeMap, 0, resources!.Galaxy.TextureId);
            GL.GetTexImage(TextureTarget.TextureCubeMapPositiveX + face, 0, PixelFormat.Rgb, PixelType.Float, pixels);
            for (int pixel = 0; pixel < pixels.Length; pixel += 3)
            {
                Vector3 color = new(pixels[pixel], pixels[pixel + 1], pixels[pixel + 2]); color /= source.StorageScale;
                if (Vector3.Dot(color, new(.2126f, .7152f, .0722f)) > Vector3.Dot(brightest, new(.2126f, .7152f, .0722f))) brightest = color;
            }
        }
        using var owner = new ColorGradingLut();
        Vector4 controls = new(1, 1, 1, .41573521f);
        var lookup = owner.Prepare(Programs.Api, controls);
        float[] final = Final([brightest], lookup, controls, 4);
        Assert.Contains(final.Take(3), component => component >= 1f / 255);
        Vector3 reference = FilmicGradingReference.Lookup(brightest * 16, controls);
        output.WriteLine($"Generated peak galaxy nits {brightest}; independent encoded LUT {reference}; final RGBA8 {string.Join(", ", final)}.");
        Vector3[] view = DrawGalaxyView(resources!);
        Vector3 visible = view.MaxBy(color => Vector3.Dot(color,new(.2126f,.7152f,.0722f)));
        float[] attenuated = Final(view, lookup, controls, 4, 64);
        int nonblack = Enumerable.Range(0,view.Length).Count(i => attenuated[i*4]>0 || attenuated[i*4+1]>0 || attenuated[i*4+2]>0);
        output.WriteLine($"Actual fullscreen galaxy draw, clear atmosphere, four orientations: peak nits {visible}; final RGBA8 maximum {attenuated.Where((_,i)=>i%4!=3).Max():R}, nonblack pixels {nonblack}/{view.Length}.");
        using var neutralOwner = new ColorGradingLut();
        var neutralLookup = neutralOwner.Prepare(Programs.Api,new(1,1,1,0));
        foreach (float ev in new[] {2f,4f}) foreach (bool savedContrast in new[] {false,true})
        {
            var settings = new Vector4(1,1,1,savedContrast?.41573521f:0);
            float[] image = Final(view,savedContrast?lookup:neutralLookup,settings,ev,64);
            int count = Enumerable.Range(0,view.Length).Count(i => image[i*4]>0 || image[i*4+1]>0 || image[i*4+2]>0);
            output.WriteLine($"Actual galaxy-only final EV {ev}, contrast {settings.W:R}: maximum {image.Where((_,i)=>i%4!=3).Max()*255:R}/255; nonblack {count}/{view.Length}.");
        }
        Assert.True(nonblack > 0);
        // Use the calibrated catalog's real flux at a controlled pixel solid
        // angle. This is a presentation check, not a new billboard footprint test.
        var star = source.Stars[0];
        Vector3 starRadiance = star.Color * star.FluxLux / .0001f;
        float[] starFinal = Final([starRadiance], lookup, controls, 4);
        Assert.Contains(starFinal.Take(3), component => component > final.Take(3).Max());
        output.WriteLine($"Calibrated catalog star at 0.0001sr: {starRadiance}; final RGBA8 {string.Join(", ", starFinal)}.");
    }
    #endregion

    #region Private
    /// <summary>Draws actual generated source through the production fullscreen program and clear-air external transmission.</summary>
    private Vector3[] DrawGalaxyView(CelestialResourceSet resources)
    {
        const int size = 64;
        var shader = Programs.Create<CelestialGalaxyProgram>();
        float p = 1 / MathF.Tan(1);
        using var camera = TestFrameCamera.CreateFromProjection(new Matrix4x4(p,0,0,0,0,p,0,0,0,0,-1,-1,0,0,-.2f,0), size, size);
        shader.Frame = camera; shader.GalaxyMap = resources.Galaxy;
        using var transmission = DynamicTexture2D.CreateWithData(512, 1, PixelInternalFormat.Rgba32f,
            AtmosphereExternalTransmission.Build(.001f, 1, TestContext.Current.CancellationToken).ToArray());
        shader.ExternalTransmission = transmission;
        using var color = DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var glow = DynamicTexture2D.Create(size,size,PixelInternalFormat.Rgba32f);
        using var target = DynamicGpuFramebuffer.CreateMRT([color,glow])!;
        using var geometry = new ArrayGraphicsGeometry(CelestialRenderer.FullscreenLayout,PrimitiveType.Triangles,new Dictionary<int,GpuVbo>(),3);
        using var lifetime = new GraphicsPipelineLifetime();
        var pass = new RenderPassDesc(target,[new(0,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),
            new(1,AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        using var metadata = new RenderPassTargets(pass);
        using var pipeline = new GraphicsPipeline(lifetime,new(shader.GraphicsIdentity!,CelestialRenderer.FullscreenLayout,
            metadata.Signature,DynamicPipelineState.Viewport),shader);
        Vector3 brightest = Vector3.Zero;
        Vector3[] brightestView = [];
        for (int orientation = 0; orientation < 4; orientation++)
        {
            shader.DrawInputs.Capture(Matrix4x4.CreateRotationY(orientation * MathF.PI / 2),
                1 / resources.Source.StorageScale,AtmosphereSkyMapping.Horizon(.001f));
            Assert.True(GraphicsCommandContext.TryRun("Tests.GradingGalaxy",[pipeline],true,commands =>
            {
                commands.BeginPass(pass); commands.SetPipeline(pipeline); commands.SetDynamicState(new(){Viewport=commands.PassViewport});
                commands.Draw(geometry,new(0,3)); commands.EndPass();
            }));
            float[] data = color.ReadPixels();
            Vector3 candidate = Vector3.Zero;
            for (int pixel = 0; pixel < data.Length; pixel += 4)
            {
                Vector3 value = new(data[pixel],data[pixel+1],data[pixel+2]);
                if (Vector3.Dot(value,new(.2126f,.7152f,.0722f)) > Vector3.Dot(candidate,new(.2126f,.7152f,.0722f))) candidate = value;
            }
            if (Vector3.Dot(candidate,new(.2126f,.7152f,.0722f)) > Vector3.Dot(brightest,new(.2126f,.7152f,.0722f)))
            {
                brightest = candidate;
                brightestView = Enumerable.Range(0,size*size).Select(i => new Vector3(data[i*4],data[i*4+1],data[i*4+2])).ToArray();
            }
            Assert.All(glow.ReadPixels(), component => Assert.Equal(0,component));
        }
        Assert.Equal(ErrorCode.NoError,GL.GetError());
        return brightestView;
    }

    /// <summary>Reads complete half-float storage with a restored texture binding.</summary>
    private static float[] Read(DynamicTexture3D texture)
    {
        float[] result = new float[texture.Width * texture.Height * texture.Depth * 4];
        using var binding = StateCache.Current.BindTextureScope(TextureTarget.Texture3D, 0, texture.TextureId);
        GL.GetTexImage(TextureTarget.Texture3D, 0, PixelFormat.Rgba, PixelType.Float, result);
        return result;
    }

    /// <summary>Runs the production final fragment on tiny radiance inputs into an actual normalized RGBA8 image.</summary>
    private float[] Final(Vector3[] colors, DynamicTexture3D lookup, Vector4 controls, float ev, int width = 0)
    {
        width = width == 0 ? colors.Length : width;
        int height = colors.Length / width;
        float[] input = colors.SelectMany(color => new[] { color.X, color.Y, color.Z, .37f }).ToArray();
        using var source = TestFramework.CreateTexture(width, height, PixelInternalFormat.Rgba32f, input);
        using var zero = TestFramework.CreateTexture(1, 1, PixelInternalFormat.Rgba32f, [0f, 0f, 0f, 0f]);
        using var target = TestFramework.CreateTestGBuffer(width, height, PixelInternalFormat.Rgba8);
        using var frame = TestFrameCamera.CreateIdentity(width, height);
        using var exposure = ExposureTestStorage.Create(ev);
        using var draw = new PostprocessDraw();
        var program = Programs.Create<FinalDisplayShaderProgram>();
        program.FrameInputs = frame; program.SceneImage = source; program.BloomImage = zero; program.ShaftImage = zero;
        program.GradingLookup = lookup; program.ExposureBuffer = exposure;
        program.Capture(false, new(controls, Vector4.Zero, Vector4.Zero), new(ev, 0, 0, 0));
        var pipeline = draw.Prepare(program,target.PostprocessPass());
        Assert.True(GraphicsCommandContext.TryRun("Tests.GradingFinal", [pipeline], true, commands => draw.Submit(commands, pipeline,target.PostprocessPass())));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        Assert.Equal(input, source.ReadPixels());
        return target[0].ReadPixels();
    }
    #endregion
}
