using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.Rendering;


using VanillaGraphicsExpanded.Tests.Support.Lighting;
using VanillaGraphicsExpanded.Tests.Support.GPU.Native;

namespace VanillaGraphicsExpanded.Tests.Component.Celestial;

/// <summary>Exercises the production explicit sky submission list without engine dispatch interception.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class SkyDrawTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{


    #region Public API
    #region Matched composition and pass contracts
    /// <summary>Four prepared passes produce identical surrogate pixels with one context and fewer actual native transitions.</summary>
    [Fact]
    public void PreparedPassesMatchSeparateBoundariesWithFewerCalls()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        // Act: capture the separate-boundary reference before measuring the shared route.
        scene.DrawStandalone();
        // Assert graphics state is restored before measuring the shared route.
        scene.AssertRestored();
        // Arrange: reinitialize before recording the standalone per-pass comparison route.
        scene.Initialize();
        List<string> separate;
        using (var capture = new GraphicsTransitionCapture())
        {
            // Act: prepare and submit each pass with a separate native boundary.
            scene.DrawStandalone();
            separate = [.. capture.Calls];
        }
        // Assert: compare the observed result with the expected contract.
        Assert.Single(scene.Contexts);
        scene.AssertRestored();
        using var expected = scene.CaptureOutput();
        scene.Initialize();
        List<string> shared;
        using (var capture = new GraphicsTransitionCapture())
        {
            // Act: submit the same passes through one shared native boundary.
            scene.DrawShared();
            shared = [.. capture.Calls];
        }
        Assert.Single(scene.Contexts);
        scene.AssertRestored();
        // Assert the shared output matches the separate-boundary reference and uses fewer transitions.
        using var actual = scene.CaptureOutput();
        for (int image = 0; image < 2; image++) ImageAssertions.Comparisons.CompareHistory(actual[image].Texture, expected[image]).AssertEqual();
        Assert.Equal(new[] { .5f, .4375f, .5625f, 1.375f }, actual[0].Texture.ReadPixelsRegion(0, 0, 1, 1));
        Assert.Equal(new[] { .25f, .21875f, .28125f, 1.375f }, actual[1].Texture.ReadPixelsRegion(0, 0, 1, 1));
        Assert.Equal(new[] { "prepare:sky0", "prepare:sky1", "prepare:sky2", "prepare:sky3",
            "draw:sky0", "draw:sky1", "draw:sky2", "draw:sky3" }, scene.Events);
        foreach (string prefix in new[] { "Framebuffer:", "Program:" })
            Assert.True(shared.Count(call => call.StartsWith(prefix)) < separate.Count(call => call.StartsWith(prefix)), prefix);
        // Attachment realization owns fixed routing; both warmed paths suppress redundant route writes.
        Assert.Equal(separate.Count(call=>call.StartsWith("Routing")),shared.Count(call=>call.StartsWith("Routing")));
        output.WriteLine("SINGLE_SKY_TRANSITIONS=" + System.Text.Json.JsonSerializer.Serialize(new { separate, shared }));
    }

    /// <summary>An empty optional pass list issues no graphics calls and retains no boundary authority.</summary>
    [Fact]
    public void EmptyPreparedListDoesNotEnterGraphics()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        using (var capture = new GraphicsTransitionCapture())
        {
            // Act: an empty prepared list must not enter graphics.
            SkyDraw.Run([]);
            // Assert no graphics transitions were issued.
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
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        // Act: execute the clear and shader pass through one shared context.
        SkyDraw.Run([new("clear", [], scene.DrawClear, error => throw error), scene.Passes[1].PrepareDraw()!]);
        // Assert the composed route uses one context and restores its boundary state.
        Assert.Single(scene.Contexts);
        scene.AssertRestored();
        Assert.Equal(new[] { .125f, .25f, .5f, 1f }, scene.ReadClearPixel());
    }
    #endregion

    #region Optional sources and failure recovery
    /// <summary>Disposed and skipped sources return no work without preventing other prepared passes from drawing.</summary>
    [Fact]
    public void SkippedAndDisposedSourcesLeaveOtherPassesViable()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.Passes[0].Dispose();
        scene.Passes[1].Skipped = true;
        // Act: run remaining prepared sources after disposing and skipping two entries.
        scene.DrawShared();
        // Assert the surviving passes remain viable and graphics authority is restored.
        Assert.Equal(new[] { 2, 3 }, scene.Rendered);
        Assert.Single(scene.Contexts);
        Assert.All(scene.Passes, source => Assert.Empty(source.Failures));
        scene.AssertRestored();
    }

    /// <summary>An invalid executable rejects only its own footprint before the remaining shared group enters graphics.</summary>
    [Fact]
    public void InvalidExecutableDoesNotRejectOtherPasses()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.InvalidateProgram(0);
        // Act: run the shared group with one invalid executable.
        scene.DrawShared();
        // Assert the invalid source is isolated from the remaining prepared passes.
        Assert.Single(scene.Passes[0].Failures);
        Assert.Equal(new[] { 1, 2, 3 }, scene.Rendered);
        Assert.All(scene.Passes.Skip(1), source => Assert.Empty(source.Failures));
        scene.AssertRestored();
    }

    /// <summary>A draw failure releases its active pass before later passes and permits explicit source recovery.</summary>
    [Fact]
    public void FailedActivePassIsIsolatedAndCanRecover()
    {
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        scene.FailAt = 1;
        // Act: fail the selected active pass while later passes continue.
        scene.DrawShared();
        // Assert the failed pass is isolated and its peers complete.
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
        // Arrange: establish this scenario's controlled inputs and starting state.

        EnsureContextValid();
        using var scene = new SkyDrawScene();
        scene.Initialize();
        var failure = new EngineBoundaryRestoreException("Injected unsafe handoff.", new ArithmeticException());
        // Act and assert: fatal boundary restoration errors escape recovery, then a later shared draw succeeds.
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
    #region Private
    /// <summary>Prepares this suite's declared observation programs before subject execution.</summary>
    private protected override void PrepareObservations(VanillaGraphicsExpanded.Tests.Support.GPU.Observations.GpuObservationPrograms programs)
    {
        base.PrepareObservations(programs);
        programs.Prepare("compare", 0, 0);
    }
    #endregion
}
