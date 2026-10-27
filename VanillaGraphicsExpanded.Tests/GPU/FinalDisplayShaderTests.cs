using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks owned final composition against independent radiometric and display expectations.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class FinalDisplayShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Exercises the original registered final-display path through cold, warm, corrupt and batched executable preparation.</summary>
    [Fact]
    public void FinalDisplayCachePathsNeverRequireEngineStagesOrRegistration()
    {
        EnsureShaderTestAvailable();
        using var grading = new ColorGradingLut();
        var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        string directory = Path.Combine(Path.GetTempPath(), "VGE.FinalCache", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VanillaGraphicsExpanded.Rendering.ProgramBinaries.ProgramBinaryStore(directory);
            using var cache = VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.UseStoreForTesting(store);
            using var assets = new BinaryShaderApiFixture();
            var shader = VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Declare(assets.Api, FinalDisplayShaderProgram.Contract.Identity, () => new FinalDisplayShaderProgram());
            using var publication = new TestFrameLightingPublication();
            shader.FrameLightingInputs = publication.Buffer;
            using var source = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [4f, 4f, 4f, .37f]);
        using var neutralExposure = ExposureTestStorage.Create(0);
            using var zero = TestFramework.CreateTexture(1, 1, TextureFormat.Rgba32f, [0f, 0f, 0f, 0f]);
            using var target = TestFramework.CreateTestGBuffer(1, 1, TextureFormat.Rgba32f);
            using var camera = TestFrameCamera.CreateIdentity(1, 1);
            shader.FrameInputs = camera; shader.GradingLookup = lookup;
            shader.SceneImage = source; shader.BloomImage = zero; shader.ShaftImage = zero; shader.ExposureBuffer=neutralExposure;
            shader.Capture(false, new(new(1, 1, 1, 0), Vector4.Zero, Vector4.Zero), Vector4.Zero);
            ulong revision = 0;
            for (int path = 0; path < 4; path++)
            {
                if (path != 0) shader.InvalidateAssets();
                if (path == 2)
                {
                    string key = Path.GetFileNameWithoutExtension(Assert.Single(Directory.GetFiles(directory, "*.bin")));
                    store.Write(key, -1, [1, 2, 3, 4]);
                }
                bool ready = path == 3
                    ? VanillaGraphicsExpanded.Rendering.Shaders.GpuShaderProgramLibraries.Preload(assets.Api, [shader])
                    : shader.EnsureReady();
                Assert.True(ready, string.Join("\n", assets.Logs));
                Assert.Equal(path is 1 or 3, VanillaGraphicsExpanded.Rendering.ProgramBinaries.DriverProgramCache.LastLoadWasHit);
                Assert.True(shader.ExecutableRevision > revision);
                revision = shader.ExecutableRevision;
                GL.GetProgram(shader.ProgramId, GetProgramParameterName.AttachedShaders, out int attached);
                Assert.Equal(0, attached);
                Draw(shader, target);
                Assert.InRange(target[0].ReadPixels()[0], Display(4f) - .00001f, Display(4f) + .00001f);
            }
            int installed = shader.ProgramId;
            shader.InvalidateAssets();
            assets.BeforeRead = _ => throw new IOException("controlled final asset failure");
            Assert.False(shader.EnsureReady());
            Assert.Contains("controlled final asset failure", shader.PreparationFailure!.Message);
            Assert.Equal(installed, shader.ProgramId);
            Assert.Equal(revision, shader.ExecutableRevision);
            Assert.True(GL.IsProgram(installed));
            Assert.True(shader.RequiresPreparation);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Already lit scene radiance receives additive effects, then one exposure and display transform.</summary>
    [Theory]
    [InlineData(-2f, false)]
    [InlineData(0f, false)]
    [InlineData(2f, true)]
    public void CompositionResolvesOnceAndPreservesInputs(float ev, bool automatic)
    {
        EnsureShaderTestAvailable();
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
        shader.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),new(automatic?-ev:ev,automatic?1:0,0,0));
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
    public void SyntheticFinalPassTiming(int width,int height)
    {
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
        shader.Capture(true,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);
        Draw(shader,target);
        var samples=new List<double>();
        for(int i=0;i<5;i++)
        {
            using var timer=GpuTimerQuery.Create();
            timer.Begin();Draw(shader,target);timer.End();samples.Add(timer.GetResultNanoseconds()/1e6);
        }
        output.WriteLine($"Synthetic owned final {width}x{height}, uniform 1x1 inputs, native antialias setting enabled, five warm samples: min={samples.Min():F4}ms median={samples.Order().ElementAt(2):F4}ms max={samples.Max():F4}ms; target payload={(long)width*height*8} bytes. Not live cost or physical bandwidth.");
    }
    /// <summary>Independent grading and screen treatments respond to native controls while preserving opaque output and source radiance.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void DisplayControlsAffectOutputWithoutChangingScene(int treatment)
    {
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
        shader.Capture(false,new(new(1,1,1,0),Vector4.Zero,Vector4.Zero),Vector4.Zero);Draw(shader,target);
        float[] baseline=target[0].ReadPixels();
        var controls=treatment==0?new FinalDisplayParameters(new(2,1,1,0),Vector4.Zero,Vector4.Zero):
            treatment==1?new FinalDisplayParameters(new(1,1,1,0),Vector4.Zero,new(1,.5f,0,0)):
            new FinalDisplayParameters(new(1,1,1,0),new(0,2,.5f,0),new(0,0,1,0));
        shader.GradingLookup=grading.Prepare(Programs.Api,controls.Grading);
        shader.Capture(false,controls,Vector4.Zero);Draw(shader,target);
        float[] changed=target[0].ReadPixels();
        Assert.Contains(Enumerable.Range(0,changed.Length),i=>i%4!=3&&Math.Abs(changed[i]-baseline[i])>.02f);
        for(int i=0;i<changed.Length;i++)
        {
            Assert.True(float.IsFinite(changed[i]));Assert.InRange(changed[i],0,1);
            if(i%4==3)Assert.Equal(1,changed[i]);
        }
        if(treatment==0)Assert.True(changed[0]>baseline[0]);
        Assert.Equal(new[]{.18f,.18f,.18f,.3f},source.ReadPixels());
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
