using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks retained resource identity, temporal isolation and lifetime through production owners.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderLightingResourcesTests : RenderTestBase
{
    /// <summary>Uses the shared mandatory OpenGL context.</summary>
    public ShaderLightingResourcesTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Branch reuse
    /// <summary>Repeated uploads retain every screen allocation; swapping one history cannot alter an equal-sized branch.</summary>
    [Fact]
    public void RepeatedPopulationRetainsAllocationsAndIndependentHistory()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var first = new ShaderLightingResources(assets.Api);
        using var second = new ShaderLightingResources(assets.Api);
        var screen = first.EnsureScreen(4, 4, 2);
        var independent = second.EnsureScreen(4, 4, 2);
        var original = BorrowScreen(screen);
        var other = BorrowScreen(independent);
        var framebuffers = BorrowFramebuffers(screen);
        var world = first.EnsureWorldProbes(1, 1, 8);
        var worldTextures = BorrowWorld(world);
        var otherWorld = second.EnsureWorldProbes(1, 1, 8);
        var independentHistory = independent.ScreenProbeAtlasHistoryTex!;
        independentHistory.UploadDataImmediate(Enumerable.Repeat(.75f, 16 * 16 * 4).ToArray());
        long revision = screen.HistoryRevision;
        for (int frame = 0; frame < 3; frame++)
        {
            Assert.Same(screen, first.EnsureScreen(4, 4, 2));
            Assert.Same(world, first.EnsureWorldProbes(1, 1, 8));
            screen.ScreenProbeAtlasCurrentTex!.UploadDataImmediate(Enumerable.Repeat(frame * .25f, 16 * 16 * 4).ToArray());
            screen.SwapRadianceBuffers();
            Assert.Equal(revision, screen.HistoryRevision);
            Assert.Equal(frame * .25f, screen.ScreenProbeAtlasHistoryTex!.ReadPixels()[0]);
            // Ping-pong changes roles, not allocation identity or the independent branch's generation.
            Assert.Equal(original.Select(t => t.TextureId).Order(), BorrowScreen(screen).Select(t => t.TextureId).Order());
            Assert.All(BorrowScreen(screen), texture => Assert.DoesNotContain(texture, other));
            Assert.Equal(framebuffers.Select(f => f.FboId).Order(), BorrowFramebuffers(screen).Select(f => f.FboId).Order());
            Assert.Same(independentHistory, independent.ScreenProbeAtlasHistoryTex);
            Assert.All(independentHistory.ReadPixels(), value => Assert.Equal(.75f, value));
            Assert.All(worldTextures, texture => Assert.True(texture.IsValid));
            Assert.NotEqual(world.ProbeRadianceAtlas.TextureId, otherWorld.ProbeRadianceAtlas.TextureId);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Resize, spacing-only recreation and world topology changes retire only their own resource family.</summary>
    [Fact]
    public void RecreationAndDisposalRespectBranchOwnership()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var branch = new ShaderLightingResources(assets.Api);
        using var independent = new ShaderLightingResources(assets.Api);
        var screen = branch.EnsureScreen(4, 4, 2);
        var other = BorrowScreen(independent.EnsureScreen(4, 4, 2));
        branch.Scene.EnsureSize(4, 4);
        var engineDepth = branch.Scene.Engine.Depth;
        var world = branch.EnsureWorldProbes(1, 1, 8);
        var retired = BorrowScreen(screen);
        var retiredFbos = BorrowFramebuffers(screen);
        Assert.Same(screen, branch.EnsureScreen(8, 6, 2));
        Assert.All(retired, texture => Assert.False(texture.IsValid));
        Assert.All(retiredFbos, fbo => Assert.False(fbo.IsValid));
        Assert.Same(world, branch.EnsureWorldProbes(1, 1, 8));
        Assert.True(engineDepth.IsValid);
        retired = BorrowScreen(screen);
        long revision = screen.HistoryRevision;
        branch.EnsureScreen(8, 6, 1);
        Assert.Equal((8, 6), (screen.ProbeCountX, screen.ProbeCountY));
        Assert.True(screen.HistoryRevision > revision);
        Assert.All(retired, texture => Assert.False(texture.IsValid));
        var screenBeforeWorldChange = BorrowScreen(screen);
        var oldWorld = BorrowWorld(world);
        var replacement = branch.EnsureWorldProbes(2, 2, 16);
        Assert.All(oldWorld, texture => Assert.False(texture.IsValid));
        Assert.All(screenBeforeWorldChange, texture => Assert.True(texture.IsValid));
        Assert.Throws<ArgumentOutOfRangeException>(() => branch.EnsureWorldProbes(0, 1, 8));
        Assert.Same(replacement, branch.EnsureWorldProbes(2, 2, 16));
        branch.Scene.EnsureSize(8, 6);
        Assert.False(engineDepth.IsValid);
        var currentDepth = branch.Scene.Engine.Depth;
        branch.Dispose(); branch.Dispose();
        Assert.All(screenBeforeWorldChange, texture => Assert.False(texture.IsValid));
        Assert.All(BorrowWorld(replacement), texture => Assert.False(texture.IsValid));
        Assert.False(currentDepth.IsValid);
        Assert.All(other, texture => Assert.True(texture.IsValid));
        Assert.Throws<ObjectDisposedException>(() => branch.EnsureScreen(4, 4, 2));
        Assert.Throws<ObjectDisposedException>(() => branch.EnsureWorldProbes(1, 1, 8));
        Assert.Throws<ObjectDisposedException>(() => branch.Scene.EnsureSize(4, 4));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Borrowed resource observations
    /// <summary>Observes production attachment identities without repeating their formats or allocation rules.</summary>
    private static GpuTexture[] BorrowScreen(LumOnBufferManager screen) =>
    [
        screen.ProbeAnchorPositionTex!, screen.ProbeAnchorNormalTex!, screen.ProbeTraceMaskTex!, screen.ProbePisEnergyTex!,
        screen.ScreenProbeAtlasTraceTex!, screen.ScreenProbeAtlasMetaTraceTex!,
        screen.ScreenProbeAtlasCurrentTex!, screen.ScreenProbeAtlasMetaCurrentTex!,
        screen.ScreenProbeAtlasHistoryTex!, screen.ScreenProbeAtlasMetaHistoryTex!,
        screen.ScreenProbeAtlasFilteredTex!, screen.ScreenProbeAtlasMetaFilteredTex!,
        screen.ProbeSh9Tex0!, screen.ProbeSh9Tex1!, screen.ProbeSh9Tex2!, screen.ProbeSh9Tex3!,
        screen.ProbeSh9Tex4!, screen.ProbeSh9Tex5!, screen.ProbeSh9Tex6!,
        screen.IndirectHalfTex!, screen.IndirectFullTex!, screen.SurfaceAlbedoTex!, screen.VelocityTex!, screen.HzbDepthTex!
    ];

    /// <summary>Observes framebuffers with stable roles; the private history framebuffer participates in ping-pong separately.</summary>
    private static GpuFramebuffer[] BorrowFramebuffers(LumOnBufferManager screen) =>
    [
        screen.ProbeAnchorFbo!, screen.ProbeTraceMaskFbo!, screen.ScreenProbeAtlasTraceFbo!,
        screen.ScreenProbeAtlasFilteredFbo!, screen.ProbeSh9Fbo!, screen.IndirectHalfFbo!,
        screen.IndirectFullFbo!, screen.SurfaceAlbedoFbo!, screen.VelocityFbo!, screen.HzbFbo!
    ];

    /// <summary>Observes the complete world-probe texture family owned by the production topology.</summary>
    private static GpuTexture[] BorrowWorld(LumOnWorldProbeClipmapGpuResources world) =>
        [world.ProbeRadianceAtlas, world.ProbeVis0, world.ProbeDist0, world.ProbeMeta0, world.ProbeDebugState0];
    #endregion
}
