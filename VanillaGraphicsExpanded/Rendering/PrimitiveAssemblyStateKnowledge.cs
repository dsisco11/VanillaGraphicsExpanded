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
    /// <summary>The cached RestartIndex value is known.</summary>
    RestartIndex = 1 << 1,
    /// <summary>The cached PrimitiveRestart value is known.</summary>
    PrimitiveRestart = 1 << 2,
    /// <summary>The cached PrimitiveRestartFixedIndex value is known.</summary>
    PrimitiveRestartFixedIndex = 1 << 3,
    /// <summary>Every field in this category is known.</summary>
    All = PatchVertices | RestartIndex | PrimitiveRestart | PrimitiveRestartFixedIndex
}
