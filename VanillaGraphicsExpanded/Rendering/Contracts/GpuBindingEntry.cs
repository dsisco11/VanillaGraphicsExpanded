namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Identifies one resource in a generated contract using a stable internal table index.</summary>
internal readonly record struct GpuBindingEntry(int Index, ShaderBindingKind Kind, string Name, GpuBindingContract.Binding Binding)
{
    /// <summary>Creates a numeric authored identity independent of variant table ordering and GPU slots.</summary>
    internal static ulong Identity(ShaderBindingKind kind, string name)
    {
        // FNV-1a provides a deterministic identifier; preparation rejects collisions rather than choosing an entry.
        ulong value = 14695981039346656037UL;
        unchecked
        {
            value = (value ^ (uint)kind) * 1099511628211UL;
            foreach (char character in name) value = (value ^ character) * 1099511628211UL;
        }
        return value;
    }
}
