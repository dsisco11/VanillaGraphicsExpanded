using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Tests.GPU.Fixtures;

/// <summary>Owns the simulated engine primary framebuffer and centralizes terrain input transfers.</summary>
internal sealed class EngineTerrainBuffers : IDisposable
{
    public DynamicTexture2D Color { get; }
    public DynamicTexture2D Depth { get; }
    public GpuFramebuffer Output { get; }
    public FrameBufferRef Primary { get; }

    #region Engine allocation
    /// <summary>Models engine-owned color and sampled depth; VGE attachments remain owned by GBufferManager.</summary>
    public EngineTerrainBuffers(int width, int height)
    {
        Color = DynamicTexture2D.Create(width,height,PixelInternalFormat.Rgba16f);
        Depth = DynamicTexture2D.Create(width,height,PixelInternalFormat.R32f);
        Output = GpuFramebuffer.CreateSingle(Color)!;
        Primary = new() { FboId=Output.FboId, Width=width, Height=height,
            DepthTextureId=Depth.TextureId, ColorTextureIds=[Color.TextureId] };
    }
    #endregion

    #region Authored terrain inputs
    /// <summary>Transfers engine raster data into its owners' existing allocations without assuming their internal formats.</summary>
    public void UploadTerrain(GBufferManager buffers, float[] depth, float[] normals, float[] material, float[] color)
    {
        Depth.UploadDataImmediate(depth); Color.UploadDataImmediate(color);
        Upload(buffers.NormalTextureId, normals); Upload(buffers.MaterialTextureId, material);
    }

    /// <summary>Publishes packed terrain patch identities using the production G-buffer attachment.</summary>
    public void UploadFeedback(GBufferManager buffers, uint[] values)
    {
        Assert.Equal(Primary.Width*Primary.Height*4,values.Length);
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,buffers.PatchIdTextureId);
        GL.TexSubImage2D(TextureTarget.Texture2D,0,0,0,Primary.Width,Primary.Height,PixelFormat.RgbaInteger,PixelType.UnsignedInt,values);
    }

    /// <summary>Uploads four source channels; OpenGL converts them to the production attachment's storage format.</summary>
    private void Upload(int texture, float[] values)
    {
        Assert.Equal(Primary.Width*Primary.Height*4,values.Length);
        using var binding = GlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,texture);
        GL.TexSubImage2D(TextureTarget.Texture2D,0,0,0,Primary.Width,Primary.Height,PixelFormat.Rgba,PixelType.Float,values);
    }
    #endregion

    #region Lifetime
    /// <summary>Releases the simulated engine framebuffer after attached production owners stop using it.</summary>
    public void Dispose() { Output.Dispose(); Depth.Dispose(); Color.Dispose(); }
    #endregion
}
