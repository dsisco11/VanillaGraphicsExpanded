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
        EnsureContextValid();using var owner=DynamicTexture3D.Create(2,2,9,PixelInternalFormat.Rgba32f,immutableStorage:true);
        using var view=GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,3,3);
        Assert.True(view.IsValid);Assert.Same(owner,view.Source);
        if(resize)owner.Resize(3,3,9);else owner.Dispose();
        Assert.False(view.IsValid);
    }
    /// <summary>Driver clamping cannot publish a wrapper claiming access to nonexistent layers.</summary>
    [Fact]
    public void ViewRejectsOutOfRangeLayers()
    {
        EnsureContextValid();using var owner=DynamicTexture3D.Create(2,2,3,PixelInternalFormat.Rgba32f,immutableStorage:true);
        Assert.Throws<InvalidOperationException>(()=>GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,1,3));
        Assert.Throws<ArgumentOutOfRangeException>(()=>GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,-1,1));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>A three-dimensional sampler reaches every z slice even though its texture-view layer count is one.</summary>
    [Fact]
    public void VolumeViewCannotSampleAnAttachedSourceSlice()
    {
        EnsureContextValid();using var owner=DynamicTexture3D.Create(2,2,3,PixelInternalFormat.Rgba32f,
            textureTarget:TextureTarget.Texture3D,immutableStorage:true);
        using var view=GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture3D,PixelInternalFormat.Rgba32f,0,1,0,1);
        using var attachment=GpuFramebufferAttachment.FromTexture(owner,layer:1);
        using var framebuffer=GpuFramebuffer.Create([attachment])!;
        Assert.True(GraphicsCommandContext.TryRun("Tests.VolumeView.Feedback",[],true,commands=>
        {
            commands.BeginPass(new(framebuffer,[new(0)]));
            Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledView(view));
            commands.EndPass();
        }));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    /// <summary>Different view object names cannot hide overlapping source images, while disjoint blocks remain legal.</summary>
    [Fact]
    public void ActivePassRejectsOverlappingViewsAndProtectsSourceOwner()
    {
        EnsureContextValid();using var owner=DynamicTexture3D.Create(2,2,9,PixelInternalFormat.Rgba32f,immutableStorage:true);
        using var target=GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,3,3);
        using var overlap=GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,4,2);
        using var separate=GpuTextureView.CreateFromTexture(owner,TextureTarget.Texture2DArray,PixelInternalFormat.Rgba32f,0,1,6,3);
        using var attachment=GpuFramebufferAttachment.FromTexture(target,layer:1);
        using var framebuffer=GpuFramebuffer.Create([attachment])!;
        Assert.True(GraphicsCommandContext.TryRun("Tests.ArrayView.Feedback",[],true,commands=>
        {
            commands.BeginPass(new(framebuffer,[new(0)]));
            Assert.Throws<InvalidOperationException>(()=>RenderPass.ValidateSampledView(overlap));
            RenderPass.ValidateSampledView(separate);
            Assert.Throws<InvalidOperationException>(()=>owner.Resize(3,3,9));
            Assert.Throws<InvalidOperationException>(()=>owner.Dispose());
            Assert.True(owner.IsValid);Assert.True(target.IsValid);
            commands.EndPass();
        }));
        Assert.Equal(ErrorCode.NoError,GL.GetError());
    }
    #endregion
}
