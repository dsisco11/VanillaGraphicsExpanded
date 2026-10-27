using OpenTK.Graphics.OpenGL;
using Core = OpenTK.Graphics.OpenGL4;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Executes admitted blending effects without owning cached state or suppression.</summary>
internal static partial class OpenGLStateEffects
{
    #region Internal API
    /// <summary>Issues the core BlendFuncSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void BlendFuncSeparate(BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb, BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) => Core.GL.BlendFuncSeparate((Core.BlendingFactorSrc)sourceRgb, (Core.BlendingFactorDest)destinationRgb, (Core.BlendingFactorSrc)sourceAlpha, (Core.BlendingFactorDest)destinationAlpha);

    /// <summary>Issues the core BlendFuncSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void BlendFuncSeparate(int index, BlendingFactorSrc sourceRgb, BlendingFactorDest destinationRgb, BlendingFactorSrc sourceAlpha, BlendingFactorDest destinationAlpha) => Core.GL.BlendFuncSeparate(index, (Core.BlendingFactorSrc)sourceRgb, (Core.BlendingFactorDest)destinationRgb, (Core.BlendingFactorSrc)sourceAlpha, (Core.BlendingFactorDest)destinationAlpha);

    /// <summary>Issues the core BlendEquationSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void BlendEquationSeparate(BlendEquationMode rgb, BlendEquationMode alpha) => Core.GL.BlendEquationSeparate((Core.BlendEquationMode)rgb, (Core.BlendEquationMode)alpha);

    /// <summary>Issues the core BlendEquationSeparate effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void BlendEquationSeparate(int index, BlendEquationMode rgb, BlendEquationMode alpha) => Core.GL.BlendEquationSeparate(index, (Core.BlendEquationMode)rgb, (Core.BlendEquationMode)alpha);

    /// <summary>Issues the core ColorMask effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ColorMask(bool red, bool green, bool blue, bool alpha) => Core.GL.ColorMask(red, green, blue, alpha);

    /// <summary>Issues the core ColorMask effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ColorMask(int index, bool red, bool green, bool blue, bool alpha) => Core.GL.ColorMask(index, red, green, blue, alpha);

    /// <summary>Issues the core BlendColor effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void BlendColor(float red, float green, float blue, float alpha) => Core.GL.BlendColor(red, green, blue, alpha);

    /// <summary>Issues the core ClearColor effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void ClearColor(float red, float green, float blue, float alpha) => Core.GL.ClearColor(red, green, blue, alpha);

    /// <summary>Issues the core LogicOp effect after cache admission; knowledge commits belong to the caller.</summary>
    internal static void LogicOp(LogicOp operation) => Core.GL.LogicOp((Core.LogicOp)operation);

    #endregion
}
