using System;

namespace VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;

/// <summary>Converts represented external texture parameters according to native integer-state rules.</summary>
internal static unsafe partial class OpenGLTexturesCalls
{
    #region Private
    /// <summary>Returns defined nearest-integer results; unspecified tie/overflow outcomes retain targeted acquisition.</summary>
    internal static int? ConvertTextureParameter(float value)
    {
        double rounded = Math.Round((double)value);
        if (!float.IsFinite(value) || rounded < int.MinValue || rounded > int.MaxValue
            || Math.Abs((double)value - Math.Truncate(value)) == 0.5) return null;
        return (int)rounded;
    }
    #endregion
}
