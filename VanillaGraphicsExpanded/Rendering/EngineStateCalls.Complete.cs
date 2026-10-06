using OpenTK.Graphics.OpenGL;

namespace VanillaGraphicsExpanded.Rendering;

/// <summary>Adapts additional native fixed-function commands through categorized cache ownership.</summary>
internal static partial class EngineStateCalls
{
    #region Public API
    #region Rasterization
    /// <summary>Publishes culling selection independently of enablement.</summary>
    public static void CullFace(CullFaceMode mode) => CurrentCache.SetCullMode(mode);
    /// <summary>Publishes front-facing winding.</summary>
    public static void FrontFace(FrontFaceDirection direction) => CurrentCache.SetFrontFace(direction);
    /// <summary>Publishes the window depth interval.</summary>
    public static void DepthRange(double near, double far) => CurrentCache.SetDepthRange(System.Math.Clamp(near, 0, 1), System.Math.Clamp(far, 0, 1));
    /// <summary>Publishes the single-precision window depth interval.</summary>
    public static void DepthRange(float near, float far) => CurrentCache.SetDepthRange(System.Math.Clamp(near, 0, 1), System.Math.Clamp(far, 0, 1));
    /// <summary>Publishes the scissor rectangle independently of enablement.</summary>
    public static void Scissor(int x, int y, int width, int height) => CurrentCache.SetScissor(x, y, width, height);
    /// <summary>Publishes polygon depth-bias parameters.</summary>
    public static void PolygonOffset(float factor, float units) => CurrentCache.SetPolygonOffset(factor, units);
    /// <summary>Publishes selected polygon faces while preserving the opposite face.</summary>
    public static void PolygonMode(MaterialFace face, PolygonMode mode) => CurrentCache.SetPolygonMode(face, mode);
    /// <summary>Adapts the triangle-face overload without changing its native face values.</summary>
    public static void PolygonMode(TriangleFace face, PolygonMode mode) => CurrentCache.SetPolygonMode((MaterialFace)face, mode);
    /// <summary>Preserves the engine triangle-face culling overload through the same owner.</summary>
    public static void CullFace(TriangleFace mode) => CurrentCache.SetCullMode((CullFaceMode)mode);
    #endregion

    #region Output blending
    /// <summary>Publishes the constant blend color.</summary>
    public static void BlendColor(float red, float green, float blue, float alpha) => CurrentCache.SetBlendConstant(System.Math.Clamp(red, 0, 1), System.Math.Clamp(green, 0, 1), System.Math.Clamp(blue, 0, 1), System.Math.Clamp(alpha, 0, 1));
    /// <summary>Updates both global blend equations and every indexed alias.</summary>
    public static void BlendEquation(BlendEquationMode mode) => CurrentCache.SetBlendEquation(mode, mode);
    /// <summary>Updates independent global RGB and alpha equations.</summary>
    public static void BlendEquationSeparate(BlendEquationMode rgb, BlendEquationMode alpha) => CurrentCache.SetBlendEquation(rgb, alpha);
    /// <summary>Updates both equations on one output.</summary>
    public static void BlendEquation(int index, BlendEquationMode mode) => CurrentCache.SetBlendEquationIndexed(index, mode, mode);
    /// <summary>Updates independent equations on one output.</summary>
    public static void BlendEquationSeparate(int index, BlendEquationMode rgb, BlendEquationMode alpha) => CurrentCache.SetBlendEquationIndexed(index, rgb, alpha);
    /// <summary>Publishes the logic operation independently of its enablement.</summary>
    public static void LogicOp(LogicOp operation) => CurrentCache.SetLogicOperation(operation);
    #endregion

    #region Sampling and assembly
    /// <summary>Publishes sample coverage parameters.</summary>
    public static void SampleCoverage(float value, bool invert) => CurrentCache.SetSampleCoverage(System.Math.Clamp(value, 0, 1), invert);
    /// <summary>Publishes the minimum sample-shading fraction.</summary>
    public static void MinSampleShading(float value) => CurrentCache.SetMinimumSampleShading(System.Math.Clamp(value, 0, 1));
    /// <summary>Publishes one sample-mask word preserving its unsigned bits.</summary>
    public static void SampleMask(int index, int mask) => CurrentCache.SetSampleMask(index, unchecked((uint)mask));
    /// <summary>Publishes one unsigned sample-mask word.</summary>
    public static void SampleMask(uint index, uint mask) => CurrentCache.SetSampleMask(checked((int)index), mask);
    /// <summary>Publishes explicit primitive-restart index.</summary>
    public static void PrimitiveRestartIndex(uint index) => CurrentCache.SetRestartIndex(index);
    /// <summary>Preserves all bits of the signed restart-index overload.</summary>
    public static void PrimitiveRestartIndex(int index) => CurrentCache.SetRestartIndex(unchecked((uint)index));
    #endregion

    #region Stencil comparisons
    /// <summary>Publishes both faces stencil comparison and reference.</summary>
    public static void StencilFunc(StencilFunction function, int reference, uint mask) => CurrentCache.SetStencilFunction(StencilFace.FrontAndBack, function, reference, mask);
    /// <summary>Preserves the signed stencil read-mask bits.</summary>
    public static void StencilFunc(StencilFunction function, int reference, int mask) => CurrentCache.SetStencilFunction(StencilFace.FrontAndBack, function, reference, unchecked((uint)mask));
    /// <summary>Publishes one faces stencil comparison and reference.</summary>
    public static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, uint mask) => CurrentCache.SetStencilFunction(face, function, reference, mask);
    /// <summary>Preserves one faces signed stencil read-mask bits.</summary>
    public static void StencilFuncSeparate(StencilFace face, StencilFunction function, int reference, int mask) => CurrentCache.SetStencilFunction(face, function, reference, unchecked((uint)mask));
    #endregion

    #region Stencil masks and operations
    /// <summary>Publishes both stencil write masks.</summary>
    public static void StencilMask(uint mask) => CurrentCache.SetStencilWriteMask(StencilFace.FrontAndBack, mask);
    /// <summary>Preserves signed stencil write-mask bits.</summary>
    public static void StencilMask(int mask) => CurrentCache.SetStencilWriteMask(StencilFace.FrontAndBack, unchecked((uint)mask));
    /// <summary>Publishes one faces stencil write mask.</summary>
    public static void StencilMaskSeparate(StencilFace face, uint mask) => CurrentCache.SetStencilWriteMask(face, mask);
    /// <summary>Preserves one faces signed stencil write-mask bits.</summary>
    public static void StencilMaskSeparate(StencilFace face, int mask) => CurrentCache.SetStencilWriteMask(face, unchecked((uint)mask));
    /// <summary>Publishes both faces stencil operations.</summary>
    public static void StencilOp(StencilOp fail, StencilOp depthFail, StencilOp pass) => CurrentCache.SetStencilOperation(StencilFace.FrontAndBack, fail, depthFail, pass);
    /// <summary>Publishes one faces stencil operations.</summary>
    public static void StencilOpSeparate(StencilFace face, StencilOp fail, StencilOp depthFail, StencilOp pass) => CurrentCache.SetStencilOperation(face, fail, depthFail, pass);
    #endregion
    #endregion
}
