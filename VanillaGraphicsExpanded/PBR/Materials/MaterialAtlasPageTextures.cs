using System;

using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.PBR.Materials;

internal sealed class MaterialAtlasPageTextures : IDisposable
{
    public MaterialAtlasPageTextures(Texture2D materialParamsTexture, Texture2D? normalDepthTexture)
    {
        MaterialParamsTexture = materialParamsTexture ?? throw new ArgumentNullException(nameof(materialParamsTexture));
        NormalDepthTexture = normalDepthTexture;
    }

    public Texture2D MaterialParamsTexture { get; }

    public Texture2D? NormalDepthTexture { get; }

    public void Dispose()
    {
        MaterialParamsTexture.Dispose();
        NormalDepthTexture?.Dispose();
    }
}
