using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using Moq;
using VanillaGraphicsExpanded.PBR.Atmosphere;
using VanillaGraphicsExpanded.PBR.Atmosphere.Celestial;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies the single engine-facing sky owner composes passes without exposing independent engine callbacks.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class SkyRendererTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Exactly one preparation callback and one opaque callback own every sky component and retire together.</summary>
    [Fact]
    public void SingleOwnerRegistersAndRetiresAllPasses()
    {
        EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>();
        api.SetupGet(value => value.Event).Returns(events.Object);
        Assert.False(SkyRenderer.IsEnabled);
        using (var renderer = new SkyRenderer(api.Object, null!))
        {
            Assert.True(SkyRenderer.IsEnabled);
            Assert.True(CelestialDrawPass.IsEnabled);
            Assert.True(MoonDrawPass.IsEnabled);
            Assert.True(SunDrawPass.IsEnabled);
            Assert.Equal(.19, renderer.RenderOrder);
            Assert.Equal(1, renderer.RenderRange);
            events.Verify(value => value.RegisterRenderer(renderer, EnumRenderStage.Before, "vge_sky_prepare"), Times.Once);
            events.Verify(value => value.RegisterRenderer(renderer, EnumRenderStage.Opaque, "vge_sky"), Times.Once);
            Assert.Equal(2, events.Invocations.Count(call => call.Method.Name == nameof(IClientEventAPI.RegisterRenderer)));
            foreach (var pass in new[] { typeof(CelestialDrawPass), typeof(MoonDrawPass), typeof(SunDrawPass), typeof(AtmosphereSkyPass) })
                Assert.False(typeof(IRenderer).IsAssignableFrom(pass));
#if DEBUG
            long before = OpenGlStateCache.Current.DrawSubmissions;
#endif
            renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
            renderer.OnRenderFrame(0, EnumRenderStage.OIT);
#if DEBUG
            Assert.Equal(before, OpenGlStateCache.Current.DrawSubmissions);
#endif
            renderer.Dispose();
            renderer.OnRenderFrame(0, EnumRenderStage.Before);
            renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
#if DEBUG
            Assert.Equal(before, OpenGlStateCache.Current.DrawSubmissions);
#endif
            Assert.False(renderer.PrepareFrame());
        }
        Assert.False(SkyRenderer.IsEnabled);
        Assert.False(CelestialDrawPass.IsEnabled);
        Assert.False(MoonDrawPass.IsEnabled);
        Assert.False(SunDrawPass.IsEnabled);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before), Times.Once);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Opaque), Times.Once);
    }

    /// <summary>Partial engine registration failure withdraws every independently created pass and both callback slots.</summary>
    [Fact]
    public void RegistrationFailureReleasesComposedOwnership()
    {
        EnsureContextValid();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        var events = new Mock<IClientEventAPI>();
        api.SetupGet(value => value.Event).Returns(events.Object);
        var failure = new ArithmeticException("Injected registration failure.");
        events.Setup(value => value.RegisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Opaque, "vge_sky")).Throws(failure);
        Assert.Same(failure, Assert.Throws<ArithmeticException>(() => new SkyRenderer(api.Object, null!)));
        Assert.False(SkyRenderer.IsEnabled);
        Assert.False(CelestialDrawPass.IsEnabled);
        Assert.False(MoonDrawPass.IsEnabled);
        Assert.False(SunDrawPass.IsEnabled);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Before), Times.Once);
        events.Verify(value => value.UnregisterRenderer(It.IsAny<IRenderer>(), EnumRenderStage.Opaque), Times.Once);
    }
    #endregion
}
