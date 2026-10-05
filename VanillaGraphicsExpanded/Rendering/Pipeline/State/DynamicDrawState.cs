using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete DynamicDrawState values independent of cache knowledge and native operations.</summary>
internal struct DynamicDrawState
{
    /// <summary>Declared X value.</summary>
    public int X;
    /// <summary>Declared Y value.</summary>
    public int Y;
    /// <summary>Declared Width value.</summary>
    public int Width;
    /// <summary>Declared Height value.</summary>
    public int Height;
}
