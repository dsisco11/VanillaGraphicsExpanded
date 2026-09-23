using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies the migrated trace harness actually borrows and retains its supplied branch resources.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class NearFieldResourceReuseTests : NearFieldShaderTestBase
{
    /// <summary>Uses the shared GPU context and real component shader owners.</summary>
    public NearFieldResourceReuseTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Trace ownership
    /// <summary>Repeated real draws retain buffers and do not overwrite a simultaneous branch or retire external world resources.</summary>
    [Fact]
    public void TraceReusesSuppliedBranchWithoutTakingOwnershipOfExternalInputs()
    {
        EnsureShaderTestAvailable();
        using var geometry = new NearFieldVoxelFixture();
        using var first = new ShaderLightingResources(Programs.Api);
        using var second = new ShaderLightingResources(Programs.Api);
        using var external = new ShaderLightingResources(Programs.Api);
        var world = external.EnsureWorldProbes(1, 1, 16);
        var screen = first.EnsureScreen(4, 4, 2);
        var other = second.EnsureScreen(4, 4, 2);
        other.ScreenProbeAtlasTraceTex!.UploadDataImmediate(Enumerable.Repeat(.75f, 16 * 16 * 4).ToArray());
        var output = screen.ScreenProbeAtlasTraceFbo!;
        var history = screen.ScreenProbeAtlasHistoryTex!;
        var radiance = output[0];
        first.Scene.EnsureSize(4, 4);
        var depth = first.Scene.Engine.Depth;
        float[]? prior = null;
        for (int frame = 0; frame < 2; frame++)
        {
            var result = Trace(geometry, nearFieldTracing: false, worldCache: false,
                worldResources: world, resources: first,
                consume: traced => Assert.Same(output, traced));
            Assert.Same(screen, first.EnsureScreen(4, 4, 2));
            Assert.Same(radiance, screen.ScreenProbeAtlasTraceTex);
            Assert.Same(history, screen.ScreenProbeAtlasHistoryTex);
            Assert.Same(depth, first.Scene.Engine.Depth);
            Assert.True(radiance.IsValid);
            Assert.All(other.ScreenProbeAtlasTraceTex.ReadPixels(), value => Assert.Equal(.75f, value));
            if (prior != null) Assert.Equal(prior, result.Radiance);
            prior = result.Radiance;
        }
        first.Dispose();
        Assert.False(radiance.IsValid);
        Assert.True(world.ProbeRadianceAtlas.IsValid);
        Assert.True(other.ScreenProbeAtlasTraceTex.IsValid);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
