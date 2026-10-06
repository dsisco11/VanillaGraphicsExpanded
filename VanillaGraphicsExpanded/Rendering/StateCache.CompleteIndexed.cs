using System;
using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns indexed equations and sample-mask words with global alias coherence.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Indexed state
    /// <summary>Establishes equations across every native output slot.</summary>
    internal void SetBlendEquation(BlendEquationMode rgb, BlendEquationMode alpha)
    {
        if (!Enum.IsDefined(rgb) || !Enum.IsDefined(alpha)) throw new ArgumentOutOfRangeException(nameof(rgb));
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations);
        int count = MaxDrawBuffers;
        bool equal = true;
        for (int i = 0; i < count; i++) equal &= TryGetCachedBlendEquation(i, out var known) && known == (rgb, alpha);
        if (equal) return;
        ForgetBlendEquations();
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.BlendEquationSeparate(rgb, alpha); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        for (int i = 0; i < count; i++) { blend[i].Equations = (rgb, alpha); blendKnown[i] |= BlendStateKnowledge.Equations; }
    }
    /// <summary>Establishes equations for a single draw-output slot.</summary>
    internal void SetBlendEquationIndexed(int index, BlendEquationMode rgb, BlendEquationMode alpha)
    {
        ValidateDrawOutput(index);
        if (!Enum.IsDefined(rgb) || !Enum.IsDefined(alpha)) throw new ArgumentOutOfRangeException(nameof(rgb));
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations, index: index);
        if (!GpuSupport.Graphics.IndependentBlend) throw new NotSupportedException("Independent blend equations are unavailable.");
        if (TryGetCachedBlendEquation(index, out var known) && known == (rgb, alpha)) return;
        ForgetBlendEquation(index);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.BlendEquationSeparate(index, rgb, alpha); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        blend[index].Equations = (rgb, alpha); blendKnown[index] |= BlendStateKnowledge.Equations;
    }
    /// <summary>Establishes one independently tracked native sample-mask word.</summary>
    internal void SetSampleMask(int index, uint value)
    {
        if (index < 0 || index >= GpuSupport.Graphics.MaxSampleMaskWords) throw new ArgumentOutOfRangeException(nameof(index));
        ValidateCompleteMutation();
        if (completeSampleMasks.TryGetValue(index, out uint known) && known == value) return;
        completeSampleMasks.Remove(index);
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.SampleMask((uint)index, value); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        completeSampleMasks[index] = value;
    }
    #endregion
    #region Output and polygon interpretation
    /// <summary>Establishes the fixed logic function independently of its enable state.</summary>
    internal void SetLogicOperation(LogicOp operation)
    {
        if (!Enum.IsDefined(operation)) throw new ArgumentOutOfRangeException(nameof(operation));
        ValidateCompleteMutation();
        if (completeOutput.Known.HasFlag(CompleteOutputKnowledge.LogicOperation) && completeOutput.LogicOperation == operation) return;
        completeOutput.Known &= ~CompleteOutputKnowledge.LogicOperation;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.LogicOp(operation); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        completeOutput.LogicOperation = operation;
        completeOutput.Known |= CompleteOutputKnowledge.LogicOperation;
    }
    /// <summary>Sets both polygon faces without inheriting an undeclared native face mode.</summary>
    internal void SetPolygonModes(PolygonMode front, PolygonMode back)
    {
        if (!Enum.IsDefined(front) || !Enum.IsDefined(back)) throw new ArgumentOutOfRangeException(nameof(front));
        if (GpuSupport.Graphics.CoreProfile && front != back) throw new NotSupportedException("Core polygon modes must match.");
        ValidateCompleteMutation();
        if (rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonModes) && rasterizer.PolygonModes == (front, back)) return;
        rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.PolygonModes;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        if (front == back) { GL.PolygonMode(MaterialFace.FrontAndBack, front); FixedFunctionCalls++; }
        else { GL.PolygonMode(MaterialFace.Front, front); GL.PolygonMode(MaterialFace.Back, back); FixedFunctionCalls += 2; }
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        rasterizer.PolygonModes = (front, back);
        rasterizer.SupplementalKnown |= CompleteRasterKnowledge.PolygonModes;
    }
    /// <summary>Changes a selected compatibility polygon face without guessing the other face's value.</summary>
    internal void SetPolygonMode(MaterialFace face, PolygonMode mode)
    {
        if (face is not (MaterialFace.Front or MaterialFace.Back or MaterialFace.FrontAndBack) || !Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(face));
        if (face == MaterialFace.FrontAndBack) { SetPolygonModes(mode, mode); return; }
        if (GpuSupport.Graphics.CoreProfile) throw new NotSupportedException("Single-face polygon modes require compatibility profile.");
        ValidateCompleteMutation();
        bool known = rasterizer.SupplementalKnown.HasFlag(CompleteRasterKnowledge.PolygonModes);
        var previous = rasterizer.PolygonModes;
        if (known && (face == MaterialFace.Front ? previous.Front : previous.Back) == mode) return;
        rasterizer.SupplementalKnown &= ~CompleteRasterKnowledge.PolygonModes;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        GL.PolygonMode(face, mode); FixedFunctionCalls++;
        if (GlDebug.CheckStateTransitions && !restoringBoundary) CheckBoundaryNativeError();
        // A single-face command cannot establish knowledge for an unobserved opposite face.
        if (known)
        {
            rasterizer.PolygonModes = face == MaterialFace.Front ? (mode, previous.Back) : (previous.Front, mode);
            rasterizer.SupplementalKnown |= CompleteRasterKnowledge.PolygonModes;
        }
    }
    #endregion
    #endregion
}
