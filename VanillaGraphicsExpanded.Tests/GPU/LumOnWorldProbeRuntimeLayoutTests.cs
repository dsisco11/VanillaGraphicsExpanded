using Moq;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies debug placement is published independently of surface-cache lighting readiness.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class LumOnWorldProbeRuntimeLayoutTests : LumOnShaderFunctionalTestBase
{
    #region Construction
    /// <summary>Uses the shared mandatory graphics context.</summary>
    public LumOnWorldProbeRuntimeLayoutTests(HeadlessGLFixture fixture) : base(fixture) { }
    #endregion

    #region Layout publication
    /// <summary>The registered updater exposes moving probe placement even without a lighting provider.</summary>
    [Fact]
    public void UpdateWithoutSurfaceLighting_PublishesCurrentRuntimeLayout()
    {
        EnsureShaderTestAvailable();
        using var assets = new BinaryShaderApiFixture();
        var events = new RuntimeRenderEvents();
        var config = new VgeConfig();
        config.LumOn.Enabled = true;
        config.WorldProbeClipmap.ClipmapResolution = 4;
        config.WorldProbeClipmap.ClipmapLevels = 1;
        config.WorldProbeClipmap.ClipmapBaseSpacing = 2;
        var world = new Mock<IClientWorldAccessor>(MockBehavior.Strict);
        var api = RuntimeEngineServices.Client(assets.Api, events.Api, world.Object,
            Mock.Of<IRenderAPI>(), Mock.Of<IShaderAPI>(), Mock.Of<IModLoader>());
        using var buffers = new LumOnWorldProbeClipmapBufferManager(api, config);
        double cameraX = 17.25;
        using var updater = new LumOnWorldProbeUpdateRenderer(api, config, buffers,
            () => new LumOnCameraState(cameraX, 32, 4, cameraX, 32, 4, 0));
        foreach (double nextX in new[] { 17.25, 49.25 })
        {
            cameraX = nextX;
            events.Render(EnumRenderStage.Done);
            Assert.True(buffers.TryGetRuntimeParams(out var camera, out _, out float spacing,
                out int levels, out int resolution, out var origins, out _));
            Assert.Equal(nextX, camera.X);
            Assert.Equal(2f, spacing);
            Assert.Equal(1, levels);
            Assert.Equal(4, resolution);
            Assert.InRange(origins[0].X, -8f, 0f);
        }
        // A strict world mock rejects any trace access while surface lighting remains unavailable.
        world.VerifyNoOtherCalls();
    }
    #endregion
}
