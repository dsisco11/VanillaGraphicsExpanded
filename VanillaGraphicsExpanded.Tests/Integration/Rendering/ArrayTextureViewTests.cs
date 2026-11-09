using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;

using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Qualifies managed array-view lifetime and feedback exclusion against actual OpenGL storage.</summary>
[Collection("GPU")][Trait("Category", "GpuIntegration")]
public sealed class ArrayTextureViewTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    /// <summary>View validity follows the source allocation, including storage replacement rather than only owner disposal.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void ManagedViewCannotOutliveItsSourceAllocation(bool resize)
    {
        // Arrange: Create a view into selected array layers, then choose whether its owner is resized or disposed.
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 9), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 3, LayerCount = 3, Label = null }).RequireResource();
        // Act: Retire or replace the source allocation after confirming that the view initially resolves to its owner.
        Assert.True(view.IsValid);Assert.Same(owner,view.Source);
        if(resize)owner.Resize(3,3,9);else owner.Dispose();
        // Assert: A view cannot remain valid across either source lifetime transition.
        Assert.False(view.IsValid);
    }
    /// <summary>Driver clamping cannot publish a wrapper claiming access to nonexistent layers.</summary>
    [Fact]
    public void ViewRejectsOutOfRangeLayers()
    {
        // Arrange: Allocate three array layers, leaving both requested ranges outside that source extent.
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        // Act and assert: Both an overflowing end layer and a negative base layer must be rejected at creation.
        Assert.ThrowsAny<ArgumentException>(()=>RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 1, LayerCount = 3, Label = null }).RequireResource());
        Assert.ThrowsAny<ArgumentException>(()=>RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = -1, LayerCount = 1, Label = null }).RequireResource());
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>A three-dimensional sampler reaches every z slice even though its texture-view layer count is one.</summary>
    [Fact]
    public void VolumeViewCannotSampleAnAttachedSourceSlice()
    {
        // Arrange: Attach one source z slice while sampling through a 3D view that spans the volume.
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
        using var attachment=GpuFramebufferAttachment.FromTexture(owner,layer:1);
        using var framebuffer=DynamicGpuFramebuffer.Create([attachment])!;
        {
    var commands=RHICommandContext.Current;
    using var scope4=commands.EnterBoundary(new("Tests.VolumeView.Feedback",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope4.Run(()=>{
            // Arrange: Enter the pass with the source slice attached before checking the sampled view.
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(framebuffer, [new(AttachmentIndex:0)]));
            // Act and assert: Feedback validation rejects the volume because it overlaps that attached image.
            Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledTexture(view));
            commands.EndRenderPass();
        });
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Validates source-image overlap while the caller retains shared storage through pass end.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActivePassRejectsOverlappingViewsWhileOwnerRetainsSource(bool nested)
    {
        // Arrange: Create overlapping and disjoint views, optionally nesting the sampled views under a target view.
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 9), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var target=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 3, LayerCount = 3, Label = null }).RequireResource();
        using var overlap=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 4, LayerCount = 2, Label = null }).RequireResource();
        using var separate=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 6, LayerCount = 3, Label = null }).RequireResource();
        using var nestedTarget = nested ? RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = target,
            Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 3 }).RequireResource() : null;
        using var nestedOverlap = nested ? RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = target,
            Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 1, LayerCount = 2 }).RequireResource() : null;
        using var attachment=GpuFramebufferAttachment.FromTexture(nestedTarget ?? target,layer:1);
        using var framebuffer=DynamicGpuFramebuffer.Create([attachment])!;
        {
            var commands = RHICommandContext.Current;
            using var boundary = commands.EnterBoundary(new("Tests.ArrayView.Feedback", true,
                operations: BoundaryOperations.RenderPasses));
            boundary.Run(() =>
            {
                // Arrange: Keep the owner alive across the pass and attach an image through the target view.
                commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(framebuffer, [new(AttachmentIndex:0)]));
                try
                {
                    // Act and assert: Reject overlapping views but allow a disjoint view into the same source allocation.
                    Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledTexture(overlap));
                    if (nestedOverlap is not null) Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledTexture(nestedOverlap));
                    RenderPass.ValidateSampledTexture(separate);
                    Assert.True(owner.IsValid);Assert.True(target.IsValid);
                }
                finally { commands.EndRenderPass(); }
            });
        }
        // Act: Replace the source only after pass cleanup releases its borrowing interval.
        owner.Resize(3,3,9);
        // Assert: The source remains valid while views into its retired allocation become invalid.
        Assert.True(owner.IsValid);
        Assert.False(target.IsValid);
        owner.Dispose();
        Assert.False(owner.IsValid);
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
