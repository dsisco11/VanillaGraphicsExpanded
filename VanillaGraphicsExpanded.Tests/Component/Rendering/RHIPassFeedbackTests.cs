using System;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Rendering.RHI;


namespace VanillaGraphicsExpanded.Tests.Component.Rendering;

/// <summary>Checks sampler and image feedback using published view-relative and shared-storage ranges.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class RHIPassFeedbackTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Sampling-range changes withdraw both cached success and cached failure across repeated pass use.</summary>
    [Fact]
    public void CachedFeedbackRefreshesAfterSamplingConfigurationChanges()
    {
        // Arrange: attach mip 1, initially outside the texture's exposed sampling range.
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var texture = commands.CreateTexture(new() { Dimension = TextureDimension.Two, Width = 4, Height = 4,
            Format = TextureFormat.Rgba8, MipLevels = 2, Storage = ResourceStorageMode.Immutable, BaseLevel = 0, MaxLevel = 0 }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(texture, new(1)))]);
        void Execute(bool conflict)
        {
            using var boundary = commands.EnterBoundary(new("Tests.FeedbackRefresh", true, operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                commands.BeginRenderPass(pass);
                for (int repeat = 0; repeat < 2; repeat++)
                    if (conflict) Assert.Throws<InvalidOperationException>(() => RenderPass.ValidateSampledTexture(texture));
                    else RenderPass.ValidateSampledTexture(texture);
                commands.EndRenderPass();
            });
        }
        // Act: validate success, expand the sampled range to include the attachment, then narrow it again.
        Execute(false);
        texture.SetMipRange(0, 1);
        Execute(true);
        texture.SetMipRange(0, 0);
        Execute(false);
        // Assert: every transition completed without a stale conflict or cached rejection.
    }
    /// <summary>Image bindings select their exact mip and layer independently of sampler base/max parameters.</summary>
    [Fact]
    public void ImageSelectionRejectsOnlyOverlappingMipAndLayers()
    {
        // Arrange: attach one array layer at mip 1 while sampler parameters expose only mip 0.
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var texture = commands.CreateTexture(new()
        {
            Dimension = TextureDimension.TwoArray, Width = 8, Height = 8, Depth = 4,
            Format = TextureFormat.Rgba8, MipLevels = 3, Storage = ResourceStorageMode.Immutable,
            BaseLevel = 0, MaxLevel = 0
        }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(texture, new(1, 2)))]);
        using var boundary = commands.EnterBoundary(new("Tests.ImageFeedback", true, operations: BoundaryOperations.RenderPasses));
        // Act: compare sampled selection and exact image-selection validation within the active pass.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            RenderPass.ValidateSampledTexture(texture);
            RenderPass.ValidateImageTexture(new(texture, Level: 0, Layer: 2));
            RenderPass.ValidateImageTexture(new(texture, Level: 1, Layer: 1));
            // Assert: only the attached mip/layer pair is rejected as overlapping.
            Assert.Throws<InvalidOperationException>(() => RenderPass.ValidateImageTexture(new(texture, Level: 1, Layer: 2)));
            Assert.Throws<InvalidOperationException>(() => RenderPass.ValidateImageTexture(new(texture, Level: 1, Layered: true)));
            commands.EndRenderPass();
        });
    }

    /// <summary>Nested views compare cumulative canonical offsets once, while sibling layer selections stay disjoint.</summary>
    [Fact]
    public void NestedViewFeedbackUsesOriginalAllocationCoordinates()
    {
        // Arrange: compose a parent view and nested/sibling views over one canonical array allocation.
        EnsureContextValid();
        var commands = RHICommandContext.Current;
        using var texture = commands.CreateTexture(new()
        {
            Dimension = TextureDimension.TwoArray, Width = 16, Height = 16, Depth = 6,
            Format = TextureFormat.Rgba8, MipLevels = 4, Storage = ResourceStorageMode.Immutable,
            BaseLevel = 0, MaxLevel = 3
        }).RequireResource();
        using var parent = commands.CreateTextureView(new()
        {
            Source = texture, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8,
            BaseMip = 1, MipCount = 3, BaseLayer = 1, LayerCount = 4
        }).RequireResource();
        using var nested = commands.CreateTextureView(new()
        {
            Source = parent, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8,
            BaseMip = 1, MipCount = 1, BaseLayer = 1, LayerCount = 1
        }).RequireResource();
        using var sibling = commands.CreateTextureView(new()
        {
            Source = texture, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba8,
            BaseMip = 2, MipCount = 1, BaseLayer = 3, LayerCount = 1
        }).RequireResource();
        var pass = new RHIRenderPassDescription([new(Image: RHIAttachment.FromTexture(texture, new(2, 2)))]);
        using var boundary = commands.EnterBoundary(new("Tests.ViewFeedback", true, operations: BoundaryOperations.RenderPasses));
        // Act: compare sibling and nested views against the attachment's canonical allocation range.
        boundary.Run(() =>
        {
            commands.BeginRenderPass(pass);
            RenderPass.ValidateSampledTexture(sibling);
            // Assert: cumulative nested offsets overlap, while the sibling selection remains disjoint.
            Assert.Throws<InvalidOperationException>(() => RenderPass.ValidateSampledTexture(nested));
            Assert.Throws<InvalidOperationException>(() => RenderPass.ValidateImageTexture(new(nested, Layer: 0)));
            commands.EndRenderPass();
        });
    }
    #endregion
}
