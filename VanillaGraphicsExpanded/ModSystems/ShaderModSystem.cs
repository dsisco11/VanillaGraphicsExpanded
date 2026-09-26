using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Owns client shader asset services and their lifetime boundaries.</summary>
public sealed class ShaderModSystem : ModSystem
{
    /// <summary>Installs shader services only on the client.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Starts a fresh shader asset lifetime and initializes source patching services.</summary>
    public override void AssetsLoaded(ICoreAPI api)
    {
        ShaderDigestIndexCache.Clear();
        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);

        // Initialize the shader imports system to load mod shader imports (shaders/includes)
        ShaderImportsSystem.Instance.Initialize(api);
    }

    /// <summary>Releases parsed indexes and shader source services at shutdown.</summary>
    public override void Dispose()
    {
        base.Dispose();
        ShaderDigestIndexCache.Clear();

        // Clear shader imports cache
        ShaderImportsSystem.Instance.Clear();
    }
}
