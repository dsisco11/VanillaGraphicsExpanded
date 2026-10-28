using System;
using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Adapts additional native fixed-function commands through categorized cache ownership.</summary>
internal static partial class EngineStateCalls
{
    #region Public API
    #region Rasterization
    /// <summary>Preserves the exact native CullFace overload and reconciles affected knowledge.</summary>
    public static void CullFace(CullFaceMode mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.CullMode);
        bool accepted = Enum.IsDefined((CullFaceMode)mode);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode);
        try { GL.CullFace(mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalCullFace(mode);
    }
    /// <summary>Preserves the exact native FrontFace overload and reconciles affected knowledge.</summary>
    public static void FrontFace(FrontFaceDirection direction)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.FrontFace);
        bool accepted = Enum.IsDefined(direction);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.FrontFace);
        try { GL.FrontFace(direction); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.FrontFace); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalFrontFace(direction);
    }
    /// <summary>Preserves the exact native DepthRange overload and reconciles affected knowledge.</summary>
    public static void DepthRange(double near, double far)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(depth: DepthStateKnowledge.DepthRange);
        bool accepted = double.IsFinite(near) && double.IsFinite(far);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !double.IsFinite(near) || !double.IsFinite(far)) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange);
        try { GL.DepthRange(near, far); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalDepthRange(near, far);
    }
    /// <summary>Preserves the exact native DepthRange overload and reconciles affected knowledge.</summary>
    public static void DepthRange(float near, float far)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(depth: DepthStateKnowledge.DepthRange);
        bool accepted = double.IsFinite(near) && double.IsFinite(far);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(near) || !float.IsFinite(far)) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange);
        try { GL.DepthRange(near, far); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalDepthRange(near, far);
    }
    /// <summary>Preserves the exact native Scissor overload and reconciles affected knowledge.</summary>
    public static void Scissor(int x, int y, int width, int height)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(dynamic: DynamicDrawStateKnowledge.Scissor);
        bool accepted = width >= 0 && height >= 0;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Scissor);
        try { GL.Scissor(x, y, width, height); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Scissor); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalScissor(x, y, width, height);
    }
    /// <summary>Preserves the exact native PolygonOffset overload and reconciles affected knowledge.</summary>
    public static void PolygonOffset(float factor, float units)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonOffset);
        bool accepted = float.IsFinite(factor) && float.IsFinite(units);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(factor) || !float.IsFinite(units)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffset);
        try { GL.PolygonOffset(factor, units); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffset); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPolygonOffset(factor, units);
    }
    /// <summary>Preserves the exact native PolygonMode overload and reconciles affected knowledge.</summary>
    public static void PolygonMode(MaterialFace face, PolygonMode mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
        bool accepted = (int)face == (int)MaterialFace.FrontAndBack && Enum.IsDefined(mode);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        // Separate face modes are outside the supported core model. A compatibility
        // context may still accept them, so they cannot leave a common mode certified.
        if (accepted || !GpuSupport.Graphics.CoreProfile && ((int)face == (int)MaterialFace.Front || (int)face == (int)MaterialFace.Back))
            cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
        try { GL.PolygonMode(face, mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPolygonMode(face, mode);
    }
    /// <summary>Preserves the exact native PolygonMode overload and reconciles affected knowledge.</summary>
    public static void PolygonMode(TriangleFace face, PolygonMode mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
        bool accepted = (int)face == (int)MaterialFace.FrontAndBack && Enum.IsDefined(mode);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        // Separate face modes are outside the supported core model. A compatibility
        // context may still accept them, so they cannot leave a common mode certified.
        if (accepted || !GpuSupport.Graphics.CoreProfile && ((int)face == (int)MaterialFace.Front || (int)face == (int)MaterialFace.Back))
            cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
        try { GL.PolygonMode(face, mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPolygonMode(face, mode);
    }
    /// <summary>Preserves the exact native CullFace overload and reconciles affected knowledge.</summary>
    public static void CullFace(TriangleFace mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(rasterizer: RasterizerStateKnowledge.CullMode);
        bool accepted = Enum.IsDefined((CullFaceMode)mode);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode);
        try { GL.CullFace(mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalCullFace(mode);
    }
    #endregion

    #region Output blending
    /// <summary>Preserves the exact native BlendColor overload and reconciles affected knowledge.</summary>
    public static void BlendColor(float red, float green, float blue, float alpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(dynamic: DynamicDrawStateKnowledge.BlendConstant);
        bool accepted = float.IsFinite(red) && float.IsFinite(green) && float.IsFinite(blue) && float.IsFinite(alpha);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(red) || !float.IsFinite(green) || !float.IsFinite(blue) || !float.IsFinite(alpha)) cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.BlendConstant);
        try { GL.BlendColor(red, green, blue, alpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.BlendConstant); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendColor(red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native BlendEquation overload and reconciles affected knowledge.</summary>
    public static void BlendEquation(BlendEquationMode mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations);
        bool accepted = Enum.IsDefined(mode) && Enum.IsDefined(mode);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations);
        try { GL.BlendEquation(mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendEquation(mode);
    }
    /// <summary>Preserves the exact native BlendEquationSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendEquationSeparate(BlendEquationMode rgb, BlendEquationMode alpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations);
        bool accepted = Enum.IsDefined(rgb) && Enum.IsDefined(alpha);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations);
        try { GL.BlendEquationSeparate(rgb, alpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendEquationSeparate(rgb, alpha);
    }
    /// <summary>Preserves the exact native BlendEquation overload and reconciles affected knowledge.</summary>
    public static void BlendEquation(int index, BlendEquationMode mode)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations, index: index);
        bool accepted = index >= 0 && index < cache.MaxDrawBuffers && (Enum.IsDefined(mode) && Enum.IsDefined(mode));
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations, index: index);
        try { GL.BlendEquation(index, mode); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations, index: index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendEquation(index, mode);
    }
    /// <summary>Preserves the exact native BlendEquationSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendEquationSeparate(int index, BlendEquationMode rgb, BlendEquationMode alpha)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(blend: BlendStateKnowledge.Equations, index: index);
        bool accepted = index >= 0 && index < cache.MaxDrawBuffers && (Enum.IsDefined(rgb) && Enum.IsDefined(alpha));
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations, index: index);
        try { GL.BlendEquationSeparate(index, rgb, alpha); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations, index: index); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalBlendEquationSeparate(index, rgb, alpha);
    }
    /// <summary>Preserves the exact native LogicOp overload and reconciles affected knowledge.</summary>
    public static void LogicOp(LogicOp operation)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(output: OutputStateKnowledge.LogicOperation);
        bool accepted = Enum.IsDefined(operation);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(output: OutputStateKnowledge.LogicOperation);
        try { GL.LogicOp(operation); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(output: OutputStateKnowledge.LogicOperation); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalLogicOp(operation);
    }
    #endregion

    #region Sampling and assembly
    /// <summary>Preserves the exact native SampleCoverage overload and reconciles affected knowledge.</summary>
    public static void SampleCoverage(float value, bool invert)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(sampling: SamplingStateKnowledge.SampleCoverage);
        bool accepted = float.IsFinite(value);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(value)) cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleCoverage);
        try { GL.SampleCoverage(value, invert); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleCoverage); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalSampleCoverage(value, invert);
    }
    /// <summary>Preserves the exact native MinSampleShading overload and reconciles affected knowledge.</summary>
    public static void MinSampleShading(float value)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(sampling: SamplingStateKnowledge.MinimumSampleShading);
        bool accepted = float.IsFinite(value);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted || !float.IsFinite(value)) cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.MinimumSampleShading);
        try { GL.MinSampleShading(value); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.MinimumSampleShading); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalMinSampleShading(value);
    }
    /// <summary>Preserves the exact native SampleMask overload and reconciles affected knowledge.</summary>
    public static void SampleMask(int index, int mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(sampleMaskWords: true);
        bool accepted = (ulong)index < (ulong)GpuSupport.Graphics.MaxSampleMaskWords;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.WithdrawExternalSampleMask(unchecked((int)index));
        try { GL.SampleMask(index, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.WithdrawExternalSampleMask(unchecked((int)index)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalSampleMask(index, mask);
    }
    /// <summary>Preserves the exact native SampleMask overload and reconciles affected knowledge.</summary>
    public static void SampleMask(uint index, uint mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(sampleMaskWords: true);
        bool accepted = (ulong)index < (ulong)GpuSupport.Graphics.MaxSampleMaskWords;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.WithdrawExternalSampleMask(unchecked((int)index));
        try { GL.SampleMask(index, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.WithdrawExternalSampleMask(unchecked((int)index)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalSampleMask(index, mask);
    }
    /// <summary>Preserves the exact native PrimitiveRestartIndex overload and reconciles affected knowledge.</summary>
    public static void PrimitiveRestartIndex(uint index)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
        bool accepted = true;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
        try { GL.PrimitiveRestartIndex(index); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPrimitiveRestartIndex(index);
    }
    /// <summary>Preserves the exact native PrimitiveRestartIndex overload and reconciles affected knowledge.</summary>
    public static void PrimitiveRestartIndex(int index)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
        bool accepted = true;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
        try { GL.PrimitiveRestartIndex(index); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalPrimitiveRestartIndex(index);
    }
    #endregion

    #region Stencil comparisons
    /// <summary>Preserves the exact native StencilFunc overload and reconciles affected knowledge.</summary>
    public static void StencilFunc(StencilFunction function, int reference, uint mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
        bool accepted = Enum.IsDefined(function);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
        try { GL.StencilFunc(function, reference, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilFunc(function, reference, mask);
    }
    /// <summary>Preserves the exact native StencilFunc overload and reconciles affected knowledge.</summary>
    public static void StencilFunc(StencilFunction function, int reference, int mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
        bool accepted = Enum.IsDefined(function);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
        try { GL.StencilFunc(function, reference, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilFunc(function, reference, mask);
    }
    /// <summary>Preserves the exact native StencilFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, uint mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
        bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack && Enum.IsDefined(function);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
        try { GL.StencilFuncSeparate(face, function, reference, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilFuncSeparate(face, function, reference, mask);
    }
    /// <summary>Preserves the exact native StencilFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, int mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
        bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack && Enum.IsDefined(function);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
        try { GL.StencilFuncSeparate(face, function, reference, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilFuncSeparate(face, function, reference, mask);
    }
    #endregion

    #region Stencil masks and operations
    /// <summary>Preserves the exact native StencilMask overload and reconciles affected knowledge.</summary>
    public static void StencilMask(uint mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
        bool accepted = true;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
        try { GL.StencilMask(mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilMask(mask);
    }
    /// <summary>Preserves the exact native StencilMask overload and reconciles affected knowledge.</summary>
    public static void StencilMask(int mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
        bool accepted = true;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
        try { GL.StencilMask(mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilMask(mask);
    }
    /// <summary>Preserves the exact native StencilMaskSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilMaskSeparate(StencilFace face, uint mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask));
        bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask));
        try { GL.StencilMaskSeparate(face, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilMaskSeparate(face, mask);
    }
    /// <summary>Preserves the exact native StencilMaskSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilMaskSeparate(StencilFace face, int mask)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask));
        bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack;
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask));
        try { GL.StencilMaskSeparate(face, mask); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilMaskSeparate(face, mask);
    }
    /// <summary>Preserves the exact native StencilOp overload and reconciles affected knowledge.</summary>
    public static void StencilOp(StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: (StencilStateKnowledge.FrontStencilOperation | StencilStateKnowledge.BackStencilOperation));
        bool accepted = Enum.IsDefined(fail) && Enum.IsDefined(depthFail) && Enum.IsDefined(pass);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilOperation | StencilStateKnowledge.BackStencilOperation));
        try { GL.StencilOp(fail, depthFail, pass); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilOperation | StencilStateKnowledge.BackStencilOperation)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilOp(fail, depthFail, pass);
    }
    /// <summary>Preserves the exact native StencilOpSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilOpSeparate(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        var cache = CurrentCache;
        cache.ValidateBoundaryMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilOperation, StencilStateKnowledge.BackStencilOperation));
        bool accepted = face is StencilFace.Front or StencilFace.Back or StencilFace.FrontAndBack && Enum.IsDefined(fail) && Enum.IsDefined(depthFail) && Enum.IsDefined(pass);
        var operationContext = Integration.RenderContextRegistry.CaptureResourceContext();
        // Reject boundary misuse before dispatch, but preserve known state after defined native rejection.
        if (accepted) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilOperation, StencilStateKnowledge.BackStencilOperation));
        try { GL.StencilOpSeparate(face, fail, depthFail, pass); }
        catch { if (cache.OwnsKnowledgeFor(operationContext)) cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilOperation, StencilStateKnowledge.BackStencilOperation)); throw; }
        if (accepted && cache.OwnsKnowledgeFor(operationContext) && Integration.RenderContextRegistry.IsCurrentResourceContext(operationContext))
            cache.ObserveExternalStencilOpSeparate(face, fail, depthFail, pass);
    }
    #endregion
    #endregion
}
