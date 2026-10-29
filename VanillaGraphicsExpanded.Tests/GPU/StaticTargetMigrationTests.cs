using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        using var transport = RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 3), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 2), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var optical = GpuFramebufferAttachment.FromTexture(transport, layer: 0);
        using var illumination = GpuFramebufferAttachment.FromTexture(transport, layer: 1);
        using var target = StaticGpuFramebuffer.Create([optical, illumination]);
        var pass = FramebufferTestSetup.DescribeAttachments(target, [new(AttachmentIndex:0,Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0)),new(AttachmentIndex:1,Load:AttachmentLoad.Clear,Clear:ColorClearValue.Float(0,0,0,0))]);
        Assert.All(pass.Colors, color => Assert.Equal(AttachmentLoad.Clear, color.Load));
        using var declarations = new StaticPassDeclarationCapture();
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        OpenGlStateCache.Current.Invalidate(EPipelineState.ScissorTest | EPipelineState.ColorMask);
        for (int frame = 0; frame < 3; frame++)
        {
            // New nonzero data makes a missing clear observable independently each frame.
            GL.ClearTexImage(transport.TextureId, 0, PixelFormat.Rgba, PixelType.Float,
                new[] { frame + 1f, 2f, 3f, 4f });
            {
    var commands=RHICommandContext.Current;
    using var scope77=commands.EnterBoundary(new("Tests.WaterVolume.Clear",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope77.Run(()=>{
                commands.BeginRenderPass(pass);
                commands.EndRenderPass();
            });
}
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
        using var external = RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var borrowed = new BorrowedTexture(external.TextureId);
        using var view = throughView ? RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = borrowed, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 3, Label = null }).RequireResource() : null;
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
            .Where(property => property.PropertyType == typeof(RHIRenderPassDescription))
            .Select(property => Assert.IsType<RHIRenderPassDescription>(property.GetValue(targets))).ToArray();
        Assert.Equal(11, framebuffers.Length);
        var clear = framebuffers.ToDictionary(target => target, target => targets.GetPass(target, true));
        foreach (var target in framebuffers)
        {
            Assert.Same(target, targets.GetPass(target, false));
            Assert.Same(clear[target], targets.GetPass(target, true));
            Assert.All(clear[target].Colors, color => Assert.Equal(AttachmentLoad.Clear, color.Load));
        }

        // Change roles only: the immutable target and its declarations stay paired with storage.
        var originalCurrent = targets.ScreenProbeAtlasCurrentPass;
        var originalHistory = targets.ScreenProbeAtlasHistoryPass;
        (targets.ScreenProbeAtlasCurrentPass, targets.ScreenProbeAtlasHistoryPass) =
            (targets.ScreenProbeAtlasHistoryPass, targets.ScreenProbeAtlasCurrentPass);
        (targets.ScreenProbeAtlasCurrentTex, targets.ScreenProbeAtlasHistoryTex) =
            (targets.ScreenProbeAtlasHistoryTex, targets.ScreenProbeAtlasCurrentTex);
        (targets.ScreenProbeAtlasMetaCurrentTex, targets.ScreenProbeAtlasMetaHistoryTex) =
            (targets.ScreenProbeAtlasMetaHistoryTex, targets.ScreenProbeAtlasMetaCurrentTex);
        Assert.Same(originalHistory, targets.ScreenProbeAtlasCurrentPass);
        Assert.Same(originalCurrent, targets.ScreenProbeAtlasHistoryPass);
        Assert.Equal((GpuTextureRef)targets.ScreenProbeAtlasCurrentTex, targets.ScreenProbeAtlasCurrentPass.Colors[0].Image!.Value.Texture);
        Assert.Same(clear[originalHistory], targets.GetPass(targets.ScreenProbeAtlasCurrentPass, true));

        // Clear loads obey native write masks and scissor; establish complete-image
        // writes before observing routing so prior hostile-state fixtures cannot clip them.
        GL.Disable(EnableCap.ScissorTest);
        GL.ColorMask(true, true, true, true);
        OpenGlStateCache.Current.Invalidate(EPipelineState.ScissorTest | EPipelineState.ColorMask);
        AssertStaticSubmissionHasNoRoutingWork(framebuffers.Select(target => clear[target]).ToArray());
        foreach (var pass in framebuffers)
            foreach (var output in pass.Colors)
            {
                var image=output.Image!.Value;
                float[] pixels=image.Texture.Resource is Texture3D array
                    ? LayeredTestTexture.Read(array,image.Subresource.Layer) : image.Texture.ReadPixels();
                Assert.All(pixels,value=>Assert.Equal(0,value));
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
        RHIRenderPassDescription[] targets=[probes.MetadataPass,probes.RadiancePass,direct.Pass,post.Pass];
        AssertStaticSubmissionHasNoRoutingWork(targets);
        Assert.True(direct.Radiance.IsValid);Assert.True(post.Texture.IsValid);
    }
    #endregion

    #region Private
    /// <summary>Submits every retained contract repeatedly and captures real native calls and all avoided managed work.</summary>
    private void AssertStaticSubmissionHasNoRoutingWork(RHIRenderPassDescription[] passes)
    {
        // Cold realization establishes the reusable native targets before repeated-work observation.
        RenderPassTestBoundary.Run(commands => { foreach (var pass in passes) { commands.BeginRenderPass(pass); commands.EndRenderPass(); } });
        using var work = new FramebufferRoutingWorkCapture();
        using var declarations = new StaticPassDeclarationCapture();
        {
    var commands=RHICommandContext.Current;
    using var scope78=commands.EnterBoundary(new("Tests.StaticTargetMigration",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope78.Run(()=>{
            for (int frame = 0; frame < 3; frame++)
                foreach (var pass in passes)
                {
                    commands.BeginRenderPass(pass);
                    commands.EndRenderPass();
                }
        });
}
        declarations.AssertReusable(passes.Length, passes.Length * 2);
        Assert.Equal(0, work.DynamicApplications);
        Assert.Equal(0, work.Captures);
        Assert.Equal(0, work.CompletenessChecks);
        output.WriteLine("STATIC_TARGET_MIGRATION=" + System.Text.Json.JsonSerializer.Serialize(new
        {
            targets = passes.Length,
            frames = 3,
            work.DynamicApplications,
            work.Captures,
            
            work.CompletenessChecks,
        }));
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
