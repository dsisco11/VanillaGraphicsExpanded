using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Engine-independent binding declarations shared by program layouts and the shader compiler.</summary>
public sealed class GpuBindingContract
{
    /// <summary>A resource slot and whether its absence should be reported by runtime diagnostics.</summary>
    public readonly record struct Binding(int Slot, bool Required);
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
    /// <summary>Copies mutable declarations into a binding contract whose dictionaries reject mutation.</summary>
    internal GpuBindingContract Snapshot() => new()
    {
        UniformBlocks = Freeze(UniformBlocks), StorageBlocks = Freeze(StorageBlocks),
        Samplers = Freeze(Samplers), Images = Freeze(Images), UniformLocations = Freeze(UniformLocations),
        VaryingLocations = Freeze(VaryingLocations), FragmentOutputLocations = Freeze(FragmentOutputLocations)
    };

    /// <summary>Freezes one independently owned declaration map.</summary>
    private static IDictionary<string, T> Freeze<T>(IDictionary<string, T> source) =>
        new ReadOnlyDictionary<string, T>(new Dictionary<string, T>(source, StringComparer.Ordinal));

    /// <summary>Compares all resource and interface declarations when a stage identity is shared.</summary>
    internal bool Equivalent(GpuBindingContract other) => Same(UniformBlocks, other.UniformBlocks) &&
        Same(StorageBlocks, other.StorageBlocks) && Same(Samplers, other.Samplers) && Same(Images, other.Images) &&
        Same(UniformLocations, other.UniformLocations) && Same(VaryingLocations, other.VaryingLocations) &&
        Same(FragmentOutputLocations, other.FragmentOutputLocations);

    /// <summary>Compares maps without relying on declaration order.</summary>
    private static bool Same<T>(IDictionary<string, T> first, IDictionary<string, T> second) =>
        first.Count == second.Count && first.All(p => second.TryGetValue(p.Key, out var value) && EqualityComparer<T>.Default.Equals(p.Value, value));
    #endregion
}
