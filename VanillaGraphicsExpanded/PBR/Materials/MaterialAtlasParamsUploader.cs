using System;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Uploads CPU-generated material params tiles into the GPU material params atlas textures.
/// </summary>
internal sealed class MaterialAtlasParamsUploader
{
    private readonly MaterialAtlasTextureStore textureStore;

    /// <summary>Uses the owning atlas store to resolve upload destinations.</summary>
    public MaterialAtlasParamsUploader(MaterialAtlasTextureStore textureStore)
    {
        this.textureStore = textureStore ?? throw new ArgumentNullException(nameof(textureStore));
    }

    /// <summary>Publishes RGB properties with independently authored transmission in alpha.</summary>
    public bool TryUploadTile(int atlasTextureId, AtlasRect rect, float[] rgbTriplets, float transmission = 0)
    {
        ArgumentNullException.ThrowIfNull(rgbTriplets);

        if (!textureStore.TryGetPageTextures(atlasTextureId, out MaterialAtlasPageTextures pageTextures))
        {
            return false;
        }

        pageTextures.MaterialParamsTexture.UploadData(
            MaterialTransmission.Pack(rgbTriplets, transmission),
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height);

        return true;
    }
}
