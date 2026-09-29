using VanillaGraphicsExpanded.HarmonyPatches;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Rendering.Shaders;
using Vintagestory.API.Client;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.ModSystems;

/// <summary>Owns client shader asset services and their lifetime boundaries.</summary>
public sealed class ShaderModSystem : ModSystem
{
    private ShaderPatchErrors? patchErrors;
    /// <summary>Installs shader services only on the client.</summary>
    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    /// <summary>Starts a fresh shader asset lifetime and initializes source patching services.</summary>
    public override void AssetsLoaded(ICoreAPI api)
    {
        ShaderDigestIndexCache.Clear();
        PbrShaderLightingMode.GenerationLumOnEnabled = null;
        // Initialize engine shader processing with its asset dependencies.
        EngineShaderProcessingHook.Initialize(api.Logger, api.Assets);
        patchErrors?.Dispose();
        patchErrors = new ShaderPatchErrors((ICoreClientAPI)api);
        EngineShaderProcessingHook.ReportError = patchErrors.Report;

        // Initialize the shader imports system to load mod shader imports (shaders/includes)
        ShaderImportsSystem.Instance.Initialize(api);
    }

    /// <summary>Releases parsed indexes and shader source services at shutdown.</summary>
    public override void Dispose()
    {
        base.Dispose();
        EngineShaderProcessingHook.ReportError = null;
        patchErrors?.Dispose();
        patchErrors = null;
        ShaderDigestIndexCache.Clear();
        PbrShaderLightingMode.GenerationLumOnEnabled = null;

        // Clear shader imports cache
        ShaderImportsSystem.Instance.Clear();
    }
}
