using System;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Independent validity flags for cached StencilState values.</summary>
[Flags]
internal enum StencilStateKnowledge : byte
{
    /// <summary>No fields are known.</summary>
    None = 0,
    /// <summary>The cached FrontStencilFunction value is known.</summary>
    FrontStencilFunction = 1 << 0,
    /// <summary>The cached BackStencilFunction value is known.</summary>
    BackStencilFunction = 1 << 1,
    /// <summary>The cached FrontStencilMask value is known.</summary>
    FrontStencilMask = 1 << 2,
    /// <summary>The cached BackStencilMask value is known.</summary>
    BackStencilMask = 1 << 3,
    /// <summary>The cached FrontStencilOperation value is known.</summary>
    FrontStencilOperation = 1 << 4,
    /// <summary>The cached BackStencilOperation value is known.</summary>
    BackStencilOperation = 1 << 5,
    /// <summary>The cached TestEnabled value is known.</summary>
    TestEnabled = 1 << 6,
    /// <summary>Every scalar field in this category is known.</summary>
    All = FrontStencilFunction | BackStencilFunction | FrontStencilMask | BackStencilMask | FrontStencilOperation | BackStencilOperation | TestEnabled
}
