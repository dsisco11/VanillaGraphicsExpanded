using VanillaGraphicsExpanded.Rendering.RHI;
using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns one floating-point effect image and its immutable attachment intentions.</summary>
internal sealed class PostprocessImage : IDisposable
{
    private readonly GpuFramebufferAttachment image;
    #region Public API
    /// <summary>Allocates persistent effect storage with cleanup on partial creation failure.</summary>
    internal PostprocessImage(int width,int height,string name,bool clear=false)
    {
        Texture=clear?RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Width = width, Height = height, Format = TextureFormat.Rgba16f, MipLevels = 1, Storage = ResourceStorageMode.Mutable, BaseLevel = 0, MaxLevel = 0, Filter = TextureFilterMode.Nearest, Label = name }, [new RHITextureInitialData(new(new(), 0, 0, 0, width, height), VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL.OpenGLPixelValues.Encoding(TextureFormatHelper.GetPixelFormat(PixelInternalFormat.Rgba16f), OpenTK.Graphics.OpenGL.PixelType.Float), System.Runtime.InteropServices.MemoryMarshal.AsBytes((new float[width*height*4]).AsSpan()).ToArray())]).RequireResource()
            :RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name }).RequireResource();
        try {
            image=GpuFramebufferAttachment.FromTexture(Texture);
            Target=StaticGpuFramebuffer.Create([image],debugName:name);
        }
        catch { image?.Dispose(); Texture.Dispose(); throw; }
    }
    /// <summary>Supplies owned linear image storage.</summary>
    internal DynamicTexture2D Texture { get; }
    /// <summary>Supplies the framebuffer borrowing that storage.</summary>
    internal StaticGpuFramebuffer Target { get; }
    /// <summary>Releases the borrower before the image.</summary>
    public void Dispose() { Target.Dispose(); image.Dispose(); Texture.Dispose(); }
    #endregion
}
