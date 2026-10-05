using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached BlendState values.</summary>
[Flags]
internal enum BlendStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached Enabled value is known.</summary>
    Enabled = 1 << 0,
    /// <summary>The cached Factors value is known.</summary>
    Factors = 1 << 1,
    /// <summary>The cached WriteMask value is known.</summary>
    WriteMask = 1 << 2,
    /// <summary>Every field in this category is known.</summary>
    All = Enabled | Factors | WriteMask
}
