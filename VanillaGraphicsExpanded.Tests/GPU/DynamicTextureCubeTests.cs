using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Qualifies reusable cube storage independently of celestial format and encoding policy.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class DynamicTextureCubeTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>All six faces of a non-power-of-two mip chain can be attached and populated in different color formats.</summary>
    [Theory]
    [InlineData(PixelInternalFormat.Rgba8)]
    [InlineData(PixelInternalFormat.Rgba16f)]
    [InlineData(PixelInternalFormat.R32f)]
    [InlineData(PixelInternalFormat.R11fG11fB10f)]
    public void ColorFacesAndMipsAreIndependent(PixelInternalFormat format)
    {
        EnsureContextValid();
        using var cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLImageValues.Format((OpenTK.Graphics.OpenGL.PixelInternalFormat)(format)), Width = 3, Height = 3, MipLevels = 2, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (2) - 1, Label = "Test.ColorCube" }).RequireResource();
        Assert.Equal(TextureTarget.TextureCubeMap, cube.TextureTarget);
        Assert.Equal(format, cube.InternalFormat); Assert.Equal(2, cube.MipLevels);
        Assert.Equal(TextureFilterMode.Linear, cube.FilterMode); Assert.Equal("Test.ColorCube", cube.DebugName);
        for (int mip = 0; mip < cube.MipLevels; mip++) for (int face = 0; face < 6; face++)
        {
            using var image = GpuFramebufferAttachment.FromTexture(cube, mip, cubeFace: TextureTarget.TextureCubeMapPositiveX + face);
            using var target = DynamicGpuFramebuffer.Create([image]);
            using var binding = StateCache.Current.BindFramebufferScope();
            OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer, target.FboId);
            GL.ClearBuffer(ClearBuffer.Color, 0, new[] { (face + 1) / 8f, .5f, .25f, 1f });
        }
        using var textureBinding = StateCache.Current.BindTextureScope(((GpuTextureRef)cube).Target, 0, cube.TextureId);
        GL.GetTexParameter(TextureTarget.TextureCubeMap, GetTextureParameter.TextureMinFilter, out int minimum);
        Assert.Equal((int)TextureMinFilter.LinearMipmapLinear, minimum);
        for (int mip = 0; mip < cube.MipLevels; mip++) for (int face = 0; face < 6; face++)
        {
            int size = Math.Max(1, 3 >> mip); float[] data = new float[size * size * 4];
            GL.GetTexImage(TextureTarget.TextureCubeMapPositiveX + face, mip, PixelFormat.Rgba, PixelType.Float, data);
            for (int pixel = 0; pixel < size * size; pixel++) Assert.InRange(MathF.Abs(data[pixel * 4] - (face + 1) / 8f), 0, .004f);
        }
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Integer cube storage uses a compatible allocation type and nearest-only sampling.</summary>
    [Fact]
    public void IntegerCubeUsesNearestFiltering()
    {
        EnsureContextValid();
        using var cube = RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.R32ui, Width = 2, Height = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        using var image = GpuFramebufferAttachment.FromTexture(cube, cubeFace: TextureTarget.TextureCubeMapPositiveX);
        using var target = DynamicGpuFramebuffer.Create([image]);
        using var binding = StateCache.Current.BindFramebufferScope();
        OpenGLFramebufferSelection.Bind(StateCache.Current, FramebufferTarget.DrawFramebuffer, target.FboId);
        GL.ClearBuffer(ClearBuffer.Color, 0, new uint[] { 7, 0, 0, 0 });
        using var textureBinding = StateCache.Current.BindTextureScope(((GpuTextureRef)cube).Target, 0, cube.TextureId);
        uint[] data = new uint[4];
        GL.GetTexImage(TextureTarget.TextureCubeMapPositiveX, 0, PixelFormat.RedInteger, PixelType.UnsignedInt, data);
        Assert.All(data, value => Assert.Equal(7u, value));
        GL.GetTexParameter(TextureTarget.TextureCubeMap, GetTextureParameter.TextureMinFilter, out int minimum);
        Assert.Equal((int)TextureMinFilter.Nearest, minimum);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>Rejects incompatible descriptors before allocating and integrates with the standard resource collection.</summary>
    [Fact]
    public void InvalidDescriptorsAndCollectionRetirementPreserveOwnership()
    {
        EnsureContextValid();
        Assert.Throws<ArgumentOutOfRangeException>(() => RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 0, Height = 0, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        Assert.Throws<ArgumentOutOfRangeException>(() => RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 3, Height = 3, MipLevels = 3, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (3) - 1, Label = null }).RequireResource());
        Assert.Throws<ArgumentException>(() => RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.DepthComponent32f, Width = 2, Height = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        Assert.Throws<ArgumentException>(() => RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.R32ui, Width = 2, Height = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Linear, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        using var resources = new GpuResourceCollection();
        var cube = resources.Own(RHICommandContext.Current.CreateTextureCube(new RHITextureDescription { Dimension = TextureDimension.Cube, Format = TextureFormat.Rgba8, Width = 2, Height = 2, MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource());
        int id = cube.TextureId;
        Assert.True(GL.IsTexture(id)); resources.Dispose();
        Assert.False(cube.IsValid); Assert.False(GL.IsTexture(id));
    }

    /// <summary>Shared storage replacement refreshes a borrowing framebuffer's selected face and mip in both allocation modes.</summary>
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public void SharedReallocationRefreshesBorrowedFace(bool inPlace)
    {
        EnsureContextValid();
        using var cube = new ReallocatableCube(4);
        using var image = GpuFramebufferAttachment.FromTexture(cube, mipLevel: 1, cubeFace: TextureTarget.TextureCubeMapNegativeZ);
        using var target = DynamicGpuFramebuffer.Create([image]);
        int original = cube.TextureId;
        cube.Replace(8, inPlace);
        target.ValidateAttachments();
        Assert.Equal(4, image.Width); Assert.Equal(4, target.Width);
        // A replacement may immediately reuse a deleted GL name; successful writes below
        // prove the framebuffer now references the new storage even in that case.
        if (inPlace) Assert.Equal(original, cube.TextureId);
        using var bindings = StateCache.Current.BindFramebufferScope();
        target.Bind();
        GL.ClearBuffer(ClearBuffer.Color, 0, new[] { .25f, .5f, .75f, 1f });
        using var textureBinding = StateCache.Current.BindTextureScope(((GpuTextureRef)cube).Target, 0, cube.TextureId);
        float[] data = new float[4 * 4 * 4];
        GL.GetTexImage(TextureTarget.TextureCubeMapNegativeZ, 1, PixelFormat.Rgba, PixelType.Float, data);
        for (int pixel = 0; pixel < 16; pixel++) Assert.Equal(.25f, data[pixel * 4]);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion

    #region Private
    /// <summary>Exposes shared protected allocation for testing framebuffer observers independently of a fixed-size public factory.</summary>
    private sealed class ReallocatableCube : GpuTexture
    {
        #region Public API
        /// <summary>Allocates a small two-level cube through the common storage lifecycle.</summary>
        internal ReallocatableCube(int size)
        {
            filterMode = TextureFilterMode.Linear;
            AllocateOrReallocate2DTexture(size, size, PixelInternalFormat.Rgba16f, 2, TextureTarget.TextureCubeMap);
        }
        /// <summary>Requests in-place or replacement storage and lets native publication deliver the new dimensions.</summary>
        internal void Replace(int size, bool inPlace)
        {
            if (inPlace) Reallocate2DStorageInPlace(size, size, 2);
            else AllocateOrReallocate2DTexture(size, size, PixelInternalFormat.Rgba16f, 2, TextureTarget.TextureCubeMap);
        }
        #endregion
    }
    #endregion
}
