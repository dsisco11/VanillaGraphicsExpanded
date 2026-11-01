using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using System.Diagnostics;
using System.Collections.Immutable;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures demand loading separately from the first validated draw, without timing thresholds.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnDebugShaderDemandTimingTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Demand timing
    /// <summary>Checks cold and cached demand, mode switching and program reuse against identical output.</summary>
    [Fact]
    public void SelectedAndCachedProgramsRenderIdenticalPixels()
    {
        RunDemandComparison(reportTiming: false);
    }

    /// <summary>Reports demand-loading costs explicitly, using the same in-memory cache and correctness oracle.</summary>
    [Fact]
    [Trait("Purpose", "Diagnostic")]
    public void ProfileSelectedAndCachedPrograms()
    {
        RunDemandComparison(reportTiming: true);
    }
    #endregion

    #region Private
    /// <summary>Exercises cold, cached and reused programs with isolated storage and optional timing output.</summary>
    private void RunDemandComparison(bool reportTiming)
    {
        EnsureShaderTestAvailable();
        using var engine = new EngineShaderPlatformScope();
        const string directory = "debug-demand";

        using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory, fileSystem: new VanillaGraphicsExpanded.Tests.TestSupport.CountingDiskCacheFileSystem()));

        using var input = TestFramework.CreateTexture(4, 4, TextureFormat.Rgba32f, Enumerable.Repeat(1f, 64).ToArray());
        using var lightingArray = LayeredTestTexture.Create(input, input, input);
        using var target = TestFramework.CreateTestGBuffer(4, 4, TextureFormat.Rgba32f);
        using var frameLighting = new TestFrameLightingPublication();
        for (int generation = 0; generation < 2; generation++)
        {
            using var assets = new BinaryShaderApiFixture();
            long started = Stopwatch.GetTimestamp();
            Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
            if (reportTiming) output.WriteLine($"Generation={generation}, declaration={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms, reads={assets.Reads.Count}.");
            started = Stopwatch.GetTimestamp();
            Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
            Assert.Empty(assets.PreparedPrograms);
            // Capture the authored declaration set before native preparation; linked output cannot define its own oracle.
            var production = GpuShaderProgramLibraries.GetAll(assets.Api)
                .Where(program => program is not LumOnDebugShaderProgram).ToImmutableArray();
            var expectedPrograms = production.Select(program => program.PassName).ToHashSet(StringComparer.Ordinal);
            Assert.NotEmpty(expectedPrograms);
            Assert.Equal(production.Length, expectedPrograms.Count);
            Assert.True(GpuShaderProgramLibraries.Preload(assets.Api, production));
            if (reportTiming) output.WriteLine($"Generation={generation}, explicitproductionpreload={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms, registered={assets.PreparedPrograms.Count}.");
            Assert.Equal(expectedPrograms.Order(), assets.PreparedPrograms.Keys.Order());
            foreach (string name in new[] { "lumon_debug_view_direct_diffuse", "lumon_debug_view_direct_specular", "lumon_debug_view_direct_diffuse" })
            {
                Assert.True(LumOnDebugShaderProgramFamily.TryGet(name, out var program));
                bool firstSelection = expectedPrograms.Add(name);
                int priorReads = assets.Reads.Count;
                started = Stopwatch.GetTimestamp();
                Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, program), string.Join('\n', assets.Logs));
                double selection = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                int reads = assets.Reads.Count - priorReads;
                if (!firstSelection) Assert.Equal(0, reads);
                if (reads != 0) Assert.Equal(generation == 1, DriverProgramCache.LastLoadWasHit);
                TestUniformRing.BeginFrame();
                started = Stopwatch.GetTimestamp();
                {
                    UpdateAndBindLumOnFrameUbo(program);
                    // This isolated alternate view owns its matching unit-scale publication, independent of scene exposure.
                    program.FrameLightingInputs = frameLighting.Buffer;
                    program.DebugMode = 22;
                    program.DirectLighting = lightingArray;

                    TestFramework.RenderQuadTo(program, target, (1f, 0f, 0f, 0f));
                }
                ImageAssertions.Inspect(target[0],.499f,.501f,channelMask:7).AssertFiniteRange();
                ImageAssertions.Inspect(target[0],.999f,1f,channelMask:8).AssertFiniteRange();
                double firstUse = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                if (reportTiming) output.WriteLine($"Generation={generation}, name={name}, selection={selection:F3} ms, first-use/readback={firstUse:F3} ms, assetreads={reads}, cachehit={DriverProgramCache.LastLoadWasHit}.");
            }
            Assert.Equal(expectedPrograms.Order(), assets.PreparedPrograms.Keys.Order());
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
    }
    #endregion
}
