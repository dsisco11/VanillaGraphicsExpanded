using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Owns resource clear-operation values outside graphics pipeline identity.</summary>
internal sealed partial class StateCache
{
    private Vector4 clearColor;
    private bool clearColorKnown;
    #region Public API
    /// <summary>Establishes clear color, retaining the native floating-point value without clamping.</summary>
    internal void SetClearColor(float red, float green, float blue, float alpha)
    {
        ValidateBoundaryMutation(clearColor: true);
        SynchronizeContext();
        var value = new Vector4(red, green, blue, alpha);
        if (clearColorKnown && clearColor == value) return;
        clearColorKnown = false;
        GL.ClearColor(red, green, blue, alpha);
        FixedFunctionCalls++;
        clearColor = value;
        clearColorKnown = true;
    }
    #endregion
}
