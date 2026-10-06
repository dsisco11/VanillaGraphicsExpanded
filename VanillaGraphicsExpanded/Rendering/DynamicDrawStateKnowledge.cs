using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached DynamicDrawState values.</summary>
[Flags]
internal enum DynamicDrawStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached Viewport value is known.</summary>
    Viewport = 1 << 0,
    /// <summary>The cached Scissor value is known.</summary>
    Scissor = 1 << 1,
    /// <summary>The cached BlendConstant value is known.</summary>
    BlendConstant = 1 << 2,
    /// <summary>Every field in this category is known.</summary>
    All = Viewport | Scissor | BlendConstant
}
