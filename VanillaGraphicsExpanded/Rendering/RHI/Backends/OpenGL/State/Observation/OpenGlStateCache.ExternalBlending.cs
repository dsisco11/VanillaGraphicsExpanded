using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Propagates established external blending effects into the existing executed-state authority.</summary>
internal sealed partial class OpenGlStateCache
{
    #region Internal API
    #region Factors
    /// <summary>Commits the defined BlendFunc effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendFunc(BlendingFactor source, BlendingFactor destination)
    {
        var factors = new GlBlendFunc((BlendingFactorSrc)source, (BlendingFactorDest)destination, (BlendingFactorSrc)source, (BlendingFactorDest)destination);
        for (int i = 0; i < MaxDrawBuffers; i++) { blend[i].Factors = factors; blendKnown[i] |= BlendStateKnowledge.Factors; }
    }

    /// <summary>Commits the defined BlendFunc effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendFunc(int index, BlendingFactorSrc source, BlendingFactorDest destination)
    {
        var factors = new GlBlendFunc(source, destination, (BlendingFactorSrc)source, (BlendingFactorDest)destination);
        blend[index].Factors = factors; blendKnown[index] |= BlendStateKnowledge.Factors;
    }

    /// <summary>Commits the defined BlendFuncSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var factors = new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
        for (int i = 0; i < MaxDrawBuffers; i++) { blend[i].Factors = factors; blendKnown[i] |= BlendStateKnowledge.Factors; }
    }

    /// <summary>Commits the defined BlendFuncSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb,
        BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha)
    {
        var factors = new GlBlendFunc(sourceRgb, destinationRgb, sourceAlpha, destinationAlpha);
        blend[index].Factors = factors; blendKnown[index] |= BlendStateKnowledge.Factors;
    }

    #endregion

    #region Color and masks
    /// <summary>Commits the defined ColorMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalColorMask(bool red, bool green, bool blue, bool alpha)
    {
        var mask = GlColorMask.FromRgba(red, green, blue, alpha);
        for (int i = 0; i < MaxDrawBuffers; i++) { blend[i].WriteMask = mask; blendKnown[i] |= BlendStateKnowledge.WriteMask; }
    }

    /// <summary>Commits the defined ColorMask effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalColorMask(int index, bool red, bool green, bool blue, bool alpha)
    {
        var mask = GlColorMask.FromRgba(red, green, blue, alpha);
        blend[index].WriteMask = mask; blendKnown[index] |= BlendStateKnowledge.WriteMask;
    }

    /// <summary>Commits the defined ClearColor effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalClearColor(float red, float green, float blue, float alpha)
    {
        clearColor = new(red, green, blue, alpha); clearColorKnown = true;
    }

    /// <summary>Commits the defined BlendColor effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendColor(float red, float green, float blue, float alpha)
    {
        dynamicState.BlendConstant = (red, green, blue, alpha); dynamicKnown |= DynamicDrawStateKnowledge.BlendConstant;
    }

    #endregion

    #region Equations and logic
    /// <summary>Commits the defined BlendEquation effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendEquation(BlendEquationMode mode)
    {
        for (int i = 0; i < MaxDrawBuffers; i++) { blend[i].Equations = (mode, mode); blendKnown[i] |= BlendStateKnowledge.Equations; }
    }

    /// <summary>Commits the defined BlendEquationSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendEquationSeparate(BlendEquationMode rgb, BlendEquationMode alpha)
    {
        for (int i = 0; i < MaxDrawBuffers; i++) { blend[i].Equations = (rgb, alpha); blendKnown[i] |= BlendStateKnowledge.Equations; }
    }

    /// <summary>Commits the defined BlendEquation effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendEquation(int index, BlendEquationMode mode)
    {
        blend[index].Equations = (mode, mode); blendKnown[index] |= BlendStateKnowledge.Equations;
    }

    /// <summary>Commits the defined BlendEquationSeparate effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalBlendEquationSeparate(int index, BlendEquationMode rgb, BlendEquationMode alpha)
    {
        blend[index].Equations = (rgb, alpha); blendKnown[index] |= BlendStateKnowledge.Equations;
    }

    /// <summary>Commits the defined LogicOp effect after exact native dispatch and original-context return.</summary>
    internal void ObserveExternalLogicOp(LogicOp operation)
    {
        output.LogicOperation = operation; outputKnown |= OutputStateKnowledge.LogicOperation;
    }
    /// <summary>Recognizes the core blend-function enum domain without consuming native errors.</summary>
    internal static bool AcceptsExternalBlendFactors(GlBlendFunc value) =>
        Enum.IsDefined((BlendingFactor)value.SrcRgb) && Enum.IsDefined((BlendingFactor)value.DstRgb)
        && Enum.IsDefined((BlendingFactor)value.SrcAlpha) && Enum.IsDefined((BlendingFactor)value.DstAlpha);
    #endregion
    #endregion
}
