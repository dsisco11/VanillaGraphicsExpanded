namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Identifies one resource in a generated contract using a stable internal table index.</summary>
internal readonly record struct GpuBindingEntry(int Index, ShaderBindingKind Kind, string Name, GpuBindingContract.Binding Binding);
