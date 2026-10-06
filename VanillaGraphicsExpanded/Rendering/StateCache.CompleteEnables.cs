using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Routes supplemental enable transitions to their owning category.</summary>
internal sealed partial class StateCache
{
    internal static readonly EnableCap[] SupplementalCapabilities =
    {
        EnableCap.StencilTest,
        EnableCap.DepthClamp,
        EnableCap.RasterizerDiscard,
        EnableCap.PolygonOffsetFill,
        EnableCap.PolygonOffsetLine,
        EnableCap.PolygonOffsetPoint,
        EnableCap.ProgramPointSize,
        EnableCap.Multisample,
        EnableCap.SampleCoverage,
        EnableCap.SampleMask,
        EnableCap.SampleAlphaToCoverage,
        EnableCap.SampleAlphaToOne,
        EnableCap.SampleShading,
        EnableCap.FramebufferSrgb,
        EnableCap.Dither,
        EnableCap.ColorLogicOp,
        EnableCap.PrimitiveRestart,
        EnableCap.PrimitiveRestartFixedIndex,
    };
    #region Public API
    /// <summary>Establishes a supplemental capability without querying unchanged native state.</summary>
    internal void SetCompleteEnable(EnableCap cap, bool enabled)
    {
        ValidateCompleteMutation();
        var flag = SupplementalEnableFlag(cap);
        if ((cap == EnableCap.SampleShading && !GpuSupport.Graphics.SampleShading)
            || (cap == EnableCap.PrimitiveRestartFixedIndex && !GpuSupport.Graphics.FixedIndexRestart))
        {
            if (enabled) throw new NotSupportedException($"Unsupported capability: {cap}.");
            return;
        }
        ref var state = ref SupplementalEnableStorage(cap);
        if (state.Known.HasFlag(flag) && state.Values.HasFlag(flag) == enabled) return;
        state.Known &= ~flag;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) GL.Enable(cap); else GL.Disable(cap);
        FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (enabled) state.Values |= flag; else state.Values &= ~flag;
        state.Known |= flag;
    }
    #endregion
    #region Private
    /// <summary>Selects the category owning a supported native capability.</summary>
    private ref CompleteEnableState SupplementalEnableStorage(EnableCap cap)
    {
        switch (cap)
        {
            case EnableCap.StencilTest: return ref completeStencil.Enables;
            case EnableCap.DepthClamp: return ref rasterizer.SupplementalEnables;
            case EnableCap.RasterizerDiscard: return ref rasterizer.SupplementalEnables;
            case EnableCap.PolygonOffsetFill: return ref rasterizer.SupplementalEnables;
            case EnableCap.PolygonOffsetLine: return ref rasterizer.SupplementalEnables;
            case EnableCap.PolygonOffsetPoint: return ref rasterizer.SupplementalEnables;
            case EnableCap.ProgramPointSize: return ref rasterizer.SupplementalEnables;
            case EnableCap.Multisample: return ref completeSampling.Enables;
            case EnableCap.SampleCoverage: return ref completeSampling.Enables;
            case EnableCap.SampleMask: return ref completeSampling.Enables;
            case EnableCap.SampleAlphaToCoverage: return ref completeSampling.Enables;
            case EnableCap.SampleAlphaToOne: return ref completeSampling.Enables;
            case EnableCap.SampleShading: return ref completeSampling.Enables;
            case EnableCap.FramebufferSrgb: return ref completeOutput.Enables;
            case EnableCap.Dither: return ref completeOutput.Enables;
            case EnableCap.ColorLogicOp: return ref completeOutput.Enables;
            case EnableCap.PrimitiveRestart: return ref assembly.SupplementalEnables;
            case EnableCap.PrimitiveRestartFixedIndex: return ref assembly.SupplementalEnables;
            default: throw new ArgumentOutOfRangeException(nameof(cap));
        }
    }
    /// <summary>Maps native capability identifiers to independent knowledge bits.</summary>
    private static CompleteEnableFlags SupplementalEnableFlag(EnableCap cap) => cap switch
    {
        EnableCap.StencilTest => CompleteEnableFlags.StencilTest,
        EnableCap.DepthClamp => CompleteEnableFlags.DepthClamp,
        EnableCap.RasterizerDiscard => CompleteEnableFlags.RasterizerDiscard,
        EnableCap.PolygonOffsetFill => CompleteEnableFlags.PolygonOffsetFill,
        EnableCap.PolygonOffsetLine => CompleteEnableFlags.PolygonOffsetLine,
        EnableCap.PolygonOffsetPoint => CompleteEnableFlags.PolygonOffsetPoint,
        EnableCap.ProgramPointSize => CompleteEnableFlags.ProgramPointSize,
        EnableCap.Multisample => CompleteEnableFlags.Multisample,
        EnableCap.SampleCoverage => CompleteEnableFlags.SampleCoverage,
        EnableCap.SampleMask => CompleteEnableFlags.SampleMask,
        EnableCap.SampleAlphaToCoverage => CompleteEnableFlags.SampleAlphaToCoverage,
        EnableCap.SampleAlphaToOne => CompleteEnableFlags.SampleAlphaToOne,
        EnableCap.SampleShading => CompleteEnableFlags.SampleShading,
        EnableCap.FramebufferSrgb => CompleteEnableFlags.FramebufferSrgb,
        EnableCap.Dither => CompleteEnableFlags.Dither,
        EnableCap.ColorLogicOp => CompleteEnableFlags.ColorLogicOp,
        EnableCap.PrimitiveRestart => CompleteEnableFlags.PrimitiveRestart,
        EnableCap.PrimitiveRestartFixedIndex => CompleteEnableFlags.PrimitiveRestartFixedIndex,
        _ => throw new ArgumentOutOfRangeException(nameof(cap))
    };
    #endregion
}
