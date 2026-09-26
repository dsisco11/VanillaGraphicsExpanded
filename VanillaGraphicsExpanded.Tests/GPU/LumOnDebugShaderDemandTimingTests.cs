using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Measures demand loading separately from the first validated draw, without timing thresholds.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnDebugShaderDemandTimingTests(HeadlessGLFixture fixture, ITestOutputHelper output)
    : LumOnShaderFunctionalTestBase(fixture)
{
    #region Demand timing
    /// <summary>Profiles cold and cached selection, mode switching and reuse when explicitly requested.</summary>
    [Fact]
    public void SelectedAndCachedProgramsRenderIdenticalPixels()
    {
        if (Environment.GetEnvironmentVariable("VGE_DEBUG_DEMAND_PROFILE") != "1") return;
        EnsureShaderTestAvailable();
        using var engine = new EngineShaderPlatformScope();
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DebugDemandTests", Guid.NewGuid().ToString("N"));
        try
        {
            using var cache = DriverProgramCache.UseStoreForTesting(new ProgramBinaryStore(directory));

            using var input = TestFramework.CreateTexture(4, 4, PixelInternalFormat.Rgba32f, Enumerable.Repeat(1f, 64).ToArray());
            using var target = TestFramework.CreateTestGBuffer(4, 4, PixelInternalFormat.Rgba32f);
            for (int generation = 0; generation < 2; generation++)
            {
                using var assets = new BinaryShaderApiFixture();
                long started = Stopwatch.GetTimestamp();
                Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
                output.WriteLine($"Generation={generation}, declaration={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms, reads={assets.Reads.Count}.");
                started = Stopwatch.GetTimestamp();
                Assert.True(VgeShaderPrograms.RegisterAll(assets.Api));
                output.WriteLine($"Generation={generation}, productionstartup={Stopwatch.GetElapsedTime(started).TotalMilliseconds:F3} ms, registered={assets.RegisteredPrograms.Count}.");
                Assert.Equal(19, assets.RegisteredPrograms.Count);
                foreach (string name in new[] { "lumon_debug_direct", "lumon_debug", "lumon_debug_direct" })
                {
                    Assert.True(LumOnDebugShaderProgramFamily.TryGet(name, out var program));
                    int priorReads = assets.Reads.Count;
                    started = Stopwatch.GetTimestamp();
                    Assert.True(LumOnDebugShaderProgramFamily.EnsureReady(assets.Api, program), string.Join('\n', assets.Logs));
                    double selection = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
                    int reads = assets.Reads.Count - priorReads;
                    if (reads != 0) Assert.Equal(generation == 1, DriverProgramCache.LastLoadWasHit);
                    TestUniformRing.BeginFrame();
                    started = Stopwatch.GetTimestamp();
                    using (program.UseScope())
                    {
                        UpdateAndBindLumOnFrameUbo(program);
                        program.DebugMode = 22;
                        program.DirectDiffuse = input;

                        TestFramework.RenderQuadTo(program, target, (1f, 0f, 0f, 0f));
                    }
                    float[] pixels = target[0].ReadPixels();
                    double firstUse = Stopwatch.GetElapsedTime(started).TotalMilliseconds;

                    for (int pixel = 0; pixel < 16; pixel++)
                    {
                        Assert.InRange(pixels[(pixel << 2)], .499f, .501f);
                        Assert.InRange(pixels[(pixel << 2) + 1], .499f, .501f);
                        Assert.InRange(pixels[(pixel << 2) + 2], .499f, .501f);
                        Assert.InRange(pixels[(pixel << 2) + 3], .999f, 1f);
                    }
                    output.WriteLine($"Generation={generation}, name={name}, selection={selection:F3} ms, first-use/readback={firstUse:F3} ms, assetreads={reads}, cachehit={DriverProgramCache.LastLoadWasHit}.");
                }
                Assert.Equal(21, assets.RegisteredPrograms.Count);
                Assert.Equal(ErrorCode.NoError, GL.GetError());
            }
        }
        finally
        {
            // The test owns this unique temporary cache directory and no shared application files.
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
    #endregion
}
