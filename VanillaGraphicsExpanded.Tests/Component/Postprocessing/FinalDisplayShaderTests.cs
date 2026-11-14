using VanillaGraphicsExpanded.Tests.Support.Cache;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;

using Assertions = VanillaGraphicsExpanded.Tests.Support.GPU.Observations;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.Numerics;
namespace VanillaGraphicsExpanded.Tests.Component.Postprocessing;

/// <summary>Checks owned final composition against independent radiometric and display expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class FinalDisplayShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{


    private readonly CountingDiskCacheFileSystem storage = new();

    #region Public API
    /// <summary>Already lit scene radiance receives additive effects, then one exposure and display transform.</summary>
    [Theory]
    [InlineData(-2f, false)]
    [InlineData(0f, false)]
    [InlineData(2f, true)]
    public void CompositionResolvesOnceAndPreservesInputs(float ev, bool automatic)
    {
        // Arrange: Prepare fixed scene, bloom, shaft, and exposure inputs with row-selected EV and camera mode.
        EnsureShaderTestAvailable();
        // Act and assert: Submit final composition and compare one exposure/display resolve while preserving each source.
        using var grading = new ColorGradingLut();
        var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        var shader = Programs.Create<FinalDisplayShaderProgram>();
        using var scene = TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[4f,4f,4f,.37f]);
        using var bloom = TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[1f,1f,1f,1f]);
        using var shafts = TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[2f,2f,2f,1f]);
        using var history = ExposureTestStorage.Create(ev);
        using var target = TestFramework.CreateTestGBuffer(1,1,TextureFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(1, 1);
        shader.FrameInputs = camera; shader.GradingLookup = lookup;
        shader.SceneImage=scene; shader.BloomImage=bloom; shader.ShaftImage=shafts;
        shader.ExposureBuffer=history;
        shader.Capture(false,new(new(1,1,1,0),0,0,0,0,0,0),automatic?-ev:ev,automatic,0);
        Draw(shader,target);
        float radiance=(4f+3f)*MathF.Pow(2,ev);
        float expected=Display(radiance);
        float[] actual=target[0].ReadPixels();
        for(int i=0;i<3;i++) Assert.InRange(actual[i],expected-1.001f/255,expected+1.001f/255);
        Assert.Equal(1,actual[3]);
        Assert.Equal(new[]{4f,4f,4f,.37f},scene.ReadPixels());
    }
    /// <summary>Measures a bounded synthetic final-pass workload without claiming live frame cost or physical bandwidth.</summary>
    [Theory]
    [InlineData(1280,720)]
    [InlineData(1920,1080)]
    [InlineData(2560,1440)]
    [Trait("Purpose", "Diagnostic")]
    public void SyntheticFinalPassTiming(int width,int height)
    {
        // Arrange: Build fixed 1x1 inputs and a parameterized output target after explicit shader setup.
        EnsureShaderTestAvailable();
        using var grading = new ColorGradingLut();
        var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        var shader=Programs.Create<FinalDisplayShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[4f,2f,1f,.5f]);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var zero=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,0f,0f,0f]);
        using var target=TestFramework.CreateTestGBuffer(width,height,TextureFormat.Rgba16f);
        using var camera = TestFrameCamera.CreateIdentity(width, height);
        shader.FrameInputs = camera; shader.GradingLookup = lookup;
        shader.SceneImage=source;shader.BloomImage=zero;shader.ShaftImage=zero;shader.ExposureBuffer=neutralExposure;
        shader.Capture(true,new(new(1,1,1,0),0,0,0,0,0,0),0,false,0);
        // Act: Warm and time repeated complete owned final-pass submissions at the requested target extent.
        Draw(shader,target);
        var samples=new List<double>();
        for(int i=0;i<5;i++)
        {
            using var timer=GpuTimerQuery.Create();
            timer.Begin();Draw(shader,target);timer.End();samples.Add(timer.GetResultNanoseconds()/1e6);
        }
        // Report measured GPU query timings and target payload; this diagnostic intentionally defines no performance threshold.
        output.WriteLine($"Synthetic owned final {width}x{height}, uniform 1x1 inputs, native antialias setting enabled, five warm samples: min={samples.Min():F4}ms median={samples.Order().ElementAt(2):F4}ms max={samples.Max():F4}ms; target payload={(long)width*height*8} bytes. Not live cost or physical bandwidth.");
    }
    /// <summary>Independent grading and screen treatments respond to native controls while preserving opaque output and source radiance.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DisplayControlsAffectOutputWithoutChangingScene(int treatment)
    {
        // Arrange: Capture a baseline scene, then select one native grading, sepia, or screen-treatment change.
        EnsureShaderTestAvailable();
        using var grading = new ColorGradingLut();
        var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        var shader=Programs.Create<FinalDisplayShaderProgram>();
        using var source=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[.18f,.18f,.18f,.3f]);
        using var neutralExposure = ExposureTestStorage.Create(0);
        using var zero=TestFramework.CreateTexture(1,1,TextureFormat.Rgba32f,[0f,0f,0f,0f]);
        using var target=TestFramework.CreateTestGBuffer(17,9,TextureFormat.Rgba32f);
        using var camera = TestFrameCamera.CreateIdentity(17, 9);
        shader.FrameInputs = camera; shader.GradingLookup = lookup;
        shader.SceneImage=source;shader.BloomImage=zero;shader.ShaftImage=zero;shader.ExposureBuffer=neutralExposure;
        shader.Capture(false,new(new(1,1,1,0),0,0,0,0,0,0),0,false,0);Draw(shader,target);
        using var baseline=new GpuImageSnapshot(target[0]);
        var controls=treatment==0?new FinalDisplayParameters(new(2,1,1,0),0,0,0,0,0,0):
            treatment==1?new FinalDisplayParameters(new(1,1,1,0),0,0,0,1,.5f,0):
            new FinalDisplayParameters(new(1,1,1,0),0,2,.5f,0,0,1);
        shader.GradingLookup=grading.Prepare(Programs.Api,controls.Grading);
        // Act: Submit the selected control change through the production final shader.
        shader.Capture(false,controls,0,false,0);Draw(shader,target);
        // Preserve every RGB witness and every finite/range/alpha assertion over the full 17x9 domain.
        // Assert: Output changes while remaining finite and opaque, and the original scene pixels stay untouched.
        Assert.True(ImageAssertions.Predicates.Predicate(target[0], MaterialPredicates.FinalDisplayShaderTests_DisplayControlsAffectOutputWithoutChangingScene,baseline.Texture).MatchingTexels>0);
        ImageAssertions.Statistics.Inspect(target[0],0,1).AssertFiniteRange();
        ImageAssertions.Comparisons.CompareConstant(target[0],1,channelMask:8).AssertEqual();
        if(treatment==0)Assert.True(GpuLocalRead.Scalar(target[0],0)>GpuLocalRead.Scalar(baseline.Texture,0));
        Assert.Equal(new[]{.18f,.18f,.18f,.3f},source.ReadPixels());
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
    }
    #endregion

    #region Private
    /// <summary>Independently reconstructs the neutral filmic LUT and first-pixel dither quantization.</summary>
    private static float Display(float radiance)
    {
        return FilmicGradingReference.Quantize(FilmicGradingReference.Lookup(new(radiance), new(1, 1, 1, 0)).X);
    }

    /// <summary>Draws the production procedural triangle after readback with deterministic raster state.</summary>
    private static void Draw(FinalDisplayShaderProgram shader,GpuFramebuffer target)
    {
        using var draw = new PostprocessDraw();
        var pipeline = draw.Prepare(shader,target.PostprocessPass());
        var commands=RHICommandContext.Current;
        using var boundary=commands.EnterBoundary(new("Tests.FinalDisplay",true,graphics:[pipeline],operations:BoundaryOperations.RenderPasses));
        boundary.Run(()=>draw.Submit(commands,pipeline,target.PostprocessPass(),shader.CaptureBindings()));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
