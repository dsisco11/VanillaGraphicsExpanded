using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using VanillaGraphicsExpanded.PBR;
using VanillaGraphicsExpanded.Rendering.ShaderCompilation;
using Vintagestory.API.Client;

namespace VanillaGraphicsExpanded.Rendering.Shaders;

/// <summary>Owns application shader declarations independently of the engine's executable registry.</summary>
internal sealed class GpuShaderPrograms
{
    private static readonly ConditionalWeakTable<ICoreClientAPI, GpuShaderPrograms> Libraries = new();
    private readonly Dictionary<string, GpuProgram> programs = new(StringComparer.Ordinal);

    #region Declarations
    /// <summary>Declares an owner without reading assets, linking, or invoking engine shader lookup.</summary>
    internal static T Declare<T>(ICoreClientAPI api, T candidate) where T : GpuProgram
    {
        var library = Libraries.GetValue(api, _ => new GpuShaderPrograms());
        string name = candidate.PassName;
        if (string.IsNullOrEmpty(name)) candidate.PassName = name = candidate.ProgramContract.Identity;
        if (library.programs.TryGetValue(name, out var existing) && !existing.IsRetired)
        {
            candidate.Dispose();
            existing.InvalidateAssets();
            return (T)existing;
        }
        candidate.Initialize(api);
        candidate.RegisterOnPreparation();
        library.programs[name] = candidate;
        return candidate;
    }

    /// <summary>Returns a declaration; preparation remains the consumer's explicit responsibility.</summary>
    internal static T? Get<T>(ICoreClientAPI api, string name) where T : GpuProgram =>
        Libraries.TryGetValue(api, out var library) && library.programs.TryGetValue(name, out var program)
            ? program as T : null;

    /// <summary>Captures declarations for explicit preload selection and configuration.</summary>
    internal static ImmutableArray<GpuProgram> GetAll(ICoreClientAPI api) =>
        Libraries.TryGetValue(api, out var library) ? library.programs.Values.ToImmutableArray() : [];
    #endregion

    #region Preparation and lifetime
    /// <summary>Removes one declaration and permanently retires its executable owner.</summary>
    internal static void Remove(ICoreClientAPI api, string name)
    {
        if (Libraries.TryGetValue(api, out var library) && library.programs.Remove(name, out var program))
            program.Dispose();
    }

    /// <summary>Batches only requested stale programs after the caller has supplied their intended settings.</summary>
    internal static bool Preload(ICoreClientAPI api, ImmutableArray<GpuProgram> selection)
    {
        bool success = true;
        foreach (var group in selection.Distinct().Where(program => !program.IsRetired && program.RequiresPreparation && program.CanSubmitPreparation).GroupBy(program =>
            string.IsNullOrWhiteSpace(program.AssetDomain) ? ShaderImportsSystem.DefaultDomain : program.AssetDomain))
        {
            using var batch = new ShaderLinkBatch(api.Assets, group.Key, group.Select(program => program.RequestedSettings));
            foreach (var program in group) success &= program.EnsureReady();
        }
        return success && selection.All(program => !program.RequiresPreparation);
    }

    /// <summary>Releases this application's declarations and executable owners without touching another API.</summary>
    internal static void Dispose(ICoreClientAPI api)
    {
        if (!Libraries.TryGetValue(api, out var library)) return;
        foreach (var program in library.programs.Values) program.Dispose();
        library.programs.Clear();
        Libraries.Remove(api);
    }
    #endregion
}

