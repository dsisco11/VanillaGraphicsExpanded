using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;

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

    private static readonly string[] ProgramNames = LumOnDebugShaderProgram.Contracts.Select(contract => contract.Identity).ToArray();

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
        // Apply only to programs explicitly accepting the composite settings group.
        // We intentionally do not gate rendering if this triggers recompiles; these toggles are rare.
        foreach (var program in GetAll().Where(p => p.ProgramContract.Groups.Contains(LumOnShaderGroups.Composite)))
        {
            program.SetShaderOptions(options =>
            {
                options.Set(LumOnShaderOptions.PbrComposite, enablePbrComposite);
                options.Set(LumOnShaderOptions.AmbientOcclusion, enableAo);
                options.Set(LumOnShaderOptions.ShortRangeAo, enableShortRangeAo);
            });
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


        bool activeStable = true;

        foreach (var program in GetAll().Where(p => p.ProgramContract.Groups.Contains(LumOnShaderGroups.World)))
        {
            bool stable = true;

            // Apply the topology settings to their declared consumers.
            // For the currently used program we additionally return whether this queued a recompile.
            bool changed = program.SetShaderOptions(options =>
            {
                options.Set(LumOnShaderOptions.WorldProbes, enabled);
                options.Set(LumOnShaderOptions.WorldProbeLevels, levels);
                options.Set(LumOnShaderOptions.WorldProbeResolution, resolution);
                options.Set(LumOnShaderOptions.WorldProbeBaseSpacing, baseSpacing);
            });
            stable = !changed;

            if (ReferenceEquals(program, activeProgram))
            {
                activeStable = stable;
            }
        }

        return activeStable;
    }
}
