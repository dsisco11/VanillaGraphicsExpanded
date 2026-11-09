using TestUniformRing = VanillaGraphicsExpanded.Tests.Support.GPU.Native.TestUniformRing;
using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Observations;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
namespace VanillaGraphicsExpanded.Tests.Integration.LumOn;

/// <summary>Measures demand loading separately from the first validated draw, without timing thresholds.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnDebugShaderDemandTimingTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Public API
    /// <summary>Checks cold and cached demand, mode switching and program reuse while selected views match within tolerance.</summary>
    [Fact]
    public void SelectedAndCachedProgramsMatchPixelsWithinTolerance()
    {
        // Arrange: The shared procedure creates deterministic inputs and separate program generations.
        // Act: Select and render each debug view through cold, cached, and reused declarations.
        // Assert: Each rendered view remains within authored ranges and matches its first output within tolerance.
        RunDemandComparison(reportTiming: false);
    }

    /// <summary>Reports demand-loading costs explicitly, using the same in-memory cache and correctness oracle.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void ProfileSelectedAndCachedPrograms()
    {
        // Arrange: Use the same deterministic inputs and in-memory cache as the correctness case.
        // Act: Run cold, cached, and reused selections while recording load and first-use timings.
        // Assert: The shared color/alpha oracle still holds; timing output has no pass threshold.
        RunDemandComparison(reportTiming: true);
    }

    #endregion

    #region Protected
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("inspection", 0, 0);
        programs.Prepare("layer", 0, 0);
        programs.Prepare("compare", 0, 0);
    }

    #endregion

    #region Private
    /// <summary>Exercises cold, cached and reused programs with isolated storage and optional timing output.</summary>
    private void RunDemandComparison(bool reportTiming)
    {
        // Arrange shared inputs and isolate each generation's declarations, cache reads, and output target.
        EnsureShaderTestAvailable();
        using var engine = new EngineShaderPlatformScope();
        const string directory = "debug-demand";

        using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: new VanillaGraphicsExpanded.Tests.Support.Cache.CountingDiskCacheFileSystem()));

        using var input = TestFramework.CreateTexture(4, 4, TextureFormat.Rgba32f, Enumerable.Repeat(1f, 64).ToArray());
        using var lightingArray = LayeredTestTexture.Create(ImageAssertions.Programs, input, input, input);
        using var target = TestFramework.CreateTestGBuffer(4, 4, TextureFormat.Rgba32f);
        using var frameLighting = new TestFrameLightingPublication();
        var baselinePixels = new Dictionary<string, GpuImageSnapshot>(StringComparer.Ordinal);
        try
        {
            for (int generation = 0; generation < 2; generation++)
            {
                // Arrange a fresh API generation before registering production and debug declarations.
                using var assets = new BinaryShaderApiFixture();
                long started = Stopwatch.GetTimestamp();
                // Act by registering debug declarations, then register production declarations and preload the selected set.
                Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
                if (reportTiming) output.WriteLine($"Generation={generation}, declaration={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms, reads={assets.Reads.Count}.");
                started = Stopwatch.GetTimestamp();
                Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
                Assert.Empty(assets.PreparedPrograms);
                // Capture the authored declaration set before native preparation; linked output cannot define its own oracle.
                var declaredProduction = GpuShaderProgramLibraries.GetAll(assets.Api)
                    .Where(program => program is not LumOnDebugShaderProgram).ToImmutableArray();
                var declaredNames = declaredProduction.Select(program => program.PassName).ToHashSet(StringComparer.Ordinal);
                Assert.NotEmpty(declaredNames);
                Assert.Equal(declaredProduction.Length, declaredNames.Count);
                // The ordinary row keeps one real nondebug coexistence witness; its diagnostic row still preloads all declarations.
                var production = reportTiming
                    ? declaredProduction
                    : declaredProduction.Where(program => string.Equals(program.PassName, "lumon_combine", StringComparison.Ordinal)).ToImmutableArray();
                if (!reportTiming) Assert.Single(production);
                var expectedPrograms = production.Select(program => program.PassName).ToHashSet(StringComparer.Ordinal);
                Assert.Equal(production.Length, expectedPrograms.Count);
                Assert.True(GpuShaderProgramLibraries.Preload(assets.Api, production));
                if (reportTiming) output.WriteLine($"Generation={generation}, explicitproductionpreload={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms, registered={assets.PreparedPrograms.Count}.");
                // Assert the explicit preload prepared precisely the authored production selection.
                Assert.Equal(expectedPrograms.Order(), assets.PreparedPrograms.Keys.Order());
                foreach (string name in new[] { "lumon_debug_view_direct_diffuse", "lumon_debug_view_direct_specular", "lumon_debug_view_direct_diffuse" })
                {
                    // Act by selecting the requested debug view; repeated selection must reuse the ready program.
                    Assert.True(LumOnDebugShaderProgramFamily.TryGet(name, out var program));
                    bool firstSelection = expectedPrograms.Add(name);
                    int priorReads = assets.Reads.Count;
                    started = Stopwatch.GetTimestamp();
                    Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, program), string.Join('\n', assets.Logs));
                    double selection = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    int reads = assets.Reads.Count - priorReads;
                    // Assert demand reads and cache provenance before rendering this selected view.
                    if (!firstSelection) Assert.Equal(0, reads);
                    if (reads != 0) Assert.Equal(generation == 1, DriverProgramCache.LastLoadWasHit);
                    TestUniformRing.BeginFrame();
                    started = Stopwatch.GetTimestamp();
                    {
                        // Act by publishing the view's matching frame inputs and rendering its output.
                        UpdateAndBindLumOnFrameUbo(program);
                        // This isolated alternate view owns its matching unit-scale publication, independent of scene exposure.
                        program.FrameLightingInputs = frameLighting.Buffer;
                        program.DebugMode = 22;
                        program.DirectLighting = lightingArray;

                        TestFramework.RenderQuadTo(program, target, (1f, 0f, 0f, 0f));
                    }
                    // Assert the selected render produced finite RGB and opaque alpha before reporting its timing.
                    ImageAssertions.Statistics.Inspect(target[0],.499f,.501f,channelMask:7).AssertFiniteRange();
                    ImageAssertions.Statistics.Inspect(target[0],.999f,1f,channelMask:8).AssertFiniteRange();
                    double firstUse = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    if (baselinePixels.TryGetValue(name, out var baseline))
                    {
                        ImageAssertions.Comparisons.CompareHistory(target[0], baseline, tolerance: 0.001f, channelMask: 15).AssertEqual();
                    }
                    else
                    {
                        baselinePixels.Add(name, new GpuImageSnapshot(target[0]));
                    }
                    if (reportTiming) output.WriteLine($"Generation={generation}, name={name}, selection={selection:F3} ms, first-use/readback={firstUse:F3} ms, assetreads={reads}, cachehit={DriverProgramCache.LastLoadWasHit}.");
                }
                // Assert the generation contains exactly the selected production and demand-loaded debug programs.
                Assert.Equal(expectedPrograms.Order(), assets.PreparedPrograms.Keys.Order());
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
        }
        finally
        {
            foreach (var snapshot in baselinePixels.Values) snapshot.Dispose();
        }
    }

    #endregion

}
