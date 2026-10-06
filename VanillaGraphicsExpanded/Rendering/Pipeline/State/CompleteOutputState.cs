using OpenTK.Graphics.OpenGL;
namespace VanillaGraphicsExpanded.Rendering;
/// <summary>Native output parameter values and their independently valid fields.</summary>
internal struct CompleteOutputState
{
    /// <summary>Independent knowledge for observed native values.</summary>
    public CompleteOutputKnowledge Known;
    /// <summary>Supplemental native enable values and their validity.</summary>
    public CompleteEnableState Enables;
    /// <summary>Declared or observed LogicOp LogicOperation value.</summary>
    public LogicOp LogicOperation;
}
