using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;
using VanillaGraphicsExpanded.Tests.Component.LumOn.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks actual sixteen-block GPU publication and identity ownership independently of lighting.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    #region Public API
    /// <summary>Published voxels outside the configured origin domain cannot masquerade as supported traces.</summary>
    [Fact]
    public void SupportedOriginDomain_RejectsOutsideProbeOrigins()
    {
        EnsureShaderTestAvailable();
        // Arrange: Publish valid voxel data, then define one admitted and one disjoint origin domain.
        using var fixture = new NearFieldVoxelFixture();
        fixture.Publish(new VanillaGraphicsExpanded.Tests.Support.LumOn.WorldProbes.Fixtures.ControlledVoxelWorld());
        var allowed = new PartitionBounds(new(-1, -1, -6), new(1, 1, -4));
        var excluded = new PartitionBounds(new(8, 8, 8), new(10, 10, 10));

        // Act and assert: GPU traces inside the domain light, while a disjoint origin remains unresolved.
        Assert.True(TraceRed(Trace(fixture, supportedOrigins: allowed, maximumTraceReach: 32)) > 9);
        AssertUnresolved(Trace(fixture, supportedOrigins: excluded, maximumTraceReach: 32));
    }

    #endregion
}
