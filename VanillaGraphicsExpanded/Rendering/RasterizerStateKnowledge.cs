using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached RasterizerState values.</summary>
[Flags]
internal enum RasterizerStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached CullEnabled value is known.</summary>
    CullEnabled = 1 << 0,
    /// <summary>The cached ScissorEnabled value is known.</summary>
    ScissorEnabled = 1 << 1,
    /// <summary>The cached LineWidth value is known.</summary>
    LineWidth = 1 << 2,
    /// <summary>The cached PointSize value is known.</summary>
    PointSize = 1 << 3,
    /// <summary>The cached ProvokingVertex value is known.</summary>
    ProvokingVertex = 1 << 4,
    /// <summary>Every field in this category is known.</summary>
    All = CullEnabled | ScissorEnabled | LineWidth | PointSize | ProvokingVertex
}
