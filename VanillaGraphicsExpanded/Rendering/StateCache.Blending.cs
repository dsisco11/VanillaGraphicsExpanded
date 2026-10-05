using System;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering.Pipeline.State;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Tracks effective state by draw-output index, including all global aliases.</summary>
internal sealed partial class StateCache
{
    #region Public API
    #region Capabilities and invalidation
    /// <summary>Returns the current context's supported output count, independently of target routing.</summary>
    internal int MaxDrawBuffers
    {
        get
        {
            SynchronizeContext();
            GpuSupport.EnsureCurrentContext();
            int count = GpuSupport.MaxDrawBuffers;
            if (count <= 0) throw new InvalidOperationException("No current draw-buffer capability.");
            if (blend.Length != count)
            {
                blend = new BlendState[count];
                blendKnown = new BlendStateKnowledge[count];
            }
            return count;
        }
    }
    /// <summary>Forgets factor knowledge without changing native output state.</summary>
    public void DirtyIndexedBlendFunc()
    {
        for (int i = 0; i < blendKnown.Length; i++) blendKnown[i] &= ~BlendStateKnowledge.Factors;
    }
    /// <summary>Forgets enable knowledge without changing native output state.</summary>
    public void DirtyIndexedBlendEnable()
    {
        for (int i = 0; i < blendKnown.Length; i++) blendKnown[i] &= ~BlendStateKnowledge.Enabled;
    }
    #endregion
    #region Enable transitions
    /// <summary>Establishes Enabled on every supported draw output, preserving alias knowledge.</summary>
    public void SetBlendEnabled(bool enabled)
    {
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Enabled);
        int count = MaxDrawBuffers;
        bool equal = true;
        for (int i = 0; i < count; i++) equal &= blendKnown[i].HasFlag(BlendStateKnowledge.Enabled) && blend[i].Enabled == enabled;
        if (equal) return;
        // Global operations affect the entire context output range, including unrouted slots.
        for (int i = 0; i < count; i++) blendKnown[i] &= ~BlendStateKnowledge.Enabled;
        if (enabled) GL.Enable(EnableCap.Blend); else GL.Disable(EnableCap.Blend);
        FixedFunctionCalls++;
        for (int i = 0; i < count; i++) { blend[i].Enabled = enabled; blendKnown[i] |= BlendStateKnowledge.Enabled; }
    }
    /// <summary>Establishes Enabled for one draw output; uniformity is always derived from all outputs.</summary>
    public void SetBlendEnabledIndexed(int index, bool enabled)
    {
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Enabled, index: index);
        ValidateDrawOutput(index);
        if (blendKnown[index].HasFlag(BlendStateKnowledge.Enabled) && blend[index].Enabled == enabled) return;
        blendKnown[index] &= ~BlendStateKnowledge.Enabled;
        if (enabled) GL.Enable(IndexedEnableCap.Blend, index); else GL.Disable(IndexedEnableCap.Blend, index);
        FixedFunctionCalls++;
        blend[index].Enabled = enabled;
        blendKnown[index] |= BlendStateKnowledge.Enabled;
    }
    #endregion
    #region Factor transitions
    /// <summary>Establishes Factors on every supported draw output, preserving alias knowledge.</summary>
    public void SetBlendFunc(GlBlendFunc func)
    {
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Factors);
        ValidateBlendFactors(func);
        int count = MaxDrawBuffers;
        bool equal = true;
        for (int i = 0; i < count; i++) equal &= blendKnown[i].HasFlag(BlendStateKnowledge.Factors) && blend[i].Factors == func;
        if (equal) return;
        // Global operations affect the entire context output range, including unrouted slots.
        for (int i = 0; i < count; i++) blendKnown[i] &= ~BlendStateKnowledge.Factors;
        GL.BlendFuncSeparate(func.SrcRgb, func.DstRgb, func.SrcAlpha, func.DstAlpha);
        FixedFunctionCalls++;
        for (int i = 0; i < count; i++) { blend[i].Factors = func; blendKnown[i] |= BlendStateKnowledge.Factors; }
    }
    /// <summary>Establishes Factors for one draw output; uniformity is always derived from all outputs.</summary>
    public void SetBlendFuncIndexed(int index, GlBlendFunc func)
    {
        ValidateBoundaryMutation(blend: BlendStateKnowledge.Factors, index: index);
        ValidateBlendFactors(func);
        ValidateDrawOutput(index);
        if (blendKnown[index].HasFlag(BlendStateKnowledge.Factors) && blend[index].Factors == func) return;
        blendKnown[index] &= ~BlendStateKnowledge.Factors;
        GL.BlendFuncSeparate(index, func.SrcRgb, func.DstRgb, func.SrcAlpha, func.DstAlpha);
        FixedFunctionCalls++;
        blend[index].Factors = func;
        blendKnown[index] |= BlendStateKnowledge.Factors;
    }
    #endregion
    #region Write-mask transitions
    /// <summary>Establishes WriteMask on every supported draw output, preserving alias knowledge.</summary>
    public void SetColorMask(GlColorMask mask)
    {
        ValidateBoundaryMutation(blend: BlendStateKnowledge.WriteMask);
        int count = MaxDrawBuffers;
        bool equal = true;
        for (int i = 0; i < count; i++) equal &= blendKnown[i].HasFlag(BlendStateKnowledge.WriteMask) && blend[i].WriteMask == mask;
        if (equal) return;
        // Global operations affect the entire context output range, including unrouted slots.
        for (int i = 0; i < count; i++) blendKnown[i] &= ~BlendStateKnowledge.WriteMask;
        GL.ColorMask(mask.R, mask.G, mask.B, mask.A);
        FixedFunctionCalls++;
        for (int i = 0; i < count; i++) { blend[i].WriteMask = mask; blendKnown[i] |= BlendStateKnowledge.WriteMask; }
    }
    /// <summary>Establishes WriteMask for one draw output; uniformity is always derived from all outputs.</summary>
    public void SetColorMaskIndexed(int index, GlColorMask mask)
    {
        ValidateBoundaryMutation(blend: BlendStateKnowledge.WriteMask, index: index);
        ValidateDrawOutput(index);
        if (blendKnown[index].HasFlag(BlendStateKnowledge.WriteMask) && blend[index].WriteMask == mask) return;
        blendKnown[index] &= ~BlendStateKnowledge.WriteMask;
        GL.ColorMask(index, mask.R, mask.G, mask.B, mask.A);
        FixedFunctionCalls++;
        blend[index].WriteMask = mask;
        blendKnown[index] |= BlendStateKnowledge.WriteMask;
    }
    #endregion
    #endregion
    #region Private
    /// <summary>Rejects unsupported enum values before they can become false native knowledge.</summary>
    private static void ValidateBlendFactors(GlBlendFunc func)
    {
        if (!Enum.IsDefined(func.SrcRgb) || !Enum.IsDefined(func.DstRgb)
            || !Enum.IsDefined(func.SrcAlpha) || !Enum.IsDefined(func.DstAlpha))
            throw new ArgumentOutOfRangeException(nameof(func));
    }

    /// <summary>Rejects unsupported indices before native mutation or cache publication.</summary>
    private void ValidateDrawOutput(int index)
    {
        if (index < 0 || index >= MaxDrawBuffers) throw new ArgumentOutOfRangeException(nameof(index));
    }
    #endregion
}
