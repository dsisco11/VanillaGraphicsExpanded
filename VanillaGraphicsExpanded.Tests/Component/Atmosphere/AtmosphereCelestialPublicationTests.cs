using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Component.Atmosphere;

/// <summary>Checks coherent dual transport and actual three-band texture storage at bounded lookup sizes.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class AtmosphereCelestialPublicationTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{


    #region Public API
    /// <summary>Pending work retains the admitted source, orientation and medium on both transport backends.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void CompleteSourceDiffusePublicationRetainsAdmissionAndCachesUnchangedInputs(bool forceCpu)
    {
        // Arrange
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        if (forceCpu) assets.BeforeRead = path => { if (path.EndsWith(".spv", StringComparison.Ordinal)) throw new IOException("Intentional CPU fallback"); };
        using var backend = new AtmosphereBackend(assets.Api);
        var firstSource = CelestialSource.Create(CelestialQuality.FromLevel(0));
        var nextSource = CelestialSource.Create(CelestialQuality.FromLevel(3));
        var sources = new AtmosphereCelestialInputs(-Vector3.UnitY,Vector3.UnitY,0);
        var firstRotation = Matrix4x4.CreateRotationX(.3f);
        var nextRotation = Matrix4x4.CreateRotationZ(.8f);
        // Act
        Assert.Null(backend.Update(sources,0,0,16,8,0,.1f,firstSource,firstRotation));
        var admitted = Complete(backend,sources,1,nextSource,nextRotation,.75f);
        // Assert
        Assert.Same(firstSource,admitted.CelestialSource);
        Assert.Equal(firstRotation,admitted.CelestialRotation);
        Assert.Equal(0,admitted.CloudCoverage);
        Assert.Equal(.001f,admitted.Altitude);
        var changed = Complete(backend,sources,1,nextSource,nextRotation,.75f);
        Assert.Same(nextSource,changed.CelestialSource);
        Assert.Equal(nextRotation,changed.CelestialRotation);
        Assert.Equal(.75f,changed.CloudCoverage);
        Assert.Equal(1,changed.Altitude);
        Assert.NotEqual(admitted.CelestialEnvironment,changed.CelestialEnvironment);
        Assert.NotEqual(admitted.ExternalTransmission.ToArray(),changed.ExternalTransmission.ToArray());
        Vector3 expected = CelestialDiffuseLighting.Integrate(nextSource,nextRotation,changed.ExternalTransmission,
            changed.HorizonElevation,changed.CloudCoverage);
        Assert.Equal(expected,changed.CelestialEnvironment);
        for(int i=0;i<10;i++) Assert.Null(backend.Update(sources,1,.75f,16,8,0,.1f,nextSource,nextRotation));
        Assert.Null(backend.PendingCpu);
        var rotated = Complete(backend,sources,1,nextSource,firstRotation,.75f);
        Assert.Equal(changed.ExternalTransmission,rotated.ExternalTransmission);
        Assert.NotEqual(changed.CelestialEnvironment,rotated.CelestialEnvironment);
        Assert.Equal(changed.Solar,rotated.Solar); Assert.Equal(changed.Lunar,rotated.Lunar);
        backend.Reload();
        var reloaded = Complete(backend,sources,1,nextSource,firstRotation,.75f);
        Assert.Same(nextSource,reloaded.CelestialSource);
        Assert.Equal(rotated.CelestialEnvironment,reloaded.CelestialEnvironment);
    }
    /// <summary>One backend completes both emitters, reuses phase-independent transport and withdraws caches on reload.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void BackendPublishesCoherentPairAndCachesPhaseChanges(bool forceCpu)
    {
        // Arrange
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        if (forceCpu) assets.BeforeRead = path => { if (path.EndsWith(".spv", StringComparison.Ordinal)) throw new IOException("Intentional CPU fallback"); };
        using var backend = new AtmosphereBackend(assets.Api);
        var sources = new AtmosphereCelestialInputs(-Vector3.UnitY, Vector3.UnitY, 4);
        // Act
        var full = Complete(backend, sources);
        // Assert
        Assert.True(full.HasNightSources);
        Assert.Equal(sources.Sun, full.Sun);
        Assert.Equal(sources.Moon, full.Moon);
        Assert.True(full.MoonIsDirect);
        Assert.True(full.Lunar.X > 0);
        Assert.Equal(Vector3.Zero, full.Solar);
        Assert.Null(backend.Update(sources, 0, 0, 16, 8, 0, .1f));
        var half = Assert.IsType<AtmosphereLighting>(backend.Update(sources with { Phase = 2 }, 0, 0, 16, 8, 0, .1f));
        Assert.Equal(full.Solar, half.Solar);
        Assert.InRange(Math.Abs(half.Lunar.X / full.Lunar.X - AtmosphereNightLighting.PhaseFraction(2)), 0, 1e-6f);
        Assert.Null(backend.PendingCpu);
        backend.Reload();
        var reloaded = Complete(backend, sources with { Phase = 0 });
        Assert.Equal(Vector3.Zero, reloaded.Lunar);
        Assert.False(reloaded.MoonIsDirect);
        Assert.True(reloaded.Environment.X > 0);
        Assert.Contains(reloaded.Sky.Where((_, index) => (index & 3) != 3), value => value > 0);
        if (forceCpu) Assert.Single(assets.Logs, log => log.Contains("using CPU transport"));
    }

    /// <summary>Changing the requested pair during asynchronous CPU work cannot mix generations.</summary>
    [Fact]
    public void PendingPairRetainsItsAdmittedDirectionsAndPhase()
    {
        // Arrange
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        assets.BeforeRead = path => { if (path.EndsWith(".spv", StringComparison.Ordinal)) throw new IOException("Intentional CPU fallback"); };
        using var backend = new AtmosphereBackend(assets.Api);
        var admitted = new AtmosphereCelestialInputs(-Vector3.UnitY, Vector3.UnitY, 4);
        // Act
        Assert.Null(backend.Update(admitted, 0, 0, 16, 8, 0, .1f));
        var changed = new AtmosphereCelestialInputs(Vector3.UnitX, -Vector3.UnitY, 0);
        var previous = Complete(backend, changed);
        // Assert
        Assert.Equal(admitted.Sun, previous.Sun);
        Assert.Equal(admitted.Moon, previous.Moon);
        Assert.True(previous.Lunar.X > 0);
        var current = Complete(backend, changed);
        Assert.Equal(changed.Sun, current.Sun);
        Assert.Equal(changed.Moon, current.Moon);
        Assert.Equal(Vector3.Zero, current.Lunar);
    }

    /// <summary>Reusing solar transport within one geometry bucket cannot mix two raw altitudes when the moon moves.</summary>
    [Fact]
    public void SameGeometryBucketCompletesWithChangedMoon()
    {
        // Arrange
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var backend = new AtmosphereBackend(assets.Api);
        var sources = new AtmosphereCelestialInputs(-Vector3.UnitY, Vector3.UnitY, 4);
        // Act
        var first = Complete(backend, sources, .101f);
        var changed = Complete(backend, sources with { Moon = Vector3.Normalize(new Vector3(0, 1, 1)) }, .109f);
        // Assert
        Assert.Equal(.1f, first.Altitude);
        Assert.Equal(first.Altitude, changed.Altitude);
        Assert.Equal(first.Solar, changed.Solar);
        Assert.NotEqual(first.Moon, changed.Moon);
        Assert.True(changed.Lunar.X > 0);
    }

    /// <summary>Actual binary16 texture bands reconstruct two angular lobes while retaining faint smooth night radiance.</summary>
    [Fact]
    public void ThreeBandsReconstructIndependentDirectionsAndFaintBackground()
    {
        // Arrange
        EnsureContextValid();
        const int width = 2, height = 2;
        const float smooth = .0008f / MathF.PI, solarMie = .002f, lunarMie = .003f;
        var sun = Vector3.UnitX;
        var moon = Vector3.UnitY;
        float[] total = new float[width * height * 4];
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            Vector3 direction = AtmosphereMieTransport.Direction(x, y, width, height, 0);
            float value = smooth + solarMie * AtmosphereMieTransport.Factor(Vector3.Dot(direction, sun))
                + lunarMie * AtmosphereMieTransport.Factor(Vector3.Dot(direction, moon));
            for (int channel = 0; channel < 3; channel++) total[(y * width + x) * 4 + channel] = value;
            total[(y * width + x) * 4 + 3] = 1;
        }
        var solar = ImmutableArray.CreateRange(Enumerable.Range(0, width * height).SelectMany(_ => new[] {solarMie, solarMie, solarMie, 1f}));
        var lunar = ImmutableArray.CreateRange(Enumerable.Range(0, width * height).SelectMany(_ => new[] {lunarMie, lunarMie, lunarMie, 1f}));
        var snapshot = new AtmosphereLighting(sun, Vector3.Zero, Vector3.Zero, Vector3.Zero, Vector3.Zero, ImmutableArray.Create(total))
        {
            Width = width, Height = height, HasNightSources = true, Moon = moon,
            SkyMie = solar, SkyMoonMie = lunar, LookupStorageScale = LightingStorage.PersistentScale, CalibrationGeneration = 1,
            AerialRadiance = ImmutableArray.CreateRange(Enumerable.Range(0, AtmosphereAerialPerspective.Depth).SelectMany(_ => total)),
            AerialAttenuation = ImmutableArray.CreateRange(Enumerable.Range(0, width * height * AtmosphereAerialPerspective.Depth).SelectMany(_ => new[] {0f, 0f, 0f, 1f})),
            AerialMie = ImmutableArray.CreateRange(Enumerable.Range(0, AtmosphereAerialPerspective.Depth).SelectMany(_ => solar)),
            AerialMoonMie = ImmutableArray.CreateRange(Enumerable.Range(0, AtmosphereAerialPerspective.Depth).SelectMany(_ => lunar))
        };
        // Act
        using var textures = new AtmosphereTextureSet(snapshot);
        textures.Upload(snapshot);
        // Assert
        Assert.Equal(height * 3, textures.Sky.Height);
        Assert.Equal(height * 3, textures.Radiance.Height);
        var expected=new float[total.Length];
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int index=(y*width+x)*4;
            var direction=AtmosphereMieTransport.Direction(x,y,width,height,0);
            expected[index]=total[index];
            expected[index+1]=AtmosphereMieTransport.Factor(Vector3.Dot(direction,sun));
            expected[index+2]=AtmosphereMieTransport.Factor(Vector3.Dot(direction,moon));
        }
        using var framework=new ShaderTestFramework();
        using var oracle=framework.CreateTexture(width,height,TextureFormat.Rgba32f,expected);
        using var smoothImage=new GpuImageSnapshot(textures.Sky,new(new(),0,0,0,width,height));
        using var solarImage=new GpuImageSnapshot(textures.Sky,new(new(),0,height,0,width,height));
        using var lunarImage=new GpuImageSnapshot(textures.Sky,new(new(),0,height*2,0,width,height));
        ImageAssertions.Predicates.Predicate(smoothImage.Texture, AtmospherePredicates.AtmosphereCelestialPublicationTests_ThreeBandsReconstructIndependentDirectionsAndFaintBackground,solarImage.Texture,lunarImage.Texture,oracle,new(LightingStorage.PersistentScale,0,0,0)).AssertEqual();
        Assert.Throws<ArgumentException>(() => textures.Upload(snapshot with { HasNightSources = false }));
        Assert.Throws<ArgumentException>(() => textures.Upload(snapshot with { MoonAngularRadius = AtmosphereSolarDisk.AngularRadius * .5f }));
        AssertAerialDecoder(textures, smooth, solarMie, lunarMie);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Executes the production aerial decoder with distinct source axes and actual three-band volumes.</summary>
    private static void AssertAerialDecoder(AtmosphereTextureSet textures, float smooth, float solar, float lunar)
    {
        // Arrange
        using var programs = new ComponentShaderPrograms();
        using var program = programs.Create<AerialLookupProgram>();
        using var framework = new ShaderTestFramework();
        using var shaft = framework.CreateTexture(1, 1, TextureFormat.R32f, [0f]);
        using var output = framework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
        using var draw = new PostprocessDraw();
        program.Radiance = textures.Radiance;
        program.Attenuation = textures.Attenuation;
        program.Occlusion = shaft;
        var pass = FramebufferTestSetup.DescribeAttachments(output,
            [new(AttachmentIndex: 0, Load: AttachmentLoad.Clear, Clear: ColorClearValue.Float(0, 0, 0, 0))]);
        var pipeline = draw.Prepare(program, pass);
        foreach (Vector3 direction in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            // Act
            program.CaptureCelestialDecoder(direction);
            var commands = RHICommandContext.Current;
            using var boundary = commands.EnterBoundary(new("Tests.Celestial.AerialDecoder", true,
                graphics: [pipeline], operations: BoundaryOperations.RenderPasses));
            boundary.Run(() => draw.Submit(commands, pipeline, pass, program.CaptureBindings()));
            float expected = smooth + solar * AtmosphereMieTransport.Factor(direction.X)
                + lunar * AtmosphereMieTransport.Factor(direction.Y);
            // Assert
            foreach (float value in output[0].ReadTexel().Take(3)) Assert.True(Math.Abs(value - expected) <= .00001f,
                $"Aerial direction {direction}: actual {value:R}, expected {expected:R}");
        }
    }
    /// <summary>Completes bounded transport by waiting only for its submitted CPU or GPU dependency.</summary>
    private static AtmosphereLighting Complete(AtmosphereBackend backend, AtmosphereCelestialInputs sources, float altitude = 0,
        CelestialSource? source = null, Matrix4x4? rotation = null, float clouds = 0)
    {
        var elapsed = Stopwatch.StartNew();
        while (elapsed.Elapsed < TimeSpan.FromSeconds(30))
        {
            var result = backend.Update(sources, altitude, clouds, 16, 8, 0, .1f,source,rotation);
            if (result is not null) return result;
            if (backend.PendingCpu is { } pending) pending.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();
            else
            {
                using var fence = RHICommandContext.Current.InsertFence();
                Assert.Contains(RHICommandContext.Current.WaitCpu(fence, TimeSpan.FromSeconds(5)), new[] {CompletionStatus.Ready, CompletionStatus.Ready});
            }
        }
        throw new TimeoutException("Celestial pair did not complete.");
    }
    #endregion
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("predicate-atmosphere", 0, 0);
    }
    #endregion
}
