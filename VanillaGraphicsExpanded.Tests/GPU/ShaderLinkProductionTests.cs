using System.Diagnostics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks complete production registration and configuration batching without starting the game.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderLinkProductionTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Registration and configuration
    /// <summary>Checks production registration; VGE_SHADER_LINK_PROFILE=1 also records matched ABBA loading timings.</summary>
    [Fact]
    public void ProductionRegistrationAndConfigurationTimings()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var depth = DynamicTexture2D.Create(8, 8, PixelInternalFormat.R32f);
        depth.UploadDataImmediate(Enumerable.Repeat(.5f, 64).ToArray());
        using var target = CreateRenderTarget(8, 8, PixelInternalFormat.R32f);
        bool profile = Environment.GetEnvironmentVariable("VGE_SHADER_LINK_PROFILE") == "1";
        foreach (bool disable in profile ? new[] { true, false, false, true } : new[] { false })
        {
            using var policy = disable ? ShaderLinkBatch.UseSynchronousForTesting() : null;
            using var assets = new BinaryShaderApiFixture();
            double submitted = 0, completed = 0;
            int consumed = 0, peak = 0;
            using var observations = ShaderLinkBatch.ObserveForTesting(batch =>
            {
                submitted += batch.SubmissionMilliseconds;
                completed += batch.CompletionMilliseconds;
                consumed += batch.ConsumedCount;
                peak = Math.Max(peak, batch.PeakOutstanding);
            });
            long started = Stopwatch.GetTimestamp();
            Assert.True(VgeShaderPrograms.RegisterAll(assets.Api), string.Join('\n', assets.Logs));
            double startup = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            double startupUse = ObserveHzb(assets, depth, target);
            output.WriteLine($"Startup driver counters: submit={submitted:F3} ms, completion={completed:F3} ms, consumed={consumed}, peak={peak}");
            submitted = completed = 0; consumed = peak = 0;
            Assert.Equal(19 + LumOnDebugShaderProgram.Contracts.Count(), assets.RegisteredPrograms.Count);
            Assert.Equal(LumOnDebugShaderProgram.Contracts.Count(), LumOnDebugShaderProgramFamily.GetAll().Count());
            foreach (var program in assets.RegisteredPrograms.Values) Assert.True(GL.IsProgram(program.ProgramId));

            // A shared option edit should schedule one callback for all accepting owners.
            var changed = assets.RegisteredPrograms.Values.OfType<GpuProgram>()
                .Where(program => program.ProgramContract.Options.Any(option => option.Name == "VGE_LUMON_DIRECT_LOCAL_VISIBILITY")).ToArray();
            Assert.True(changed.Length > 1);
            int[] previous = changed.Select(program => program.ProgramId).ToArray();
            foreach (var program in changed)
                Assert.True(program.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" }));
            var update = Assert.Single(assets.ScheduledTasks);
            assets.ScheduledTasks.Clear();
            started = Stopwatch.GetTimestamp();
            update();
            double configuration = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            double configurationUse = ObserveHzb(assets, depth, target);
            output.WriteLine($"Configuration driver counters: submit={submitted:F3} ms, completion={completed:F3} ms, consumed={consumed}, peak={peak}");
            submitted = completed = 0; consumed = peak = 0;
            Assert.Empty(assets.ScheduledTasks);
            for (int index = 0; index < changed.Length; index++)
            {
                Assert.NotEqual(previous[index], changed[index].ProgramId);
                // Later owners may reuse deleted GL names; identity is checked per owner.
                Assert.Equal("1", changed[index].InstalledSettings!.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
            }

            started = Stopwatch.GetTimestamp();
            Assert.True(VgeShaderPrograms.RegisterAll(assets.Api), string.Join('\n', assets.Logs));
            double reload = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
            double reloadUse = ObserveHzb(assets, depth, target);
            output.WriteLine($"Re-registration driver counters: submit={submitted:F3} ms, completion={completed:F3} ms, consumed={consumed}, peak={peak}");
            Assert.Equal(19 + LumOnDebugShaderProgram.Contracts.Count(), assets.RegisteredPrograms.Count);
            output.WriteLine($"Production mode={(disable ? "sync" : "batch")}, registered={assets.RegisteredPrograms.Count}, changed={changed.Length}, startup={startup:F3} ms, configuration={configuration:F3} ms, re-registration={reload:F3} ms; HZB use+readback startup={startupUse:F3}/configuration={configurationUse:F3}/re-registration={reloadUse:F3} ms.");
            Assert.Equal(ErrorCode.NoError, GL.GetError());
        }
    }
    /// <summary>A failed debug member retains its previous usable owner while independent members reload.</summary>
    [Fact]
    public void DebugFamilyRetainsFailedReplacement()
    {
        EnsureContextValid();
        using var cache = DriverProgramCache.UseStoreForTesting(null);
        using var assets = new BinaryShaderApiFixture();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_direct", out var previous));
        int installed = previous.ProgramId;
        assets.Overrides["shaders/lumon_debug_direct.fsh.spv"] = new byte[20];
        Assert.False(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_direct", out var retained));
        Assert.Same(previous, retained);
        Assert.Equal(installed, retained.ProgramId);
        Assert.True(GL.IsProgram(installed));
        Assert.Same(retained, assets.RegisteredPrograms["lumon_debug_direct"]);
        assets.Overrides.Clear();
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        Assert.True(LumOnDebugShaderProgramFamily.TryGet("lumon_debug_direct", out var repaired));
        Assert.NotSame(previous, repaired);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    /// <summary>Measures one production shader's first draw and required GPU readback after registration.</summary>
    private double ObserveHzb(BinaryShaderApiFixture assets, DynamicTexture2D depth, GpuFramebuffer target)
    {
        var program = Assert.IsType<LumOnHzbCopyShaderProgram>(assets.RegisteredPrograms["lumon_hzb_copy"]);
        long started = Stopwatch.GetTimestamp();
        target.BindWithViewport();
        GlStateCache.Current.UseProgram(program.ProgramId);
        program.PrimaryDepth = depth.TextureId;
        RenderFullscreenQuad(program.ProgramId);
        var pixel = ReadPixel(target, 4, 4);
        double elapsed = Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        Assert.InRange(pixel.R, .499f, .501f);
        GL.UseProgram(0);
        GlStateCache.Current.InvalidateAll();
        return elapsed;
    }
    #endregion
}