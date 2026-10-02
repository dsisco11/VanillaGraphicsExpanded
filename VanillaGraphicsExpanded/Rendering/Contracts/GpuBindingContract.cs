using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Engine-independent binding declarations shared by program layouts and the shader compiler.</summary>
public sealed class GpuBindingContract
{
    /// <summary>A resource's fixed range, compatible type and publication policy; required inputs apply only while active.</summary>
    public readonly record struct Binding(int Slot, bool Required, int ArrayLength = 1, ShaderResourceType? ShaderType = null,
        int TextureTarget = 0, int Sampler = 0);

    /// <summary>Returns deterministic internal resource indices independently of GPU binding numbers.</summary>
    internal IReadOnlyList<GpuBindingEntry> Entries => Array.AsReadOnly(
        ResourceMaps().SelectMany(map => map.Values.Select(pair => (map.Kind, pair.Key, pair.Value)))
            .OrderBy(value => value.Kind).ThenBy(value => value.Key, StringComparer.Ordinal)
            .Select((value, index) => new GpuBindingEntry(index, value.Kind, value.Key, value.Value)).ToArray());
    /// <summary>Atomic-counter buffer slots named by one active counter uniform.</summary>
    public IDictionary<string, Binding> AtomicCounters { get; private init; } = new Dictionary<string, Binding>(StringComparer.Ordinal);
    public IDictionary<string, Binding> UniformBlocks { get; private init; } = new Dictionary<string, Binding>(StringComparer.Ordinal);
    public IDictionary<string, Binding> StorageBlocks { get; private init; } = new Dictionary<string, Binding>(StringComparer.Ordinal);
    public IDictionary<string, Binding> Samplers { get; private init; } = new Dictionary<string, Binding>(StringComparer.Ordinal);
    public IDictionary<string, Binding> Images { get; private init; } = new Dictionary<string, Binding>(StringComparer.Ordinal);

    /// <summary>Explicit standalone uniform locations shared by compatible stages.</summary>
    public IDictionary<string, int> UniformLocations { get; private init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    /// <summary>Explicit locations for values passed between shader stages.</summary>
    public IDictionary<string, int> VaryingLocations { get; private init; } = new Dictionary<string, int>(StringComparer.Ordinal);
    /// <summary>Default color attachment locations for outputs without source layout declarations.</summary>
    public IDictionary<string, int> FragmentOutputLocations { get; private init; } = new Dictionary<string, int>(StringComparer.Ordinal);

    #region Declarations
    /// <summary>Declares a uniform-buffer slot.</summary>
    public void RegisterUniformBlockBinding(string name, int bindingIndex, bool required = true) => UniformBlocks.Add(name, new(bindingIndex, required));
    /// <summary>Declares a storage-buffer slot.</summary>
    public void RegisterShaderStorageBlockBinding(string name, int bindingIndex, bool required = true) => StorageBlocks.Add(name, new(bindingIndex, required));
    /// <summary>Declares a texture unit.</summary>
    public void RegisterSamplerUnit(string name, int unit, bool required = true) => Samplers.Add(name, new(unit, required));
    /// <summary>Declares an image unit.</summary>
    public void RegisterImageUnit(string name, int unit, bool required = true) => Images.Add(name, new(unit, required));
    #endregion

    #region Immutable publication
    /// <summary>Merges compatible stage declarations and rejects conflicting names or occupied resource ranges.</summary>
    internal static GpuBindingContract Merge(IEnumerable<GpuBindingContract> contracts)
    {
        var result = new GpuBindingContract();
        foreach (var contract in contracts)
            foreach (var source in contract.ResourceMaps())
            {
                var target = result.ResourceMaps().Single(map => map.Kind == source.Kind).Values;
                foreach (var pair in source.Values)
                {
                    if (target.TryGetValue(pair.Key, out var previous) && previous != pair.Value)
                        throw new ArgumentException($"Incompatible {source.Kind} declaration '{pair.Key}' across shader stages.");
                    target[pair.Key] = pair.Value;
                }
            }
        foreach (var map in result.ResourceMaps())
        {
            KeyValuePair<string, Binding>? previous = null;
            foreach (var pair in map.Values.OrderBy(pair => pair.Value.Slot))
            {
                if (pair.Value.Slot < 0 || pair.Value.ArrayLength < 1 || pair.Value.Slot > int.MaxValue - pair.Value.ArrayLength)
                    throw new ArgumentException($"Invalid {map.Kind} range for '{pair.Key}'.");
                // Compare intervals rather than enumerating authored extents, which may be very large.
                if (previous is { } prior && pair.Value.Slot < prior.Value.Slot + prior.Value.ArrayLength)
                    throw new ArgumentException($"Overlapping {map.Kind} slot {pair.Value.Slot}: '{prior.Key}' and '{pair.Key}'.");
                previous = pair;
            }
        }
        return result.Snapshot();
    }

    /// <summary>Enumerates the independent resource namespaces without including diagnostic location defaults.</summary>
    private IEnumerable<(ShaderBindingKind Kind, IDictionary<string, Binding> Values)> ResourceMaps()
    {
        yield return (ShaderBindingKind.Sampler, Samplers);
        yield return (ShaderBindingKind.Image, Images);
        yield return (ShaderBindingKind.UniformBlock, UniformBlocks);
        yield return (ShaderBindingKind.StorageBlock, StorageBlocks);
        yield return (ShaderBindingKind.AtomicCounter, AtomicCounters);
    }

    /// <summary>Copies mutable declarations into a binding contract whose dictionaries reject mutation.</summary>
    internal GpuBindingContract Snapshot() => new()
    {
        AtomicCounters = Freeze(AtomicCounters), UniformBlocks = Freeze(UniformBlocks), StorageBlocks = Freeze(StorageBlocks),
        Samplers = Freeze(Samplers), Images = Freeze(Images), UniformLocations = Freeze(UniformLocations),
        VaryingLocations = Freeze(VaryingLocations), FragmentOutputLocations = Freeze(FragmentOutputLocations)
    };

    /// <summary>Freezes one independently owned declaration map.</summary>
    private static IDictionary<string, T> Freeze<T>(IDictionary<string, T> source) =>
        new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(source, StringComparer.Ordinal));

    /// <summary>Compares all resource and interface declarations when a stage identity is shared.</summary>
    internal bool Equivalent(GpuBindingContract other) => Same(AtomicCounters, other.AtomicCounters) && Same(UniformBlocks, other.UniformBlocks) &&
        Same(StorageBlocks, other.StorageBlocks) && Same(Samplers, other.Samplers) && Same(Images, other.Images) &&
        Same(UniformLocations, other.UniformLocations) && Same(VaryingLocations, other.VaryingLocations) &&
        Same(FragmentOutputLocations, other.FragmentOutputLocations);

    /// <summary>Compares maps without relying on declaration order.</summary>
    private static bool Same<T>(IDictionary<string, T> first, IDictionary<string, T> second) =>
        first.Count == second.Count && first.All(p => second.TryGetValue(p.Key, out var value) && EqualityComparer<T>.Default.Equals(p.Value, value));
    #endregion
}
