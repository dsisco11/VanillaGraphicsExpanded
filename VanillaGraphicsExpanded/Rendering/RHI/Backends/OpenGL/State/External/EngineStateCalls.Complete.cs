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
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode);
        GL.CullFace(mode);
    }
    /// <summary>Preserves the exact native FrontFace overload and reconciles affected knowledge.</summary>
    public static void FrontFace(FrontFaceDirection direction)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.FrontFace);
        GL.FrontFace(direction);
    }
    /// <summary>Preserves the exact native DepthRange overload and reconciles affected knowledge.</summary>
    public static void DepthRange(double near, double far)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange);
        GL.DepthRange(near, far);
    }
    /// <summary>Preserves the exact native DepthRange overload and reconciles affected knowledge.</summary>
    public static void DepthRange(float near, float far)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(depth: DepthStateKnowledge.DepthRange);
        GL.DepthRange(near, far);
    }
    /// <summary>Preserves the exact native Scissor overload and reconciles affected knowledge.</summary>
    public static void Scissor(int x, int y, int width, int height)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.Scissor);
        GL.Scissor(x, y, width, height);
    }
    /// <summary>Preserves the exact native PolygonOffset overload and reconciles affected knowledge.</summary>
    public static void PolygonOffset(float factor, float units)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonOffset);
        GL.PolygonOffset(factor, units);
    }
    /// <summary>Preserves the exact native PolygonMode overload and reconciles affected knowledge.</summary>
    public static void PolygonMode(MaterialFace face, PolygonMode mode)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
        GL.PolygonMode(face, mode);
    }
    /// <summary>Preserves the exact native PolygonMode overload and reconciles affected knowledge.</summary>
    public static void PolygonMode(TriangleFace face, PolygonMode mode)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.PolygonModes);
        GL.PolygonMode(face, mode);
    }
    /// <summary>Preserves the exact native CullFace overload and reconciles affected knowledge.</summary>
    public static void CullFace(TriangleFace mode)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(rasterizer: RasterizerStateKnowledge.CullMode);
        GL.CullFace(mode);
    }
    #endregion

    #region Output blending
    /// <summary>Preserves the exact native BlendColor overload and reconciles affected knowledge.</summary>
    public static void BlendColor(float red, float green, float blue, float alpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(dynamic: DynamicDrawStateKnowledge.BlendConstant);
        GL.BlendColor(red, green, blue, alpha);
    }
    /// <summary>Preserves the exact native BlendEquation overload and reconciles affected knowledge.</summary>
    public static void BlendEquation(BlendEquationMode mode)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations);
        GL.BlendEquation(mode);
    }
    /// <summary>Preserves the exact native BlendEquationSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendEquationSeparate(BlendEquationMode rgb, BlendEquationMode alpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations);
        GL.BlendEquationSeparate(rgb, alpha);
    }
    /// <summary>Preserves the exact native BlendEquation overload and reconciles affected knowledge.</summary>
    public static void BlendEquation(int index, BlendEquationMode mode)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations, index: index);
        GL.BlendEquation(index, mode);
    }
    /// <summary>Preserves the exact native BlendEquationSeparate overload and reconciles affected knowledge.</summary>
    public static void BlendEquationSeparate(int index, BlendEquationMode rgb, BlendEquationMode alpha)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(blend: BlendStateKnowledge.Equations, index: index);
        GL.BlendEquationSeparate(index, rgb, alpha);
    }
    /// <summary>Preserves the exact native LogicOp overload and reconciles affected knowledge.</summary>
    public static void LogicOp(LogicOp operation)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(output: OutputStateKnowledge.LogicOperation);
        GL.LogicOp(operation);
    }
    #endregion

    #region Sampling and assembly
    /// <summary>Preserves the exact native SampleCoverage overload and reconciles affected knowledge.</summary>
    public static void SampleCoverage(float value, bool invert)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.SampleCoverage);
        GL.SampleCoverage(value, invert);
    }
    /// <summary>Preserves the exact native MinSampleShading overload and reconciles affected knowledge.</summary>
    public static void MinSampleShading(float value)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(sampling: SamplingStateKnowledge.MinimumSampleShading);
        GL.MinSampleShading(value);
    }
    /// <summary>Preserves the exact native SampleMask overload and reconciles affected knowledge.</summary>
    public static void SampleMask(int index, int mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(sampleMaskWords: true);
        GL.SampleMask(index, mask);
    }
    /// <summary>Preserves the exact native SampleMask overload and reconciles affected knowledge.</summary>
    public static void SampleMask(uint index, uint mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(sampleMaskWords: true);
        GL.SampleMask(index, mask);
    }
    /// <summary>Preserves the exact native PrimitiveRestartIndex overload and reconciles affected knowledge.</summary>
    public static void PrimitiveRestartIndex(uint index)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
        GL.PrimitiveRestartIndex(index);
    }
    /// <summary>Preserves the exact native PrimitiveRestartIndex overload and reconciles affected knowledge.</summary>
    public static void PrimitiveRestartIndex(int index)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(assembly: PrimitiveAssemblyStateKnowledge.RestartIndex);
        GL.PrimitiveRestartIndex(index);
    }
    #endregion

    #region Stencil comparisons
    /// <summary>Preserves the exact native StencilFunc overload and reconciles affected knowledge.</summary>
    public static void StencilFunc(StencilFunction function, int reference, uint mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
        GL.StencilFunc(function, reference, mask);
    }
    /// <summary>Preserves the exact native StencilFunc overload and reconciles affected knowledge.</summary>
    public static void StencilFunc(StencilFunction function, int reference, int mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilFunction | StencilStateKnowledge.BackStencilFunction));
        GL.StencilFunc(function, reference, mask);
    }
    /// <summary>Preserves the exact native StencilFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, uint mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
        GL.StencilFuncSeparate(face, function, reference, mask);
    }
    /// <summary>Preserves the exact native StencilFuncSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, int mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilFunction, StencilStateKnowledge.BackStencilFunction));
        GL.StencilFuncSeparate(face, function, reference, mask);
    }
    #endregion

    #region Stencil masks and operations
    /// <summary>Preserves the exact native StencilMask overload and reconciles affected knowledge.</summary>
    public static void StencilMask(uint mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
        GL.StencilMask(mask);
    }
    /// <summary>Preserves the exact native StencilMask overload and reconciles affected knowledge.</summary>
    public static void StencilMask(int mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilMask | StencilStateKnowledge.BackStencilMask));
        GL.StencilMask(mask);
    }
    /// <summary>Preserves the exact native StencilMaskSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilMaskSeparate(StencilFace face, uint mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask));
        GL.StencilMaskSeparate(face, mask);
    }
    /// <summary>Preserves the exact native StencilMaskSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilMaskSeparate(StencilFace face, int mask)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilMask, StencilStateKnowledge.BackStencilMask));
        GL.StencilMaskSeparate(face, mask);
    }
    /// <summary>Preserves the exact native StencilOp overload and reconciles affected knowledge.</summary>
    public static void StencilOp(StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: (StencilStateKnowledge.FrontStencilOperation | StencilStateKnowledge.BackStencilOperation));
        GL.StencilOp(fail, depthFail, pass);
    }
    /// <summary>Preserves the exact native StencilOpSeparate overload and reconciles affected knowledge.</summary>
    public static void StencilOpSeparate(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass)
    {
        var cache = CurrentCache;
        // Admit only the affected fields; preserve original native arguments, errors and execution.
        cache.ForgetExternalStateMutation(stencil: StencilFields(face, StencilStateKnowledge.FrontStencilOperation, StencilStateKnowledge.BackStencilOperation));
        GL.StencilOpSeparate(face, fail, depthFail, pass);
    }
    #endregion
    #endregion
}
