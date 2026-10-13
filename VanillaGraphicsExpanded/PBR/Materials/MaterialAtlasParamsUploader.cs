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

        return textureStore.TryUploadMaterialParamsTile(atlasTextureId, rect, rgbTriplets, transmission);
    }
}
