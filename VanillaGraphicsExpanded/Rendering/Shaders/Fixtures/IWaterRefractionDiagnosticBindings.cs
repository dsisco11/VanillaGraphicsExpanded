using System.Numerics;
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
    /// <summary>Supplies full-frame projection dimensions.</summary>
    [ShaderBinding("frameSize", ShaderBindingKind.UniformLocation, 121, ShaderStageKind.Fragment)]
    Vector2 FrameSize { get; set; }
    /// <summary>Supplies an optional independently reconstructed surface for diagnostic scenario twelve.</summary>
    [ShaderBinding("customSurface", ShaderBindingKind.UniformLocation, 122, ShaderStageKind.Fragment)]
    Vector3 Surface { get; set; }
    /// <summary>Supplies the corresponding oriented view-space water normal.</summary>
    [ShaderBinding("customNormal", ShaderBindingKind.UniformLocation, 123, ShaderStageKind.Fragment)]
    Vector3 Normal { get; set; }
    /// <summary>Caps all receiver evaluations performed by the ray-only entry point.</summary>
    [ShaderBinding("diagnosticBudget", ShaderBindingKind.UniformLocation, 124, ShaderStageKind.Fragment)]
    int Budget { get; set; }
    /// <summary>Selects the complete tier dispatcher instead of the isolated ray algorithm.</summary>
    [ShaderBinding("diagnosticSelect", ShaderBindingKind.UniformLocation, 125, ShaderStageKind.Fragment)]
    int SelectReceiver { get; set; }
    /// <summary>Chooses the production quality identifier when dispatch is requested.</summary>
    [ShaderBinding("diagnosticQuality", ShaderBindingKind.UniformLocation, 126, ShaderStageKind.Fragment)]
    int Quality { get; set; }
    /// <summary>Selects an underwater exit for custom optical geometry.</summary>
    [ShaderBinding("diagnosticUnderwater", ShaderBindingKind.UniformLocation, 127, ShaderStageKind.Fragment)]
    int Underwater { get; set; }
    #endregion
}
