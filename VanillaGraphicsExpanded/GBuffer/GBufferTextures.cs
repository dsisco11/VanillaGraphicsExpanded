using VanillaGraphicsExpanded.Rendering.RHI;
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
            Surface = resources.Own(RHICommandContext.Current.CreateTexture3D(new RHITextureDescription { Dimension = TextureDimension.TwoArray, Format = TextureFormat.Rgba16f, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), Depth = System.Math.Max(1, 3), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = "GBuffer.Surface" }).RequireResource());
            PatchId = Create(width, height, TextureFormat.Rgba32ui, "gPatchId");
        }
        catch { Dispose(); throw; }
    }

    /// <summary>Configures non-mipmapped attachments so raw-ID readers and sampler-based readers agree.</summary>
    private DynamicTexture2D Create(int width, int height, TextureFormat format, string name)
    {
        var texture = resources.Own(RHICommandContext.Current.CreateDynamicTexture2D(new RHITextureDescription { Dimension = TextureDimension.Two, Format = format, Width = System.Math.Max(1, width), Height = System.Math.Max(1, height), MipLevels = 1, Storage = ResourceStorageMode.Mutable, Filter = TextureFilterMode.Nearest, BaseLevel = 0, MaxLevel = (1) - 1, Label = name }).RequireResource());
        texture.DisableMipmaps();
        texture.SetTexFilter(SamplerFilter.Nearest, SamplerFilter.Nearest);
        texture.SetTexWrap(SamplerAddressMode.ClampToEdge, SamplerAddressMode.ClampToEdge);
        return texture;
    }
    #endregion

    #region Lifetime
    /// <summary>Releases all attachments, including a partially allocated set.</summary>
    public void Dispose() => resources.Dispose();
    #endregion
}
