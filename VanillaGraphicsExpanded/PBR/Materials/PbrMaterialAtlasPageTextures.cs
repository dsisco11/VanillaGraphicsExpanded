using System;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class PbrMaterialAtlasPageTextures : IDisposable
{
    public PbrMaterialAtlasPageTextures(DynamicTexture materialParamsTexture, DynamicTexture? normalDepthTexture)
    {
        MaterialParamsTexture = materialParamsTexture ?? throw new ArgumentNullException(nameof(materialParamsTexture));
        NormalDepthTexture = normalDepthTexture;
    }

    public DynamicTexture MaterialParamsTexture { get; }

    public DynamicTexture? NormalDepthTexture { get; }

    public void Dispose()
    {
        MaterialParamsTexture.Dispose();
        NormalDepthTexture?.Dispose();
    }
}
