using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Adapts native graphics capability identifiers to their category transition owners.</summary>
internal sealed partial class StateCache
{
    #region Public API
    /// <summary>Routes an engine or complete-pipeline enable request to its owning category.</summary>
    internal void SetGraphicsEnable(EnableCap cap, bool enabled)
    {
        switch (cap)
        {
            case EnableCap.DepthClamp: SetDepthClampEnabled(enabled); break;
            case EnableCap.RasterizerDiscard: SetRasterizerDiscardEnabled(enabled); break;
            case EnableCap.PolygonOffsetFill: SetPolygonOffsetFillEnabled(enabled); break;
            case EnableCap.PolygonOffsetLine: SetPolygonOffsetLineEnabled(enabled); break;
            case EnableCap.PolygonOffsetPoint: SetPolygonOffsetPointEnabled(enabled); break;
            case EnableCap.ProgramPointSize: SetProgramPointSizeEnabled(enabled); break;
            case EnableCap.PrimitiveRestart: SetPrimitiveRestartEnabled(enabled); break;
            case EnableCap.PrimitiveRestartFixedIndex: SetPrimitiveRestartFixedIndexEnabled(enabled); break;
            case EnableCap.Multisample: SetMultisampleEnabled(enabled); break;
            case EnableCap.SampleCoverage: SetSampleCoverageEnabled(enabled); break;
            case EnableCap.SampleMask: SetSampleMaskEnabled(enabled); break;
            case EnableCap.SampleAlphaToCoverage: SetSampleAlphaToCoverageEnabled(enabled); break;
            case EnableCap.SampleAlphaToOne: SetSampleAlphaToOneEnabled(enabled); break;
            case EnableCap.SampleShading: SetSampleShadingEnabled(enabled); break;
            case EnableCap.StencilTest: SetStencilTestEnabled(enabled); break;
            case EnableCap.FramebufferSrgb: SetFramebufferSrgbEnabled(enabled); break;
            case EnableCap.Dither: SetDitherEnabled(enabled); break;
            case EnableCap.ColorLogicOp: SetColorLogicOpEnabled(enabled); break;
            default: throw new ArgumentOutOfRangeException(nameof(cap));
        }
    }
    #endregion
}
