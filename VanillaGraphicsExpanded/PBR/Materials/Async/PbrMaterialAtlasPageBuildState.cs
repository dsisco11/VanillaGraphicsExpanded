using System;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

internal sealed class PbrMaterialAtlasPageBuildState
{
    public PbrMaterialAtlasPageBuildState(int atlasTextureId, int width, int height)
    {
        if (atlasTextureId == 0) throw new ArgumentOutOfRangeException(nameof(atlasTextureId));
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        AtlasTextureId = atlasTextureId;
        Width = width;
        Height = height;
    }

    public int AtlasTextureId { get; }

    public int Width { get; }

    public int Height { get; }

    public int PendingTiles { get; set; }

    public int InFlightTiles { get; set; }

    public int CompletedTiles { get; set; }

    public int PendingOverrides { get; set; }

    public int CompletedOverrides { get; set; }

    public bool PageClearDone { get; set; }
}
