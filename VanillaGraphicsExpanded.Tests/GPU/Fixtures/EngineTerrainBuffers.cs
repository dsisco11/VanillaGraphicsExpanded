using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using VanillaGraphicsExpanded.Rendering.RHI;
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
        Color = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Depth = RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = TextureFormat.R32f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = null }).RequireResource();
        Output = DynamicGpuFramebuffer.CreateSingle(Color)!;
        Primary = new() { FboId=Output.FboId, Width=width, Height=height,
            DepthTextureId=Depth.TextureId, ColorTextureIds=[Color.TextureId] };
    }
    #endregion

    #region Authored terrain inputs
    /// <summary>Transfers engine raster data into its owners' existing allocations without assuming their internal formats.</summary>
    public void UploadTerrain(GBufferManager buffers, float[] depth, float[] normals, float[] material, float[] color)
    {
        Depth.UploadDataImmediate(depth); Color.UploadDataImmediate(color);
        buffers.SurfaceTexture!.UploadDataImmediate(normals, 0, 0, 0, Primary.Width, Primary.Height, 1);
        buffers.SurfaceTexture.UploadDataImmediate(material, 0, 0, 1, Primary.Width, Primary.Height, 1);
    }

    /// <summary>Publishes packed terrain patch identities using the production G-buffer attachment.</summary>
    public void UploadFeedback(GBufferManager buffers, uint[] values)
    {
        Assert.Equal(Primary.Width*Primary.Height*4,values.Length);
        using var binding = OpenGlStateCache.Current.BindTextureScope(TextureTarget.Texture2D,0,buffers.PatchIdTextureId);
        GL.TexSubImage2D(TextureTarget.Texture2D,0,0,0,Primary.Width,Primary.Height,PixelFormat.RgbaInteger,PixelType.UnsignedInt,values);
    }

    #endregion

    #region Lifetime
    /// <summary>Releases the simulated engine framebuffer after attached production owners stop using it.</summary>
    public void Dispose() { Output.Dispose(); Depth.Dispose(); Color.Dispose(); }
    #endregion
}
