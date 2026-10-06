using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns typed numerical inputs separately from the fixture executable.</summary>
internal sealed partial class WaterPixelNormalRefractionShaderProgram
{
    private readonly WaterPixelNormalRefractionUniformBuffer inputs;

    #region Public API
    /// <summary>Attaches input lifetime and mutation validation to the shader owner.</summary>
    public WaterPixelNormalRefractionShaderProgram() => inputs = OwnUniformBuffer(new WaterPixelNormalRefractionUniformBuffer());

    /// <summary>Supplies the view-space interface point.</summary>
    public Vector3 Surface { get => inputs.Surface; set => inputs.Surface = value; }

    /// <summary>Supplies the oriented view-space interface normal.</summary>
    public Vector3 Normal { get => inputs.Normal; set => inputs.Normal = value; }

    /// <summary>Projects optical geometry using the complete camera projection.</summary>
    public Matrix4x4 Projection { get => inputs.Projection; set => inputs.Projection = value; }

    /// <summary>Supplies original view dimensions independently of background resolution.</summary>
    public Vector2 FrameSize { get => inputs.FrameSize; set => inputs.FrameSize = value; }

    /// <summary>Selects the submerged-camera exit interface.</summary>
    public int Underwater { get => inputs.Underwater; set => inputs.Underwater = value; }

    /// <summary>Reconstructs receivers with the CPU inverse of the supplied camera projection.</summary>
    public Matrix4x4 InverseProjection { get => inputs.InverseProjection; set => inputs.InverseProjection = value; }

    /// <summary>Supplies the underlying geometric normal before wave detail.</summary>
    public Vector3 BaseNormal { get => inputs.BaseNormal; set => inputs.BaseNormal = value; }
    #endregion

    #region Binding sources
    /// <summary>Supplies the owned block to generated prepared submission.</summary>
    CpuUniformBuffer IWaterPixelNormalRefractionBindings.Inputs => inputs;
    #endregion
}
