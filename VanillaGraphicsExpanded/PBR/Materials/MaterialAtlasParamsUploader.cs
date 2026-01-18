using System;

namespace VanillaGraphicsExpanded.PBR.Materials;

/// <summary>
/// Uploads CPU-generated material params tiles into the GPU material params atlas textures.
/// </summary>
internal sealed class MaterialAtlasParamsUploader
{
    private readonly MaterialAtlasTextureStore textureStore;

    public MaterialAtlasParamsUploader(MaterialAtlasTextureStore textureStore)
    {
        this.textureStore = textureStore ?? throw new ArgumentNullException(nameof(textureStore));
    }

    public bool TryUploadTile(int atlasTextureId, AtlasRect rect, float[] rgbTriplets)
    {
        ArgumentNullException.ThrowIfNull(rgbTriplets);

            if (!textureStore.TryGetPageTextures(atlasTextureId, out MaterialAtlasPageTextures pageTextures))
        {
            return false;
        }

        pageTextures.MaterialParamsTexture.UploadData(
            rgbTriplets,
            rect.X,
            rect.Y,
            rect.Width,
            rect.Height);

        return true;
    }
}
