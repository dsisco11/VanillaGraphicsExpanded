using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises the production explicit sky submission list without engine dispatch interception.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SkyDrawTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    #region Matched composition and pass contracts
    /// <summary>Four prepared passes produce identical surrogate pixels with one context and fewer actual native transitions.</summary>
    [Fact]
    public void PreparedPassesMatchSeparateBoundariesWithFewerCalls()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.DrawStandalone();
        scene.AssertRestored();
        scene.Initialize();
        List<string> separate;
        using (var capture = new GraphicsTransitionCapture())
        {
            scene.DrawStandalone();
            separate = [.. capture.Calls];
        }
        Assert.Equal(4, scene.Contexts.Count);
        scene.AssertRestored();
        var expected = scene.ReadOutput();
        scene.Initialize();
        List<string> shared;
        using (var capture = new GraphicsTransitionCapture())
        {
            scene.DrawShared();
            shared = [.. capture.Calls];
        }
        Assert.Single(scene.Contexts);
        scene.AssertRestored();
        var actual = scene.ReadOutput();
        for (int image = 0; image < 2; image++) Assert.Equal(expected[image], actual[image]);
        Assert.Equal(new[] { .5f, .4375f, .5625f, 1.375f }, actual[0][..4]);
        Assert.Equal(new[] { .25f, .21875f, .28125f, 1.375f }, actual[1][..4]);
        Assert.Equal(new[] { "prepare:sky0", "prepare:sky1", "prepare:sky2", "prepare:sky3",
            "draw:sky0", "draw:sky1", "draw:sky2", "draw:sky3" }, scene.Events);
        foreach (string prefix in new[] { "Framebuffer:", "Program:", "Routing" })
            Assert.True(shared.Count(call => call.StartsWith(prefix)) < separate.Count(call => call.StartsWith(prefix)), prefix);
        output.WriteLine("SINGLE_SKY_TRANSITIONS=" + System.Text.Json.JsonSerializer.Serialize(new { separate, shared }));
    }

    /// <summary>An empty optional pass list issues no graphics calls and retains no boundary authority.</summary>
    [Fact]
    public void EmptyPreparedListDoesNotEnterGraphics()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        using (var capture = new GraphicsTransitionCapture())
        {
            SkyDraw.Run([]);
            Assert.Empty(capture.Calls);
        }
        scene.AssertRestored();
        scene.DrawShared();
        Assert.Equal(new[] { 0, 1, 2, 3 }, scene.Rendered);
    }

    /// <summary>A clear-only pass and a shader destination can share the explicit prepared-work list.</summary>
    [Fact]
    public void ClearAndShaderPassesShareOneContext()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        SkyDraw.Run([new("clear", [], scene.DrawClear, error => throw error), scene.Passes[1].PrepareDraw()!]);
        Assert.Single(scene.Contexts);
        scene.AssertRestored();
        Assert.Equal(new[] { .125f, .25f, .5f, 1f }, scene.ReadClear()[..4]);
    }
    #endregion

    #region Optional sources and failure recovery
    /// <summary>Disposed and skipped sources return no work without preventing other prepared passes from drawing.</summary>
    [Fact]
    public void SkippedAndDisposedSourcesLeaveOtherPassesViable()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.Passes[0].Dispose();
        scene.Passes[1].Skipped = true;
        scene.DrawShared();
        Assert.Equal(new[] { 2, 3 }, scene.Rendered);
        Assert.Single(scene.Contexts);
        Assert.All(scene.Passes, source => Assert.Empty(source.Failures));
        scene.AssertRestored();
    }

    /// <summary>An invalid executable rejects only its own footprint before the remaining shared group enters graphics.</summary>
    [Fact]
    public void InvalidExecutableDoesNotRejectOtherPasses()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.InvalidateProgram(0);
        scene.DrawShared();
        Assert.Single(scene.Passes[0].Failures);
        Assert.Equal(new[] { 1, 2, 3 }, scene.Rendered);
        Assert.All(scene.Passes.Skip(1), source => Assert.Empty(source.Failures));
        scene.AssertRestored();
    }

    /// <summary>A draw failure releases its active pass before later passes and permits explicit source recovery.</summary>
    [Fact]
    public void FailedActivePassIsIsolatedAndCanRecover()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.FailAt = 1;
        scene.DrawShared();
        Assert.Equal(new[] { 0, 2, 3 }, scene.Rendered);
        Assert.IsType<ArithmeticException>(Assert.Single(scene.Passes[1].Failures));
        Assert.All(scene.Passes.Where((_, index) => index != 1), source => Assert.Empty(source.Failures));
        scene.AssertRestored();
        scene.Initialize();
        scene.Passes[1].Recover();
        scene.FailAt = -1;
        scene.DrawShared();
        Assert.Equal(new[] { 0, 1, 2, 3 }, scene.Rendered);
        scene.AssertRestored();
        scene.AssertMutable();
    }

    /// <summary>Unsafe handoff errors escape optional-pass recovery and leave no ambient submission scope behind.</summary>
    [Fact]
    public void FatalDrawFailurePropagatesAndReleasesGraphicsAuthority()
    {
        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        var failure = new EngineBoundaryRestoreException("Injected unsafe handoff.", new ArithmeticException());
        Assert.Same(failure, Assert.Throws<EngineBoundaryRestoreException>(() => SkyDraw.Run([
            new("fatal", [], commands => { scene.DrawClear(commands); throw failure; }, error => throw new Exception("Must not recover fatal handoff", error)),
            scene.Passes[1].PrepareDraw()!])));
        Assert.Empty(scene.Rendered);
        Assert.All(scene.Passes, source => Assert.Empty(source.Failures));
        scene.AssertRestored();
        scene.Initialize();
        scene.DrawShared();
        Assert.Equal(new[] { 0, 1, 2, 3 }, scene.Rendered);
        scene.AssertRestored();
        scene.AssertMutable();
    }
    #endregion
    #endregion
}
