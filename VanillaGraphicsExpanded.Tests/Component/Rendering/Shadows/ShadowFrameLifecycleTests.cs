using Moq;
using VanillaGraphicsExpanded.Rendering.Integration.Shadows;
using VanillaGraphicsExpanded.Rendering.Shadows.Configuration;
using Vintagestory.API.Client;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Component.Rendering.Shadows;

/// <summary>Checks requested shadow-generation lifecycle through the renderer's registration and allocation boundary.</summary>
public sealed class ShadowFrameLifecycleTests
{
    #region Public API
    /// <summary>Disabled policy and failed aggregate preflight never invoke the generation factory.</summary>
    [Fact]
    public void DisabledAndUnderfundedPoliciesDoNotCreateGenerations()
    {
        // Arrange: capture the renderer registration and count only attempts to construct a generation.
        var events = new Mock<IClientEventAPI>();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.Event).Returns(events.Object);
        int creations = 0;
        using var renderer = new ShadowFrameRenderer(api.Object, _ =>
        {
            creations++;
            throw new InvalidOperationException("Factory must not run for these policies.");
        }, new ShadowSettings(), () => 0, () => null);

        // Act: disabled startup drains without allocation, then enabled settings fail the aggregate preflight.
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        var underfunded = new ShadowSettings();
        underfunded.Scheduling.Enabled = true;
        underfunded.Scheduling.MemoryMiB = 1;
        renderer.ApplyPolicy(underfunded);
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);

        // Assert: both policy decisions stop before the caller-owned generation factory boundary.
        Assert.Equal(0, creations);
        events.Verify(value => value.RegisterRenderer(renderer, EnumRenderStage.Opaque, "vge_shadow_readiness"), Times.Once);
    }

    /// <summary>Repeated identical requests do not retry a failed generation factory on each render callback.</summary>
    [Fact]
    public void IdenticalRequestedPolicyIsNotRecreatedAfterFactoryFailure()
    {
        // Arrange: enable one valid tiny policy and use a factory that records the single construction attempt.
        var events = new Mock<IClientEventAPI>();
        var api = new Mock<ICoreClientAPI> { DefaultValue = DefaultValue.Mock };
        api.SetupGet(value => value.Event).Returns(events.Object);
        int creations = 0;
        using var renderer = new ShadowFrameRenderer(api.Object, _ =>
        {
            creations++;
            throw new InvalidOperationException("Controlled generation-construction failure.");
        }, new ShadowSettings(), () => 0, () => null);
        var enabled = MinimalEnabledSettings();

        // Act: request the policy twice around the first failed construction, then render once more.
        renderer.ApplyPolicy(enabled);
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);
        renderer.ApplyPolicy(enabled);
        renderer.OnRenderFrame(0, EnumRenderStage.Opaque);

        // Assert: the unchanged immutable request is not treated as a new generation request.
        Assert.Equal(1, creations);
    }
    #endregion

    #region Private
    /// <summary>Builds the smallest aggregate layout that passes CPU preflight before the controlled factory.</summary>
    private static ShadowSettings MinimalEnabledSettings() => new()
    {
        Scheduling = new() { Enabled = true, MemoryMiB = 2, LightCapacity = 1, DemandRequests = 1,
            ProjectionHistoryCapacity = 1 },
        Inputs = new() { ChunkCapacity = 1, RangeCapacity = 1, ChangeCapacity = 1 },
        Candidates = new() { MemoryMiB = 1, HeaderCapacity = 1, SegmentsPerAssociation = 1,
            DedupCapacity = 128, ChangeCapacity = 1 },
        Pages = new() { MemoryMiB = 1, PageSize = 8, TileColumns = 1, TileRows = 1, ArrayLayers = 1 },
        Drawing = new() { MemoryMiB = 1, ViewCapacity = 1, PairCapacity = 128, CommandSlots = 1,
            ViewInstances = 1, RasterPixels = 1, EstimatedTriangles = 1, EstimatedRasterSamples = 1 },
        Temporal = new()
    };
    #endregion
}
