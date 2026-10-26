using System;
using System.Text.Json;
using System.Runtime.InteropServices;
using Moq;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using Vintagestory.API.Client;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Compares five real sky executables and their distinct generated bindings under separate boundaries and one explicit shared submission.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ProductionSkyDrawTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>The actual opaque callback orders all four pass slots and submits their real five-program MRT work once.</summary>
    [Fact]
    public void ActualSkyRendererSchedulesAllPreparedPassesInOneBoundary()
    {
        EnsureContextValid();
        using var scene = new ProductionSkyDrawScene(1);
        scene.Initialize();
        scene.Draw(false);
        scene.AssertRestored();
        var expected = scene.Read();
        scene.Initialize();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        using var renderer = new SkyRenderer(api.Object, null!);
        using (var preparation = new SkyPassPreparationScope(scene.PrepareDraws()))
        {
            renderer.OnRenderFrame(.125f, EnumRenderStage.Opaque);
            Assert.Equal(new[] { "CelestialDrawPass", "MoonDrawPass", "SunDrawPass", "AtmosphereSkyPass" }, preparation.Calls);
        }
        Assert.Single(scene.Contexts);
        Assert.Equal(new[] { "CelestialGalaxyProgram", "CelestialStarProgram", "MoonProgram", "SunProgram", "AtmosphereSkyShaderProgram" }, scene.Draws);
        scene.AssertRestored();
        var actual = scene.Read();
        for (int image = 0; image < 2; image++)
            Assert.True(MemoryMarshal.AsBytes(expected[image].AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual[image].AsSpan())));
    }

    /// <summary>Actual simultaneous sky/glow results remain bit-exact while repeated native and fixed-state handoffs decrease.</summary>
    [Theory]
    [InlineData(1f)]
    [InlineData(.6f)]
    public void FiveProductionProgramsPreserveMrtAndSeparateInputs(float variation)
    {
        EnsureContextValid();
        using var scene = new ProductionSkyDrawScene(variation);
        scene.Initialize();
        scene.ReadPrefixes = true;
        scene.Draw(false);
        Assert.Equal(5, scene.PrefixImages.Count);
        // Independently demonstrate each executable changes its output before comparing the two scheduling paths.
        Assert.Contains(scene.PrefixImages[0][0], value => value > 0);
        for (int index = 1; index < 5; index++)
            Assert.False(scene.PrefixImages[index - 1][0].SequenceEqual(scene.PrefixImages[index][0]), "Inactive program index " + index);
        Assert.Contains(scene.PrefixImages[3][1].Where((_, index) => index % 4 == 1), value => value > 0);
        scene.AssertRestored();
        scene.ReadPrefixes = false;
        scene.Initialize();
#if DEBUG
        long start = StateCache.Current.FixedFunctionCalls;
#endif
        List<string> standalone;
        using (var capture = new GraphicsTransitionCapture())
        {
            scene.Draw(false);
            standalone = [.. capture.Calls];
        }
#if DEBUG
        long standaloneFixed = StateCache.Current.FixedFunctionCalls - start;
#endif
        Assert.Equal(4, scene.Contexts.Count);
        scene.AssertRestored();
        float[][] expected = scene.Read();
        scene.Initialize();
#if DEBUG
        start = StateCache.Current.FixedFunctionCalls;
#endif
        List<string> shared;
        using (var capture = new GraphicsTransitionCapture())
        {
            scene.Draw(true);
            shared = [.. capture.Calls];
        }
#if DEBUG
        long sharedFixed = StateCache.Current.FixedFunctionCalls - start;
#endif
        Assert.Single(scene.Contexts);
        scene.AssertRestored();
        Assert.Equal(new[] { "CelestialGalaxyProgram", "CelestialStarProgram", "MoonProgram", "SunProgram", "AtmosphereSkyShaderProgram" }, scene.Draws);
        float[][] actual = scene.Read();
        for (int image = 0; image < 2; image++)
        {
            Assert.Equal(expected[image], actual[image]);
            Assert.True(MemoryMarshal.AsBytes(expected[image].AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(actual[image].AsSpan())));
            Assert.All(actual[image], value => Assert.True(float.IsFinite(value)));
        }
#if DEBUG
        Assert.True(sharedFixed < standaloneFixed, $"Fixed setters: {sharedFixed} vs {standaloneFixed}");
#endif
        foreach (string prefix in new[] { "Framebuffer:", "Program:" })
            Assert.True(shared.Count(call => call.StartsWith(prefix)) < standalone.Count(call => call.StartsWith(prefix)), prefix);
        // This primary-like target already has the exact MRT route. Neither scheduling path should rewrite it.
        Assert.DoesNotContain(standalone, call => call.StartsWith("Routing"));
        Assert.DoesNotContain(shared, call => call.StartsWith("Routing"));
        output.WriteLine("PRODUCTION_SKY_TRANSITIONS=" + JsonSerializer.Serialize(new
        {
            variation,
#if DEBUG
            standaloneFixed,
            sharedFixed,
#endif
            standalone,
            shared,
            colorPixels = actual[0].Length / 4,
            bitExactMrt = true
        }));
    }
    #endregion
}
