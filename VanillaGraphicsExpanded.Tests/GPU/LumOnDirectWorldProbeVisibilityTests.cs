using System.Numerics;
using VanillaGraphicsExpanded.Numerics;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks exact local occlusion in direct irradiance debug and both gather fallback consumers.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed partial class LumOnDirectWorldProbeVisibilityTests : DirectWorldProbeVisibilityTestBase
{
    /// <summary>Shares the mandatory GPU context.</summary>
    public LumOnDirectWorldProbeVisibilityTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Local Occlusion
    /// <summary>Exterior cached light enters only through a real doorway in the near-field geometry.</summary>
    [Theory]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(33, false)]
    [InlineData(-1, false)]
    [InlineData(-2, false)]
    [InlineData(31, true)]
    [InlineData(32, true)]
    [InlineData(33, true)]
    [InlineData(-1, true)]
    [InlineData(-2, true)]
    public void SealedRoom_RejectsExteriorLight_AndDoorwayRestoresIt(int consumer, bool doorway)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld();
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        if (doorway) world.SetBlock(0, 0, -2, null);
        using var local = new NearFieldVoxelFixture();
        local.Publish(world);
        var result = RenderDirectVisibility(CreateUniformCache(), local.Scene,
            new Vector3(0.5f, 0.5f, -5), new Vector3(-7.5f), 16, consumer);
        AssertLighting(result, consumer, doorway);
        if (doorway && consumer < 0)
        {
            var suppressed = RenderDirectVisibility(CreateUniformCache(), local.Scene,
                new Vector3(0.5f, 0.5f, -5), new Vector3(-7.5f), 16, consumer, suppress: true);
            for (int i = 0; i < result.Length; i += 4)
            {
                Assert.Equal(result[i + 3], suppressed[i + 3]);
                for (int c = 0; c < 3; c++) Assert.Equal(0, suppressed[i + c]);
            }
        }
    }

    /// <summary>Unknown geometry, exhausted traversal and missing near-field resources cannot establish visibility.</summary>
    [Theory]
    [InlineData(31, 0)]
    [InlineData(-1, 0)]
    [InlineData(-2, 0)]
    [InlineData(31, 1)]
    [InlineData(-1, 1)]
    [InlineData(-2, 1)]
    [InlineData(31, 2)]
    [InlineData(-1, 2)]
    [InlineData(-2, 2)]
    public void UnresolvedVisibility_RemainsDark(int consumer, int scenario)
    {
        EnsureShaderTestAvailable();
        using var local = new NearFieldVoxelFixture();
        if (scenario != 0) local.Publish(new ControlledVoxelWorld());
        var result = RenderDirectVisibility(CreateUniformCache(), scenario == 2 ? null : local.Scene,
            new Vector3(0.5f, 0.5f, -5), new Vector3(-7.5f), 16, consumer, budget: scenario == 1 ? 1 : 256);
        AssertLighting(result, consumer, false);
    }

    /// <summary>Near-field geometry establishes visibility even when nearest angular depth incorrectly lies in front of the receiver.</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(-2)]
    [InlineData(31)]
    public void ClearGeometry_IgnoresQuantizedVisibilityDepth(int consumer)
    {
        EnsureShaderTestAvailable();
        using var local = new NearFieldVoxelFixture();
        local.Publish(new ControlledVoxelWorld());
        var result = RenderDirectVisibility(CreateUniformCache(distance: 0.1f), local.Scene,
            new Vector3(0.5f, 0.5f, -5), new Vector3(-7.5f), 16, consumer);
        AssertLighting(result, consumer, true);
    }
    #endregion

    #region Cache and Assertions
    /// <summary>Creates a fully published constant radiance tile with independently controllable recorded distance.</summary>
    private static WorldProbeAtlasData CreateUniformCache(float distance = 64)
    {
        var atlas = new WorldProbeAtlasData(1, 16);
        for (int i = 0; i < atlas.Radiance.Length; i += 4)
        {
            atlas.Radiance[i] = atlas.Radiance[i + 1] = atlas.Radiance[i + 2] = 1;
            atlas.Radiance[i + 3] = MathF.Log(1 + distance);
        }
        atlas.Metadata[0] = 1;
        atlas.Visibility[0] = atlas.Visibility[1] = 0.5f;
        return atlas;
    }

    /// <summary>Checks actual irradiance and selected confidence, accounting for debug tone mapping.</summary>
    private static void AssertLighting(float[] result, int consumer, bool lit)
    {
        float expected = !lit ? 0 : consumer == 33 ? 1 : consumer >= 0 ? MathF.PI / (1 + MathF.PI) : MathF.PI;
        for (int i = 0; i < result.Length; i += 4)
        {
            for (int c = 0; c < 3; c++) Assert.InRange(result[i + c], expected - 0.004f, expected + 0.004f);
            if (consumer < 0) Assert.InRange(result[i + 3], lit ? 0.999f : 0, lit ? 1.001f : 0);
        }
    }
    #endregion
}
