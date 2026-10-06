using System.Numerics;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Declares diagnostic inputs without changing the production traversal resource layout.</summary>
internal interface IWaterRefractionDiagnosticBindings
{
    #region Public API
    /// <summary>Publishes the complete retained optical input block.</summary>
    [ShaderBinding("WaterRefractionDiagnosticInputs", ShaderBindingKind.UniformBlock, GpuBindingRegistry.Ubo.ShaderInputs, ShaderStageKind.Fragment)]
    CpuUniformBuffer Inputs { get; }
    /// <summary>Supplies immutable receiver radiance and coverage metadata.</summary>
    [ShaderBinding("vge_refractionColor", ShaderBindingKind.Sampler, 9, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Color { get; set; }
    /// <summary>Supplies independently projected opaque receiver depths.</summary>
    [ShaderBinding("vge_refractionDepth", ShaderBindingKind.Sampler, 10, ShaderStageKind.Fragment, Sampler = ShaderSamplerPolicy.NearestClamp)]
    DynamicTexture2D Depth { get; set; }
    /// <summary>Selects the independently authored optical geometry.</summary>
    int Scenario { get; set; }

    /// <summary>Supplies full-frame projection dimensions.</summary>
    Vector2 FrameSize { get; set; }

    /// <summary>Supplies an optional independently reconstructed surface for diagnostic scenario twelve.</summary>
    Vector3 Surface { get; set; }

    /// <summary>Supplies the corresponding oriented view-space water normal.</summary>
    Vector3 Normal { get; set; }

    /// <summary>Caps all receiver evaluations performed by the ray-only entry point.</summary>
    int Budget { get; set; }

    /// <summary>Selects raw ray traversal (zero), the tier dispatcher (one), or standalone UV (two).</summary>
    int SelectReceiver { get; set; }

    /// <summary>Chooses the production quality identifier when dispatch is requested.</summary>
    int Quality { get; set; }

    /// <summary>Selects an underwater exit for custom optical geometry.</summary>
    int Underwater { get; set; }

    /// <summary>Supplies the CPU-authored projection used to generate the receiver depths.</summary>
    Matrix4x4 Projection { get; set; }

    /// <summary>Supplies its CPU inverse for production receiver reconstruction.</summary>
    Matrix4x4 InverseProjection { get; set; }
    #endregion
}
