namespace VanillaGraphicsExpanded.Rendering;

/// <summary>
/// Stable mapping from tracked pipeline state knobs to bit indices.
/// </summary>
/// <remarks>
/// IMPORTANT: Values are part of the stable bit layout for PSO masks.
/// Append new entries at the end; do not reorder or renumber.
/// </remarks>
internal enum GlPipelineStateId : byte
{
    DepthTestEnable = 0,
    DepthFunc = 1,
    DepthWriteMask = 2,

    BlendEnable = 3,
    BlendFunc = 4,

    BlendEnableIndexed = 5,
    BlendFuncIndexed = 6,

    CullFaceEnable = 7,
    ColorMask = 8,
    ScissorTestEnable = 9,

    LineWidth = 10,
    PointSize = 11,

    Count = 12
}

