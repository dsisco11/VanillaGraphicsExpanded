using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering.Pipeline.State;
/// <summary>Concrete output values independent of cache knowledge and native operations.</summary>
internal struct OutputState
{
    /// <summary>Declared or observed LogicOp LogicOperation value.</summary>
    public LogicOp LogicOperation;
    /// <summary>Cached native FramebufferSrgb enable value.</summary>
    public bool FramebufferSrgb;
    /// <summary>Cached native Dither enable value.</summary>
    public bool Dither;
    /// <summary>Cached native ColorLogicOp enable value.</summary>
    public bool ColorLogicOp;
}
