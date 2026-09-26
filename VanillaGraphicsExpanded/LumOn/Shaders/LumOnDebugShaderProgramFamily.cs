using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using VanillaGraphicsExpanded.Rendering.Contracts;


using Vintagestory.API.Client;

using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.LumOn;

/// <summary>Declares debug settings eagerly and prepares executables only when a view selects them.</summary>
internal static class LumOnDebugShaderProgramFamily
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, LumOnDebugShaderProgram> Entries = new(StringComparer.Ordinal);
    private static ICoreClientAPI? ownerApi;

    #region Declaration and selection
    /// <summary>Refreshes declarations after asset reload without linking unused debug programs.</summary>
    public static bool Register(ICoreClientAPI api)
    {
        lock (Sync)
        {
            if (!ReferenceEquals(ownerApi, api))
            {
                foreach (var entry in Entries.Values) if (ownerApi != null) GpuShaderPrograms.Remove(ownerApi, entry.PassName);
                Entries.Clear();
                ownerApi = api;
            }
            foreach (var contract in LumOnDebugShaderProgram.Contracts)
            {
                if (Entries.TryGetValue(contract.Identity, out var entry) && !entry.IsRetired) entry.InvalidateAssets();
                else Entries[contract.Identity] = GpuShaderPrograms.Declare(api, new LumOnDebugShaderProgram { PassName = contract.Identity });
            }
            return true;
        }
    }

    /// <summary>Finds a settings declaration without triggering engine asset lookup or executable creation.</summary>
    public static bool TryGet(string programName, out LumOnDebugShaderProgram program)
    {
        lock (Sync)
        {
            if (Entries.TryGetValue(programName, out var entry))
            {
                program = entry;
                return true;
            }
            program = null!;
            return false;
        }
    }

    /// <summary>Prepares a selected declaration only after its current view settings have been applied.</summary>
    internal static bool EnsureReady(ICoreClientAPI api, LumOnDebugShaderProgram program)
    {
        lock (Sync)
            return ReferenceEquals(ownerApi, api) && Entries.TryGetValue(program.PassName, out var entry) &&
                ReferenceEquals(entry, program) && program.EnsureReady();
    }

    /// <summary>Returns an immutable declaration snapshot for settings updates, including never-selected members.</summary>
    public static System.Collections.Immutable.ImmutableArray<LumOnDebugShaderProgram> GetAll()
    {
        lock (Sync) return Entries.Values.ToImmutableArray();
    }
    #endregion

    #region Lifetime
    /// <summary>Releases the current application's family without affecting a newer API owner.</summary>
    internal static void Dispose(ICoreClientAPI api)
    {
        lock (Sync)
        {
            if (!ReferenceEquals(ownerApi, api)) return;
            foreach (var entry in Entries.Values) if (ownerApi != null) GpuShaderPrograms.Remove(ownerApi, entry.PassName);
            Entries.Clear();
            ownerApi = null;
        }
    }
    #endregion

    #region Shared settings
    /// <summary>Updates declared composite settings without linking inactive debug views.</summary>
    public static void ApplyCompositeDefines(bool enablePbrComposite, bool enableAo, bool enableShortRangeAo)
    {
        // Apply only to programs explicitly accepting the composite settings group.
        // Owners retain pending selections; only EnsureReady for the selected view may create GPU work.
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
    /// Returns <c>true</c> if the active declaration kept the same effective settings.
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
            // Report whether the selected declaration needs a new generation on its next EnsureReady.
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
    #endregion
}
