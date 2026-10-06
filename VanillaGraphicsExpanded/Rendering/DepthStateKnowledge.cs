using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached DepthState values.</summary>
[Flags]
internal enum DepthStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached TestEnabled value is known.</summary>
    TestEnabled = 1 << 0,
    /// <summary>The cached Comparison value is known.</summary>
    Comparison = 1 << 1,
    /// <summary>The cached WriteEnabled value is known.</summary>
    WriteEnabled = 1 << 2,
    /// <summary>The cached DepthRange value is known.</summary>
    DepthRange = 1 << 3,
    /// <summary>Every field in this category is known.</summary>
    All = TestEnabled | Comparison | WriteEnabled | DepthRange
}
