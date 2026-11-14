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
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.Postprocessing;

/// <summary>Exercises registered final-display preparation through cache hits and recovery after a failed replacement.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class FinalDisplayShaderTests(HeadlessGLFixture fixture, ITestOutputHelper output) : LumOnShaderFunctionalTestBase(fixture)
{


    private readonly CountingDiskCacheFileSystem storage = new();

    #region Public API
    /// <summary>Exercises the original registered final-display path through cold, warm, corrupt and batched executable preparation.</summary>
    [Fact]
    public void FinalDisplayCachePathsNeverRequireEngineStagesOrRegistration()
    {
        // Arrange: Create the registered shader, a deterministic one-pixel scene, and an isolated binary cache.
        EnsureShaderTestAvailable();
        using var grading = new ColorGradingLut();
        var lookup = grading.Prepare(Programs.Api, new(1, 1, 1, 0));
        string directory = Path.Combine(Path.GetTempPath(), "VGE.FinalCache", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new VanillaGraphicsExpanded.Rendering.ProgramBinaries.ProgramBinaryStore(directory, fileSystem: storage);
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
            shader.Capture(false, new(new(1, 1, 1, 0), 0, 0, 0, 0, 0, 0), 0, false, 0);
            ulong revision = 0;
            // Visit cold compile, binary hit, corrupt-binary recovery, then warm batch preload in a fixed order.
            // Act: Visit cold compile, binary hit, corrupt-binary recovery, and warm batch preload in fixed order.
            for (int path = 0; path < 4; path++)
            {
                if (path != 0) shader.InvalidateAssets();
                if (path == 2)
                {
                    string key = Path.GetFileNameWithoutExtension(Assert.Single(storage.EnumerateFiles(directory, "*.bin")));
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
                Assert.InRange(target[0].ReadTexel()[0], Display(4f) - .00001f, Display(4f) + .00001f);
            }
            // Assert: Each executable produces the independent display reference; failed replacement retains the last valid revision.
            int installed = shader.ProgramId;
            // A failed replacement must retain the last complete executable and its published revision.
            shader.InvalidateAssets();
            assets.BeforeRead = _ => throw new IOException("controlled final asset failure");
            Assert.False(shader.EnsureReady());
            Assert.Contains("controlled final asset failure", shader.PreparationFailure!.Message);
            Assert.Equal(installed, shader.ProgramId);
            Assert.Equal(revision, shader.ExecutableRevision);
            Assert.True(GL.IsProgram(installed));
            Assert.True(shader.RequiresPreparation);
        }
        finally { if (storage.DirectoryExists(directory)) storage.DeleteDirectory(directory, true); }
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
