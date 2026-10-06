using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached OutputState values.</summary>
[Flags]
internal enum OutputStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached LogicOperation value is known.</summary>
    LogicOperation = 1 << 0,
    /// <summary>The cached FramebufferSrgb value is known.</summary>
    FramebufferSrgb = 1 << 1,
    /// <summary>The cached Dither value is known.</summary>
    Dither = 1 << 2,
    /// <summary>The cached ColorLogicOp value is known.</summary>
    ColorLogicOp = 1 << 3,
    /// <summary>Every scalar field in this category is known.</summary>
    All = LogicOperation | FramebufferSrgb | Dither | ColorLogicOp
}
