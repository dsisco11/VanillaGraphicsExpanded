using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Clouds;

/// <summary>Owns a static hardware-filtered volume shared by all density consumers in one cloud lifetime.</summary>
internal sealed class CloudPerturbationTexture : GpuTexture
{
    #region Public API
    /// <summary>Validates and uploads a complete replacement before exposing its texture name.</summary>
    internal static CloudPerturbationTexture Create()
    {
        GpuSupport.EnsureCurrentContext();
        if(GpuSupport.Max3DTextureSize<CloudPerturbationAsset.Side || GpuSupport.MaxTextureImageUnits<=11)
            throw new NotSupportedException("Cloud perturbation requires a 64-cubed volume and sampler unit eleven.");
        var asset = CloudPerturbationAsset.Load();
        var texture = new CloudPerturbationTexture();
        try
        {
            texture.width = texture.height = texture.depth = CloudPerturbationAsset.Side;
            texture.internalFormat = PixelInternalFormat.Rgba8;
            texture.textureTarget = TextureTarget.Texture3D;
            texture.filterMode = TextureFilterMode.Linear;
            texture.debugName = "Cloud.Density.Perturbation";
            texture.AllocateOrReallocate3DTexture();
            texture.SetTexWrap(TextureWrapMode.Repeat, TextureWrapMode.Repeat, TextureWrapMode.Repeat);
            texture.UploadDataImmediate(asset.Pixels, 0, 0, 0, texture.width, texture.height, texture.depth);
            return texture;
        }
        catch { texture.Dispose(); throw; }
    }
    #endregion
    #region Private
    /// <summary>Restricts construction to complete validated uploads.</summary>
    private CloudPerturbationTexture() { }
    #endregion
}
