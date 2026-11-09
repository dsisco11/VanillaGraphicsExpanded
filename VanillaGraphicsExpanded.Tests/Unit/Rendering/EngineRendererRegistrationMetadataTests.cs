using VanillaGraphicsExpanded.Rendering.Profiling;
using Vintagestory.API.Client;
using Vintagestory.Client.NoObf;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Verifies renderer registration metadata for action-backed handlers.</summary>
public sealed class EngineRendererRegistrationMetadataTests
{
    #region Public API
    /// <summary>Action renderers retain distinct engine registration names instead of collapsing to DummyRenderer.</summary>
    [Fact]
    public void RegistrationNamesDistinguishActionRenderers()
    {
        // Arrange: create two handlers that share a dummy renderer type but have separate engine names.
        var first = new RenderHandler { Renderer = new DummyRenderer { action = IgnoreFrame }, ProfilingName = "terrain" };
        var second = new RenderHandler { Renderer = new DummyRenderer { action = IgnoreFrame }, ProfilingName = "first-person" };
        // Act and assert: each handler returns its configured name and repeated lookup is stable.
        Assert.Equal("terrain", EngineRenderScopes.HandlerName(first));
        Assert.Equal("first-person", EngineRenderScopes.HandlerName(second));
        Assert.Same(EngineRenderScopes.HandlerName(first), EngineRenderScopes.HandlerName(first));
    }
    #endregion

    #region Private
    /// <summary>Supplies a named engine action without issuing rendering work.</summary>
    private static void IgnoreFrame(float dt) { }
    #endregion
}
