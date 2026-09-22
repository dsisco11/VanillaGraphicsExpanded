using System;
using System.Collections.Generic;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Engine-independent binding declarations shared by program layouts and the shader compiler.</summary>
public sealed class GpuBindingContract
{
    /// <summary>A resource slot and whether its absence should be reported by runtime diagnostics.</summary>
    public readonly record struct Binding(int Slot, bool Required);
    public Dictionary<string, Binding> UniformBlocks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Binding> StorageBlocks { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Binding> Samplers { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, Binding> Images { get; } = new(StringComparer.Ordinal);

    /// <summary>Explicit standalone uniform locations shared by compatible stages.</summary>
    public Dictionary<string, int> UniformLocations { get; } = new(StringComparer.Ordinal);
    /// <summary>Explicit locations for values passed between shader stages.</summary>
    public Dictionary<string, int> VaryingLocations { get; } = new(StringComparer.Ordinal);
    /// <summary>Default color attachment locations for outputs without source layout declarations.</summary>
    public Dictionary<string, int> FragmentOutputLocations { get; } = new(StringComparer.Ordinal);

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
}
