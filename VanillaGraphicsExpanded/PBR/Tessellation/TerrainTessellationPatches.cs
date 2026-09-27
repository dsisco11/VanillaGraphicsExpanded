using System;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.ModSystems;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Owns tessellation source templates prepared by the engine shader patch pipeline.</summary>
internal static class TerrainTessellationPatches
{
    private static readonly ConditionalWeakTable<IShader, TerrainTessellationStages.Sources> sources = new();
    internal const string EnabledDefine = "\n#undef VGE_ENABLE_TESSELLATION\n#define VGE_ENABLE_TESSELLATION 1\n";
    private const string DisabledDefine = "\n#undef VGE_ENABLE_TESSELLATION\n#define VGE_ENABLE_TESSELLATION 0\n";

    #region Source and compilation lifecycle
    /// <summary>Captures the final patched interface once; unsupported declarations retain ordinary rendering.</summary>
    internal static void Prepare(IShaderProgram program)
    {
        if (!TerrainTessellationPrograms.Eligible(program.PassName) || program.AssetDomain == Constants.ModId) return;
        sources.Remove(program.VertexShader);
        try
        {
            sources.Add(program.VertexShader, TerrainTessellationStages.Generate(program.VertexShader.Code));
        }
        catch (Exception ex)
        {
            TerrainTessellationPrograms.Log?.Invoke($"[VGE] {program.PassName}: tessellation source preparation skipped: {ex.Message}");
        }
    }

    /// <summary>Sets the engine prefix before compilation without accumulating defines on repeated compiles.</summary>
    internal static void Configure(IShaderProgram program)
    {
        if (!TerrainTessellationPrograms.Eligible(program.PassName) || program.AssetDomain == Constants.ModId) return;
        bool enabled = ConfigModSystem.Config.MaterialAtlas.UndisplacedTessellationLevel > 0
            && TerrainTessellationPrograms.DrawHookAvailable && program.GeometryShader is null
            && sources.TryGetValue(program.VertexShader, out _);
        // Installed IShader exposes PrefixCode rather than SetDefine. The engine injects this
        // immediately after #version; preserve all other owners' defines and use it for TCS/TES too.
        foreach (var shader in new[] { program.VertexShader, program.FragmentShader })
        {
            string prefix = (shader.PrefixCode ?? "").Replace(EnabledDefine, "").Replace(DisabledDefine, "");
            shader.PrefixCode = prefix + (enabled ? EnabledDefine : DisabledDefine);
        }
    }

    /// <summary>Returns only metadata published during source patching; linking never discovers interfaces.</summary>
    internal static bool TryGet(IShader shader, out TerrainTessellationStages.Sources result) => sources.TryGetValue(shader, out result!);
    #endregion
}
