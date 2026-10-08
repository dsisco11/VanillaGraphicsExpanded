using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded;

/// <summary>Owns terrain normal, material, patch-identity and environment render targets independently of framebuffer injection.</summary>
internal sealed class GBufferTextures : IDisposable
{
    private readonly GpuResourceCollection resources = new();

    /// <summary>Layer indices used by terrain MRT attachments and surface consumers.</summary>
    public const int NormalLayer = 0, MaterialLayer = 1, EnvironmentLayer = 2;

    /// <summary>Shared storage for normal, material and environmental-light data.</summary>
    public Texture3D Surface { get; }
    /// <summary>Separate integer storage for exact terrain patch identities.</summary>
    public DynamicTexture2D PatchId { get; }

    #region Allocation
    /// <summary>Allocates companion terrain targets with the sampling policy used by raw-ID consumers.</summary>
    public GBufferTextures(int width, int height)
    {
        try
        {
            Surface = resources.Own(Texture3D.Create(width, height, 3, PixelInternalFormat.Rgba16f,
                TextureFilterMode.Nearest, TextureTarget.Texture2DArray, "GBuffer.Surface"));
            PatchId = Create(width, height, PixelInternalFormat.Rgba32ui, "gPatchId");
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
