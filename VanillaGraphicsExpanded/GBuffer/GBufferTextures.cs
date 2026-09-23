using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded;

/// <summary>Owns terrain normal, material and patch-identity render targets independently of framebuffer injection.</summary>
internal sealed class GBufferTextures : IDisposable
{
    public DynamicTexture2D Normal { get; }
    public DynamicTexture2D Material { get; }
    public DynamicTexture2D PatchId { get; }

    #region Allocation
    /// <summary>Allocates companion terrain targets with the sampling policy used by raw-ID consumers.</summary>
    public GBufferTextures(int width, int height)
    {
        try
        {
            Normal = Create(width, height, PixelInternalFormat.Rgba16f, "gNormal");
            Material = Create(width, height, PixelInternalFormat.Rgba16f, "gMaterial");
            PatchId = Create(width, height, PixelInternalFormat.Rgba32ui, "gPatchId");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Configures non-mipmapped attachments so raw-ID readers and sampler-based readers agree.</summary>
    private static DynamicTexture2D Create(int width, int height, PixelInternalFormat format, string name)
    {
        var texture = DynamicTexture2D.Create(width, height, format, debugName: name);
        texture.DisableMipmaps();
        texture.SetTexFilter(TextureMinFilter.Nearest, TextureMagFilter.Nearest);
        texture.SetTexWrap(TextureWrapMode.ClampToEdge, TextureWrapMode.ClampToEdge);
        return texture;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases all attachments, including a partially allocated set.</summary>
    public void Dispose() { Normal?.Dispose(); Material?.Dispose(); PatchId?.Dispose(); }
    #endregion
}
