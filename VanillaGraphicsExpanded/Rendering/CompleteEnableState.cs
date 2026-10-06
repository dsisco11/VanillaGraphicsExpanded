using System;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Knowledge and values for independently addressable supplemental capabilities.</summary>
[Flags]
internal enum CompleteEnableFlags : uint
{
    /// <summary>Declared or observed None value.</summary>
    None = 0,
    /// <summary>Declared or observed StencilTest value.</summary>
    StencilTest = 1u << 0,
    /// <summary>Declared or observed DepthClamp value.</summary>
    DepthClamp = 1u << 1,
    /// <summary>Declared or observed RasterizerDiscard value.</summary>
    RasterizerDiscard = 1u << 2,
    /// <summary>Declared or observed PolygonOffsetFill value.</summary>
    PolygonOffsetFill = 1u << 3,
    /// <summary>Declared or observed PolygonOffsetLine value.</summary>
    PolygonOffsetLine = 1u << 4,
    /// <summary>Declared or observed PolygonOffsetPoint value.</summary>
    PolygonOffsetPoint = 1u << 5,
    /// <summary>Declared or observed ProgramPointSize value.</summary>
    ProgramPointSize = 1u << 6,
    /// <summary>Declared or observed Multisample value.</summary>
    Multisample = 1u << 7,
    /// <summary>Declared or observed SampleCoverage value.</summary>
    SampleCoverage = 1u << 8,
    /// <summary>Declared or observed SampleMask value.</summary>
    SampleMask = 1u << 9,
    /// <summary>Declared or observed SampleAlphaToCoverage value.</summary>
    SampleAlphaToCoverage = 1u << 10,
    /// <summary>Declared or observed SampleAlphaToOne value.</summary>
    SampleAlphaToOne = 1u << 11,
    /// <summary>Declared or observed SampleShading value.</summary>
    SampleShading = 1u << 12,
    /// <summary>Declared or observed FramebufferSrgb value.</summary>
    FramebufferSrgb = 1u << 13,
    /// <summary>Declared or observed Dither value.</summary>
    Dither = 1u << 14,
    /// <summary>Declared or observed ColorLogicOp value.</summary>
    ColorLogicOp = 1u << 15,
    /// <summary>Declared or observed PrimitiveRestart value.</summary>
    PrimitiveRestart = 1u << 16,
    /// <summary>Declared or observed PrimitiveRestartFixedIndex value.</summary>
    PrimitiveRestartFixedIndex = 1u << 17,
}
/// <summary>Separates native enable values from whether those values are known.</summary>
internal struct CompleteEnableState
{
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteEnableFlags Known;
    /// <summary>Declared or observed CompleteEnableFlags Values value.</summary>
    public CompleteEnableFlags Values;
}
