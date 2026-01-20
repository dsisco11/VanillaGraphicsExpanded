using System;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class MaterialAtlasPageTextures : IDisposable
{
    public MaterialAtlasPageTextures(DynamicTexture2D materialParamsTexture, DynamicTexture2D? normalDepthTexture)
    {
        MaterialParamsTexture = materialParamsTexture ?? throw new ArgumentNullException(nameof(materialParamsTexture));
        NormalDepthTexture = normalDepthTexture;
    }

    public DynamicTexture2D MaterialParamsTexture { get; }

    public DynamicTexture2D? NormalDepthTexture { get; }

    public void Dispose()
    {
        MaterialParamsTexture.Dispose();
        NormalDepthTexture?.Dispose();
    }
}
