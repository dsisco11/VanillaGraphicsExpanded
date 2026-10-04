using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares diagnostic inputs without changing the production traversal resource layout.</summary>
internal interface IWaterRefractionDiagnosticBindings
{
    #region Public API
    /// <summary>Selects the independently authored optical geometry.</summary>
    [ShaderBinding("diagnosticScenario", ShaderBindingKind.UniformLocation, 120, ShaderStageKind.Fragment)]
    int Scenario { get; set; }
    /// <summary>Supplies immutable receiver radiance and coverage metadata.</summary>
    [ShaderBinding("vge_refractionColor", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Color { get; set; }
    /// <summary>Supplies independently projected opaque receiver depths.</summary>
    [ShaderBinding("vge_refractionDepth", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Depth { get; set; }
    #endregion
}
