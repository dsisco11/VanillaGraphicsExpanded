using System.Reflection;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.LumOn.WorldProbes.Gpu;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.PBR.Liquids;
using VanillaGraphicsExpanded.PBR.Postprocessing;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks real migrated allocations and retained declarations without rebuilding their rendering algorithms.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class StaticTargetMigrationTests(HeadlessGLFixture fixture, ITestOutputHelper output) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Both production water-volume accumulators clear on every repeated pass instead of retaining an earlier frame.</summary>
    [Fact]
    public void WaterVolumeContractClearsBothStaticLayersEveryFrame()
    {
        EnsureContextValid();
        using var transport = Texture3D.Create(3, 2, 2, PixelInternalFormat.Rgba32f,
            textureTarget: TextureTarget.Texture2DArray);
        using var optical = GpuFramebufferAttachment.FromTexture(transport, layer: 0);
        using var illumination = GpuFramebufferAttachment.FromTexture(transport, layer: 1);
        using var target = StaticGpuFramebuffer.Create([optical, illumination]);
        var pass = new RenderPassDesc(target, LiquidPipelineStates.VolumeOutputs);
        Assert.All(pass.Colors, color => Assert.Equal(AttachmentLoad.Clear, color.Load));
        using var declarations = new StaticPassDeclarationCapture();
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        StateCache.Current.Invalidate(EPipelineState.ScissorTest | EPipelineState.ColorMask);
        for (int frame = 0; frame < 3; frame++)
        {
            // New nonzero data makes a missing clear observable independently each frame.
            GL.ClearTexImage(transport.TextureId, 0, PixelFormat.Rgba, PixelType.Float,
                new[] { frame + 1f, 2f, 3f, 4f });
            Assert.True(GraphicsCommandContext.TryRun("Tests.WaterVolume.Clear", [], true, commands =>
            {
                commands.BeginPass(pass);
                commands.EndPass();
            }));
            for (int layer = 0; layer < 2; layer++)
                Assert.All(LayeredTestTexture.Read(transport, layer), value => Assert.Equal(0, value));
        }
        declarations.AssertReusable();
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Typed metadata and views over external allocations cannot establish immutable storage ownership.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExternalTextureMetadataCannotBeFrozen(bool throughView)
    {
        EnsureContextValid();
        using var external = DynamicTexture3D.Create(2, 2, 3, PixelInternalFormat.Rgba32f,
            textureTarget: TextureTarget.Texture2DArray, immutableStorage: true);
        using var borrowed = new BorrowedTexture(external.TextureId);
        using var view = throughView ? GpuTextureView.CreateFromTexture(borrowed,
            TextureTarget.Texture2DArray, PixelInternalFormat.Rgba32f, 0, 1, 0, 3) : null;
        using var image = view is null ? GpuFramebufferAttachment.FromTexture(borrowed, layer: 0)
            : GpuFramebufferAttachment.FromTexture(view, layer: 0);
        Assert.Throws<ArgumentException>(() => StaticGpuFramebuffer.Create([image]));
        Assert.True(external.IsValid);
        Assert.True(external.Resize(3, 3, 3));
        borrowed.Dispose();
        Assert.True(GL.IsTexture(external.TextureId));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>All eleven screen targets retain immutable image mappings and both load contracts across temporal role rotation.</summary>
    [Fact]
    public void LumOnPhysicalTargetsKeepDeclarationsAcrossRotation()
    {
        EnsureContextValid();
        using var targets = new LumOnTargets(8, 6, 2, 2, 4, 3);
        var framebuffers = typeof(LumOnTargets).GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => typeof(GpuFramebuffer).IsAssignableFrom(property.PropertyType))
            .Select(property => Assert.IsType<StaticGpuFramebuffer>(property.GetValue(targets))).ToArray();
        Assert.Equal(11, framebuffers.Length);
        var clear = framebuffers.ToDictionary(target => target, target => targets.GetPass(target, true));
        foreach (var target in framebuffers)
        {
            Assert.Same(target.PreservePass, targets.GetPass(target, false));
            Assert.Same(clear[target], targets.GetPass(target, true));
            Assert.All(clear[target].Colors, color => Assert.Equal(AttachmentLoad.Clear, color.Load));
        }

        // Change roles only: the immutable target and its declarations stay paired with storage.
        var originalCurrent = targets.ScreenProbeAtlasCurrentFbo;
        var originalHistory = targets.ScreenProbeAtlasHistoryFbo;
        (targets.ScreenProbeAtlasCurrentFbo, targets.ScreenProbeAtlasHistoryFbo) =
            (targets.ScreenProbeAtlasHistoryFbo, targets.ScreenProbeAtlasCurrentFbo);
        (targets.ScreenProbeAtlasCurrentTex, targets.ScreenProbeAtlasHistoryTex) =
            (targets.ScreenProbeAtlasHistoryTex, targets.ScreenProbeAtlasCurrentTex);
        (targets.ScreenProbeAtlasMetaCurrentTex, targets.ScreenProbeAtlasMetaHistoryTex) =
            (targets.ScreenProbeAtlasMetaHistoryTex, targets.ScreenProbeAtlasMetaCurrentTex);
        Assert.Same(originalHistory, targets.ScreenProbeAtlasCurrentFbo);
        Assert.Same(originalCurrent, targets.ScreenProbeAtlasHistoryFbo);
        Assert.Same(targets.ScreenProbeAtlasCurrentTex, targets.ScreenProbeAtlasCurrentFbo[0]);
        Assert.Same(clear[(StaticGpuFramebuffer)originalHistory], targets.GetPass(targets.ScreenProbeAtlasCurrentFbo, true));

        // Clear loads obey native write masks and scissor; establish complete-image
        // writes before observing routing so prior hostile-state fixtures cannot clip them.
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        StateCache.Current.Invalidate(EPipelineState.ScissorTest | EPipelineState.ColorMask);
        AssertStaticSubmissionHasNoRoutingWork(framebuffers.Select(target => clear[target]).ToArray());
        foreach (var target in framebuffers)
            foreach (var image in target.AttachmentImages.Select(pair => pair.Value))
            {
                float[] pixels = image.Resource switch
                {
                    DynamicTexture2D texture => texture.ReadPixels(),
                    Texture3D texture => LayeredTestTexture.Read(texture, image.Layer!.Value),
                    _ => throw new InvalidOperationException("Unexpected LumOn attachment storage.")
                };
                Assert.All(pixels, value => Assert.Equal(0, value));
            }
    }

    /// <summary>Both probe-atlas targets and direct/postprocess owners freeze their complete backing images until retirement.</summary>
    [Fact]
    public void WorldDirectAndPostprocessOwnersUseStaticDeclarations()
    {
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        using var probes = new LumOnWorldProbeClipmapGpuResources(assets.Api, 2, 1, 2);
        using var direct = new DirectLightingTargets(4, 3);
        using var post = new PostprocessImage(3, 2, "Tests.StaticPostprocess");
        var targets = new[]
        {
            Assert.IsType<StaticGpuFramebuffer>(probes.GetFbo()),
            Assert.IsType<StaticGpuFramebuffer>(probes.GetRadianceFbo()),
            Assert.IsType<StaticGpuFramebuffer>(direct.Framebuffer),
            Assert.IsType<StaticGpuFramebuffer>(post.Target)
        };
        Assert.Throws<InvalidOperationException>(direct.Radiance.Dispose);
        Assert.Throws<InvalidOperationException>(() => post.Texture.Resize(4, 3));
        AssertStaticSubmissionHasNoRoutingWork(targets.Select(target => target.PreservePass).ToArray());
    }
    #endregion

    #region Private
    /// <summary>Submits every retained contract repeatedly and captures real native calls and all avoided managed work.</summary>
    private void AssertStaticSubmissionHasNoRoutingWork(RenderPassDesc[] passes)
    {
        using var work = new FramebufferRoutingWorkCapture();
        using var native = new GraphicsTransitionCapture();
        using var declarations = new StaticPassDeclarationCapture();
        Assert.True(GraphicsCommandContext.TryRun("Tests.StaticTargetMigration", [], true, commands =>
        {
            for (int frame = 0; frame < 3; frame++)
                foreach (var pass in passes)
                {
                    commands.BeginPass(pass);
                    commands.EndPass();
                }
        }));
        declarations.AssertReusable(passes.Length, passes.Length * 2);
        Assert.Equal(0, work.DynamicApplications);
        Assert.Equal(0, work.Captures);
        Assert.Equal(0, work.StaticChecks);
        Assert.Equal(0, work.CompletenessChecks);
        Assert.DoesNotContain(native.Calls, call => call.StartsWith("Routing"));
        output.WriteLine("STATIC_TARGET_MIGRATION=" + System.Text.Json.JsonSerializer.Serialize(new
        {
            targets = passes.Length,
            frames = 3,
            work.DynamicApplications,
            work.Captures,
            work.StaticChecks,
            work.CompletenessChecks,
            calls = native.Calls
        }));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
