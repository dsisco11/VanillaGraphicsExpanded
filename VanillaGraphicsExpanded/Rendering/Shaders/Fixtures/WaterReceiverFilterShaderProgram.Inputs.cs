using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns typed numerical inputs separately from the fixture executable.</summary>
internal sealed partial class WaterReceiverFilterShaderProgram
{
    private readonly WaterReceiverFilterUniformBuffer inputs;

    private VgeFrameUniformBuffer? frameInputs;

    #region Public API
    /// <summary>Retains the shared camera used to author this optical fixture.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Attaches input lifetime and mutation validation to the shader owner.</summary>
    public WaterReceiverFilterShaderProgram() => inputs = OwnUniformBuffer(new WaterReceiverFilterUniformBuffer());

    /// <summary>Locates the requested normalized background sample.</summary>
    public Vector2 SampleUv { get => inputs.SampleUv; set => inputs.SampleUv = value; }

    /// <summary>Supplies the local interface point in view coordinates.</summary>
    public Vector3 Surface { get => inputs.Surface; set => inputs.Surface = value; }

    /// <summary>Supplies the oriented local interface normal.</summary>
    public Vector3 Normal { get => inputs.Normal; set => inputs.Normal = value; }

    #endregion

    #region Binding sources
    /// <summary>Supplies the explicit fixture snapshot or current world camera.</summary>
    CpuUniformBuffer IWaterReceiverFilterBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Supplies the owned block to generated prepared submission.</summary>
    CpuUniformBuffer IWaterReceiverFilterBindings.Inputs => inputs;
    #endregion
}
