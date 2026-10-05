using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached PrimitiveAssemblyState values.</summary>
[Flags]
internal enum PrimitiveAssemblyStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached PatchVertices value is known.</summary>
    PatchVertices = 1 << 0,
    /// <summary>Every field in this category is known.</summary>
    All = PatchVertices
}
