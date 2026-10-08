using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
namespace VanillaGraphicsExpanded.PBR.Postprocessing;
/// <summary>Owns one floating-point effect image and its borrowing framebuffer.</summary>
internal sealed class PostprocessImage : IDisposable
{
    #region Public API
    /// <summary>Allocates persistent effect storage with cleanup on partial creation failure.</summary>
    internal PostprocessImage(int width,int height,string name,bool clear=false)
    {
        Texture=clear?DynamicTexture2D.CreateWithData(width,height,PixelInternalFormat.Rgba16f,new float[width*height*4],debugName:name)
            :DynamicTexture2D.Create(width,height,PixelInternalFormat.Rgba16f,debugName:name);
        try { Target=GpuFramebuffer.CreateSingle(Texture,debugName:name)!; }
        catch { Texture.Dispose(); throw; }
    }
    /// <summary>Supplies owned linear image storage.</summary>
    internal DynamicTexture2D Texture { get; }
    /// <summary>Supplies the framebuffer borrowing that storage.</summary>
    internal GpuFramebuffer Target { get; }
    /// <summary>Releases the borrower before the image.</summary>
    public void Dispose() { Target.Dispose(); Texture.Dispose(); }
    #endregion
}
