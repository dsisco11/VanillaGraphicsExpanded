using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Pipeline;
using VanillaGraphicsExpanded.Rendering.Pipeline.Passes;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies managed array-view lifetime and feedback exclusion against actual OpenGL storage.</summary>
[Collection("GPU")][Trait("Category","GPU")]
public sealed class ArrayTextureViewTests(HeadlessGLFixture fixture):RenderTestBase(fixture)
{
    #region Public API
    /// <summary>View validity follows the source allocation, including storage replacement rather than only owner disposal.</summary>
    [Theory][InlineData(false)][InlineData(true)]
    public void ManagedViewCannotOutliveItsSourceAllocation(bool resize)
    {
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 9), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 3, LayerCount = 3, Label = null }).RequireResource();
        Assert.True(view.IsValid);Assert.Same(owner,view.Source);
        if(resize)owner.Resize(3,3,9);else owner.Dispose();
        Assert.False(view.IsValid);
    }
    /// <summary>Driver clamping cannot publish a wrapper claiming access to nonexistent layers.</summary>
    [Fact]
    public void ViewRejectsOutOfRangeLayers()
    {
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Assert.ThrowsAny<ArgumentException>(()=>RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 1, LayerCount = 3, Label = null }).RequireResource());
        Assert.ThrowsAny<ArgumentException>(()=>RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = -1, LayerCount = 1, Label = null }).RequireResource());
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>A three-dimensional sampler reaches every z slice even though its texture-view layer count is one.</summary>
    [Fact]
    public void VolumeViewCannotSampleAnAttachedSourceSlice()
    {
        EnsureContextValid();using var owner=RHICommandContext.Current.CreateDynamicTexture3D(new RHITextureDescription { Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, Width = System.Math.Max(1, 2), Height = System.Math.Max(1, 2), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = (true ? ResourceStorageMode.Immutable : ResourceStorageMode.Mutable), Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var view=RHICommandContext.Current.CreateTextureView(new RHITextureViewDescription { Source = owner, Dimension = TextureDimension.Three, Format = TextureFormat.Rgba32f, BaseMip = 0, MipCount = 1, BaseLayer = 0, LayerCount = 1, Label = null }).RequireResource();
        using var attachment=GpuFramebufferAttachment.FromTexture(owner,layer:1);
        using var framebuffer=DynamicGpuFramebuffer.Create([attachment])!;
        {
    var commands=RHICommandContext.Current;
    using var scope4=commands.EnterBoundary(new("Tests.VolumeView.Feedback",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope4.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(framebuffer, [new(AttachmentIndex:0)]));
            Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledTexture(view));
            commands.EndRenderPass();
        });
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Different view object names cannot hide overlapping source images, while disjoint blocks remain legal.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void ActivePassRejectsOverlappingViewsAndProtectsSourceOwner(bool nested)
    {
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
    var commands=RHICommandContext.Current;
    using var scope5=commands.EnterBoundary(new("Tests.ArrayView.Feedback",true,graphics:System.Linq.Enumerable.Select<RHIGraphicsPipeline,RHIGraphicsPipelineRef>([],value=>value),operations:BoundaryOperations.RenderPasses));
    scope5.Run(()=>{
            commands.BeginRenderPass(FramebufferTestSetup.DescribeAttachments(framebuffer, [new(AttachmentIndex:0)]));
            Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledTexture(overlap));
            if (nestedOverlap is not null) Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledTexture(nestedOverlap));
            RenderPass.ValidateSampledTexture(separate);
            Assert.Throws<InvalidOperationException>(()=>owner.Resize(3,3,9));
            Assert.Throws<InvalidOperationException>(()=>owner.Dispose());
            Assert.True(owner.IsValid);Assert.True(target.IsValid);
            commands.EndRenderPass();
        });
}
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
