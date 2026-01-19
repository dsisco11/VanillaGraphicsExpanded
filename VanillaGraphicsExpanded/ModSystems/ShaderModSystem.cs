using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

public sealed class ShaderModSystem : ModSystem
{
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void AssetsLoaded(ICoreAPI api)
    {
        // Initialize the shader includes hook with dependencies
        ShaderIncludesHook.Initialize(api.Logger, api.Assets);

        // Initialize the shader imports system to load mod shader imports (shaders/includes)
        ShaderImportsSystem.Instance.Initialize(api);
    }

    public override void Dispose()
    {
        base.Dispose();

        // Clear shader imports cache
        ShaderImportsSystem.Instance.Clear();
    }
}
