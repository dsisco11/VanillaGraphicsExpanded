using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns typed numerical inputs separately from the fixture executable.</summary>
internal sealed partial class WaterPixelNormalRefractionShaderProgram
{
    private readonly WaterPixelNormalRefractionUniformBuffer inputs;

    private VgeFrameUniformBuffer? frameInputs;

    #region Public API
    /// <summary>Retains the shared camera used to author this optical fixture.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Attaches input lifetime and mutation validation to the shader owner.</summary>
    public WaterPixelNormalRefractionShaderProgram() => inputs = OwnUniformBuffer(new WaterPixelNormalRefractionUniformBuffer());

    /// <summary>Supplies the view-space interface point.</summary>
    public Vector3 Surface { get => inputs.Surface; set => inputs.Surface = value; }

    /// <summary>Supplies the oriented view-space interface normal.</summary>
    public Vector3 Normal { get => inputs.Normal; set => inputs.Normal = value; }

    /// <summary>Selects the submerged-camera exit interface.</summary>
    public int Underwater { get => inputs.Underwater; set => inputs.Underwater = value; }

    /// <summary>Supplies the underlying geometric normal before wave detail.</summary>
    public Vector3 BaseNormal { get => inputs.BaseNormal; set => inputs.BaseNormal = value; }
    #endregion

    #region Binding sources
    /// <summary>Supplies the explicit fixture snapshot or current world camera.</summary>
    CpuUniformBuffer IWaterPixelNormalRefractionBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Supplies the owned block to generated prepared submission.</summary>
    CpuUniformBuffer IWaterPixelNormalRefractionBindings.Inputs => inputs;
    #endregion
}
