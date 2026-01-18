using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Materials.Async;

/// <summary>
/// Represents a render-thread GPU update (uploads/writes into atlas textures).
/// </summary>
internal interface IMaterialAtlasGpuJob
{
    int GenerationId { get; }

    int AtlasTextureId { get; }

    int Priority { get; }

    void Execute(ICoreClientAPI capi, System.Func<int, PbrMaterialAtlasPageTextures?> tryGetPageTextures, PbrMaterialAtlasBuildSession session);
}
