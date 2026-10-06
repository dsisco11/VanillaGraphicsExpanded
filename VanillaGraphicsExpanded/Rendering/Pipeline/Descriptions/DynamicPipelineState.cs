using System;

namespace VanillaGraphicsExpanded.Rendering.Pipeline.Descriptions;

/// <summary>Declares draw-supplied values; none may be silently inherited by a complete pipeline.</summary>
[Flags]
internal enum DynamicPipelineState
{
    None = 0,
    Viewport = 1,
    Scissor = 2,
    StencilReference = 4,
    BlendConstant = 8,
    All = Viewport | Scissor | StencilReference | BlendConstant
}
