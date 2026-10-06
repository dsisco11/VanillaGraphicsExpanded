using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Spirv;

/// <summary>Owns the active numeric interface of one linked program; slots are fixed by its compiled contract.</summary>
internal sealed class GpuProgramInterface
{
    private readonly Resources resources;
    private readonly IReadOnlyDictionary<ShaderBindingKind, IReadOnlyDictionary<string, int>> activeBindings;
    private readonly Dictionary<(ProgramInterface Kind, int Index), string> resourceNames = new();
    private readonly Dictionary<int, string> uniformNames = new();
    /// <summary>Validated resource entries owned by this executable's interface generation.</summary>
    internal GpuPreparedBindings PreparedBindings { get; }
    /// <summary>Graphics-only linked interface metadata, installed with this same resource-interface candidate.</summary>
    internal GraphicsExecutableInterface? Graphics { get; }
    /// <summary>Active locations and diagnostic resource indices belonging to this program.</summary>
    private sealed record Resources(Dictionary<string, int> Uniforms, Dictionary<string, int> Blocks, Dictionary<string, int> Storage, Dictionary<int, int> ArraySizes);

    #region Registration
    /// <summary>Matches active locations and initial block bindings without querying any SPIR-V debug name.</summary>
    public GpuProgramInterface(int program, IEnumerable<GpuBindingContract> contracts, GraphicsExecutableInterface? graphics = null)
    {
        Graphics = graphics;
        var array = contracts.ToArray();
        PreparedBindings = new GpuPreparedBindings(program, GpuBindingContract.Merge(array));
        var arraySizes = new Dictionary<int, int>();
        var uniforms = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in array.SelectMany(v => v.UniformLocations))
        {
            uniforms[pair.Key] = PreparedBindings.ContainsUniformLocation(pair.Value) ? pair.Value : -1;
            arraySizes[pair.Value] = PreparedBindings.UniformArrayLength(pair.Value);
        }
        // Engine Uniform/HasUniform consumers retain addresses reconstructed from validated units.
        // These projections never participate in generated resource submission.
        foreach (var entry in PreparedBindings.Entries)
        {
            if (entry.Contract.Kind is not (ShaderBindingKind.Sampler or ShaderBindingKind.Image)) continue;
            int location = entry.Active ? PreparedBindings.TextureUniformLocation(entry.Contract.Kind, entry.Contract.Binding.Slot) : -1;
            uniforms[entry.Contract.Name] = location;
            if (location >= 0) arraySizes[location] = PreparedBindings.UniformArrayLength(location);
        }
        resources = new(uniforms, Blocks(ShaderBindingKind.UniformBlock, array.SelectMany(v => v.UniformBlocks)),
            Blocks(ShaderBindingKind.StorageBlock, array.SelectMany(v => v.StorageBlocks)), arraySizes);
        foreach (var uniform in uniforms)
            if (uniform.Value >= 0) uniformNames.TryAdd(uniform.Value, uniform.Key);
        // Compatibility caches are projections of validated activity, never a second driver inspection.
        activeBindings = new ReadOnlyDictionary<ShaderBindingKind, IReadOnlyDictionary<string, int>>(
            PreparedBindings.Entries.GroupBy(entry => entry.Contract.Kind).ToDictionary(group => group.Key,
                group => (IReadOnlyDictionary<string, int>)new ReadOnlyDictionary<string, int>(group.Where(entry => entry.Active)
                    .ToDictionary(entry => entry.Contract.Name, entry => entry.Contract.Binding.Slot, StringComparer.Ordinal))));
        foreach (var block in resources.Blocks)
            if (block.Value >= 0) resourceNames.TryAdd((ProgramInterface.UniformBlock, block.Value), block.Key);
        foreach (var block in resources.Storage)
            if (block.Value >= 0) resourceNames.TryAdd((ProgramInterface.ShaderStorageBlock, block.Value), block.Key);
    }

    /// <summary>Matches compiled binding slots to active resource indices for diagnostics.</summary>
    private Dictionary<string, int> Blocks(ShaderBindingKind kind, IEnumerable<KeyValuePair<string, GpuBindingContract.Binding>> metadata)
    {
        return metadata.GroupBy(p => p.Key).ToDictionary(g => g.Key,
            g => PreparedBindings.BlockResourceIndex(kind, g.First().Value.Slot), StringComparer.Ordinal);
    }

    #endregion

    #region Lookup
    /// <summary>Projects validated active bindings for existing layout caches without driver queries.</summary>
    internal IReadOnlyDictionary<string, int> ActiveBindings(ShaderBindingKind kind) =>
        activeBindings.TryGetValue(kind, out var bindings) ? bindings : EmptyBindings;

    private static readonly IReadOnlyDictionary<string, int> EmptyBindings =
        new ReadOnlyDictionary<string, int>(new Dictionary<string, int>());

    /// <summary>Returns active standalone locations from the compiled interface.</summary>
    public int GetUniformLocation(string name)
    {
        UniformNameResolutions++;
        if (resources.Uniforms.TryGetValue(name, out int location)) return location;
        int bracket = name.IndexOf('[');
        if (bracket > 0 && name.EndsWith(']') && int.TryParse(name[(bracket + 1)..^1], out int index) &&
            resources.Uniforms.TryGetValue(name[..bracket], out location) && location >= 0 && index >= 0 &&
            index < resources.ArraySizes.GetValueOrDefault(location)) return location + index;
        return -1;
    }
    /// <summary>Counts diagnostic and engine named-address requests independently of preparation.</summary>
    internal long UniformNameResolutions { get; private set; }
    /// <summary>Returns a uniform block's numeric index, independent of optional shader names.</summary>
    public int GetUniformBlockIndex(string name) => resources.Blocks.GetValueOrDefault(name, -1);
    /// <summary>Returns a storage block's numeric index, independent of optional shader names.</summary>
    public int GetStorageBlockIndex(string name) => resources.Storage.GetValueOrDefault(name, -1);
    /// <summary>Resolves resource names from the generated contract for diagnostic cache enumeration.</summary>
    public void GetProgramResourceName(ProgramInterface kind, int index, int capacity, out int length, out string name)
    {
        if (kind == ProgramInterface.Uniform)
        {
            int location = PreparedBindings.UniformResourceLocation(index);
            name = location < 0 ? "" : uniformNames.GetValueOrDefault(location, "");
        }
        else name = resourceNames.GetValueOrDefault((kind, index), "");
        length = name.Length;
    }
    /// <summary>Provides the inactive entries required by the engine's indexed uniform dictionary.</summary>
    public IReadOnlyDictionary<string, int> Uniforms => resources.Uniforms;
    #endregion
}


