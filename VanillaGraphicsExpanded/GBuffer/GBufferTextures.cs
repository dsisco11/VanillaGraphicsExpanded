using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded;

/// <summary>Owns terrain normal, material, patch-identity and environment render targets independently of framebuffer injection.</summary>
internal sealed class GBufferTextures : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    public DynamicTexture2D Normal { get; }
    public DynamicTexture2D Material { get; }
    public DynamicTexture2D PatchId { get; }
    public DynamicTexture2D Environment { get; }

    #region Allocation
    /// <summary>Allocates companion terrain targets with the sampling policy used by raw-ID consumers.</summary>
    public GBufferTextures(int width, int height)
    {
        try
        {
            Normal = Create(width, height, PixelInternalFormat.Rgba16f, "gNormal");
            Material = Create(width, height, PixelInternalFormat.Rgba16f, "gMaterial");
            PatchId = Create(width, height, PixelInternalFormat.Rgba32ui, "gPatchId");
            Environment = Create(width, height, PixelInternalFormat.Rgba16f, "gEnvironment");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Configures non-mipmapped attachments so raw-ID readers and sampler-based readers agree.</summary>
    private DynamicTexture2D Create(int width, int height, PixelInternalFormat format, string name)
    {
        var texture = resources.Own(DynamicTexture2D.Create(width, height, format, debugName: name));
        texture.DisableMipmaps();
        texture.SetTexFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        texture.SetTexWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
        return texture;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases all attachments, including a partially allocated set.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
