using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Scene.Geometry;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.WorldPartition;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks actual sixteen-block GPU publication and identity ownership independently of lighting.</summary>
public sealed partial class LumOnNearFieldFunctionalTests
{
    /// <summary>Published voxels outside the configured origin domain cannot masquerade as supported traces.</summary>
    [Fact]
    public void SupportedOriginDomain_RejectsOutsideProbeOrigins()
    {
        EnsureShaderTestAvailable();
        using var fixture = new Fixtures.NearFieldVoxelFixture();
        fixture.Publish(new VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes.ControlledVoxelWorld());
        var allowed = new PartitionBounds(new(-1, -1, -6), new(1, 1, -4));
        Assert.True(Trace(fixture, supportedOrigins: allowed, maximumTraceReach: 32).Radiance[0] > 9);
        var excluded = new PartitionBounds(new(8, 8, 8), new(10, 10, 10));
        AssertUnresolved(Trace(fixture, supportedOrigins: excluded, maximumTraceReach: 32));
    }

}
