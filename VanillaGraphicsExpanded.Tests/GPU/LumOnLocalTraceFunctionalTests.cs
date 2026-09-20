using System.Numerics;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn.Shaders;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.Fixtures.WorldProbes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises production local geometry, lighting and cache handoff through actual screen-probe rays.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed partial class LumOnLocalTraceFunctionalTests : LumOnShaderFunctionalTestBase
{
    /// <summary>Shares the headless GPU context.</summary>
    public LumOnLocalTraceFunctionalTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Shader Variants
    /// <summary>Production local tracing compiles alongside importance selection, with and without a world cache.</summary>
    [Theory]
    [InlineData("0")]
    [InlineData("1")]
    public void LocalTrace_CompilesWithImportanceSelection(string worldCache)
    {
        EnsureShaderTestAvailable();
        int program = CompileShaderWithDefines("lumon_probe_atlas_trace.vsh", "lumon_probe_atlas_trace.fsh",
            new Dictionary<string, string?>
            {
                ["VGE_LUMON_LOCAL_TRACE_ENABLED"] = "1",
                ["VGE_LUMON_PROBE_PIS_ENABLED"] = "1",
                ["VGE_LUMON_WORLDPROBE_ENABLED"] = worldCache,
                ["VGE_LUMON_BIND_WORLDPROBE_RADIANCE_ATLAS"] = "1"
            });
        GL.DeleteProgram(program);
    }
    #endregion

    #region Scenarios
    /// <summary>A sealed room must resolve all ray directions locally even with a bright distant cache.</summary>
    [Theory]
    [InlineData(0f)]
    [InlineData(0.25f)]
    public void SealedRoom_UsesOutsideCellLighting(float lighting)
    {
        EnsureShaderTestAvailable();
        var world = new ControlledVoxelWorld { DefaultLight = Vector4.One };
        world.AddRoom((-3, -3, -8), (3, 3, -2));
        world.FillLight((-2, -2, -7), (2, 2, -3), new Vector4(lighting, lighting, lighting, 0));
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(world);
        var result = Trace(fixture);
        var suppressed = Trace(fixture, suppress: true);
        Assert.Equal(result.Meta, suppressed.Meta);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            for (int c = 0; c < 3; c++)
            {
                Assert.InRange(result.Radiance[i + c], lighting - 0.002f, lighting + 0.002f);
                Assert.Equal(result.Radiance[i + c], suppressed.Radiance[i + c]);
            }
            Assert.Equal(1f, result.Meta[i / 2]);
            Assert.Equal(0u, Flags(result.Meta[i / 2 + 1]) & (1u << 5));
        }
    }

    /// <summary>Unpublished data, unsupported cells and exhausted traversal never authorize cache or sky.</summary>
    [Theory]
    [InlineData(false, 256, false)]
    [InlineData(true, 1, false)]
    [InlineData(true, 256, true)]
    public void UnresolvedScene_RemainsDark(bool publish, int budget, bool unsupported)
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        if (publish) fixture.Publish(new ControlledVoxelWorld { IsLoaded = _ => !unsupported });
        var result = Trace(fixture, budget: budget);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.Equal(0f, result.Radiance[i]);
            Assert.Equal(0f, result.Meta[i / 2]);
            Assert.Equal(0u, Flags(result.Meta[i / 2 + 1]) & ((1u << 5) | (1u << 1)));
        }
    }

    /// <summary>A fully known clear segment admits distant cache lighting and suppression affects only that lighting.</summary>
    [Fact]
    public void ClearScene_UsesDistantCache()
    {
        EnsureShaderTestAvailable();
        using var fixture = new LocalTraceVoxelFixture();
        fixture.Publish(new ControlledVoxelWorld());
        var result = Trace(fixture);
        var suppressed = Trace(fixture, suppress: true);
        Assert.Equal(result.Meta, suppressed.Meta);
        for (int i = 0; i < result.Radiance.Length; i += 4)
        {
            Assert.InRange(result.Radiance[i], 9.9f, 10.1f);
            Assert.Equal(0f, suppressed.Radiance[i]);
            Assert.Equal(1f, result.Meta[i / 2]);
            Assert.NotEqual(0u, Flags(result.Meta[i / 2 + 1]) & (1u << 5));
        }
    }
    #endregion

}
