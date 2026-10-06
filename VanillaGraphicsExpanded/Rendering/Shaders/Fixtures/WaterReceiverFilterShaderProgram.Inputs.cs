using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns typed numerical inputs separately from the fixture executable.</summary>
internal sealed partial class WaterReceiverFilterShaderProgram
{
    private readonly WaterReceiverFilterUniformBuffer inputs;

    #region Public API
    /// <summary>Attaches input lifetime and mutation validation to the shader owner.</summary>
    public WaterReceiverFilterShaderProgram() => inputs = OwnUniformBuffer(new WaterReceiverFilterUniformBuffer());

    /// <summary>Locates the requested normalized background sample.</summary>
    public Vector2 SampleUv { get => inputs.SampleUv; set => inputs.SampleUv = value; }

    /// <summary>Supplies the local interface point in view coordinates.</summary>
    public Vector3 Surface { get => inputs.Surface; set => inputs.Surface = value; }

    /// <summary>Supplies the oriented local interface normal.</summary>
    public Vector3 Normal { get => inputs.Normal; set => inputs.Normal = value; }

    /// <summary>Reconstructs receiver positions independently of background dimensions.</summary>
    public Matrix4x4 InverseProjection { get => inputs.InverseProjection; set => inputs.InverseProjection = value; }

    /// <summary>Distinguishes original framebuffer dimensions from reduced background dimensions.</summary>
    public Vector2 FullFrameSize { get => inputs.FullFrameSize; set => inputs.FullFrameSize = value; }
    #endregion

    #region Binding sources
    /// <summary>Supplies the owned block to generated prepared submission.</summary>
    CpuUniformBuffer IWaterReceiverFilterBindings.Inputs => inputs;
    #endregion
}
