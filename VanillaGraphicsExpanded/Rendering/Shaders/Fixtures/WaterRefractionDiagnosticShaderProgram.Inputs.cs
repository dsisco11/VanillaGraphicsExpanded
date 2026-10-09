using System.Numerics;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Owns typed numerical inputs separately from the fixture executable.</summary>
internal sealed partial class WaterRefractionDiagnosticShaderProgram
{
    private readonly WaterRefractionDiagnosticUniformBuffer inputs;

    private VgeFrameUniformBuffer? frameInputs;

    #region Public API
    /// <summary>Retains the shared camera used to author this optical fixture.</summary>
    internal VgeFrameUniformBuffer FrameInputs { set { RequireInputMutation(); frameInputs = value; } }
    /// <summary>Attaches input lifetime and mutation validation to the shader owner.</summary>
    public WaterRefractionDiagnosticShaderProgram() => inputs = OwnUniformBuffer(new WaterRefractionDiagnosticUniformBuffer());

    /// <summary>Selects the independently authored optical geometry.</summary>
    public int Scenario { get => inputs.Scenario; set => inputs.Scenario = value; }

    /// <summary>Supplies an optional independently reconstructed surface for diagnostic scenario twelve.</summary>
    public Vector3 Surface { get => inputs.Surface; set => inputs.Surface = value; }

    /// <summary>Supplies the corresponding oriented view-space water normal.</summary>
    public Vector3 Normal { get => inputs.Normal; set => inputs.Normal = value; }

    /// <summary>Caps all receiver evaluations performed by the ray-only entry point.</summary>
    public int Budget { get => inputs.Budget; set => inputs.Budget = value; }

    /// <summary>Selects raw ray traversal (zero), the tier dispatcher (one), or standalone UV (two).</summary>
    public int SelectReceiver { get => inputs.SelectReceiver; set => inputs.SelectReceiver = value; }

    /// <summary>Chooses the production quality identifier when dispatch is requested.</summary>
    public int Quality { get => inputs.Quality; set => inputs.Quality = value; }

    /// <summary>Selects an underwater exit for custom optical geometry.</summary>
    public int Underwater { get => inputs.Underwater; set => inputs.Underwater = value; }

    #endregion

    #region Binding sources
    /// <summary>Supplies the explicit fixture snapshot or current world camera.</summary>
    CpuUniformBuffer IWaterRefractionDiagnosticBindings.FrameInputs => frameInputs ?? VgeFrameRenderer.Current;
    /// <summary>Supplies the owned block to generated prepared submission.</summary>
    CpuUniformBuffer IWaterRefractionDiagnosticBindings.Inputs => inputs;
    #endregion
}
