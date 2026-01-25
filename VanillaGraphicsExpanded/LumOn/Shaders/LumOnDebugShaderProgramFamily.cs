using System;
using System.Collections.Generic;
using System.Globalization;

using Vintagestory.API.Client;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>
/// Registers and manages the LumOn debug shader program family (one program per debug program kind).
///
/// Responsibility:
/// - Register all debug programs as memory shader programs
/// - Provide helper APIs to apply shared define state across the family
/// </summary>
internal static class LumOnDebugShaderProgramFamily
{
    private static readonly object Sync = new();

    private static readonly Dictionary<string, LumOnDebugShaderProgram> ProgramsByName = new(StringComparer.Ordinal);

    private static readonly string[] ProgramNames =
    [
        // Legacy dispatcher (kept for compatibility)
        "lumon_debug",

        // Per-program-kind entrypoints
        "lumon_debug_probe_anchors",
        "lumon_debug_gbuffer",
        "lumon_debug_temporal",
        "lumon_debug_sh",
        "lumon_debug_indirect",
        "lumon_debug_probe_atlas",
        "lumon_debug_composite",
        "lumon_debug_direct",
        "lumon_debug_velocity",
        "lumon_debug_worldprobe",
    ];

    public static void Register(ICoreClientAPI api)
    {
        lock (Sync)
        {
            ProgramsByName.Clear();

            foreach (var passName in ProgramNames)
            {
                var instance = new LumOnDebugShaderProgram
                {
                    PassName = passName,
                    AssetDomain = "vanillagraphicsexpanded"
                };

                instance.Initialize(api);
                instance.CompileAndLink();

                api.Shader.RegisterMemoryShaderProgram(passName, instance);
                ProgramsByName[passName] = instance;
            }
        }
    }

    public static bool TryGet(string programName, out LumOnDebugShaderProgram program)
    {
        lock (Sync)
        {
            return ProgramsByName.TryGetValue(programName, out program!);
        }
    }

    public static IEnumerable<LumOnDebugShaderProgram> GetAll()
    {
        lock (Sync)
        {
            // Snapshot so callers can iterate without holding the lock.
            return new List<LumOnDebugShaderProgram>(ProgramsByName.Values);
        }
    }

    public static void ApplyCompositeDefines(bool enablePbrComposite, bool enableAo, bool enableShortRangeAo)
    {
        // Defines are compile-time; apply consistently to all programs.
        // We intentionally do not gate rendering if this triggers recompiles; these toggles are rare.
        foreach (var program in GetAll())
        {
            program.SetDefine(VgeShaderDefines.LumOnPbrComposite, enablePbrComposite ? "1" : "0");
            program.SetDefine(VgeShaderDefines.LumOnEnableAo, enableAo ? "1" : "0");
            program.SetDefine(VgeShaderDefines.LumOnEnableShortRangeAo, enableShortRangeAo ? "1" : "0");
        }
    }

    /// <summary>
    /// Applies world-probe topology defines across all programs.
    /// Returns <c>true</c> if the active program did not change defines (i.e. no recompile queued).
    /// </summary>
    public static bool ApplyWorldProbeClipmapDefines(
        bool enabled,
        float baseSpacing,
        int levels,
        int resolution,
        LumOnDebugShaderProgram? activeProgram)
    {
        // Normalize inputs exactly once so every program gets the same variant key.
        if (!enabled)
        {
            baseSpacing = 0;
            levels = 0;
            resolution = 0;
        }

        string baseSpacingStr = baseSpacing.ToString("0.0####", CultureInfo.InvariantCulture);
        string levelsStr = levels.ToString(CultureInfo.InvariantCulture);
        string resolutionStr = resolution.ToString(CultureInfo.InvariantCulture);
        string enabledStr = enabled ? "1" : "0";

        bool activeStable = true;

        foreach (var program in GetAll())
        {
            bool stable = true;

            // Apply the same define set to every program.
            // For the currently used program we additionally return whether this queued a recompile.
            bool changed = false;
            changed |= program.SetDefine(VgeShaderDefines.LumOnWorldProbeEnabled, enabledStr);
            changed |= program.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapLevels, levelsStr);
            changed |= program.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapResolution, resolutionStr);
            changed |= program.SetDefine(VgeShaderDefines.LumOnWorldProbeClipmapBaseSpacing, baseSpacingStr);
            stable = !changed;

            if (ReferenceEquals(program, activeProgram))
            {
                activeStable = stable;
            }
        }

        return activeStable;
    }
}
