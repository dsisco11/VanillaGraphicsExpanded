using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Restores surviving particle SSAO metadata without replacing deferred material receivers early.</summary>
[ShaderProgram("Contract", "scene_color_particle_ssao", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh", Identity = "scene_color_particle_ssao.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "scene_color_particle_ssao.fsh")]
public sealed partial class SceneColorParticleSsaoShaderProgram : GpuProgram, ISceneColorParticleSsaoShaderProgramBindings
{
    #region Public API
    /// <summary>Registers the immutable input layout used by the metadata restoration draw.</summary>
    public SceneColorParticleSsaoShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }
    /// <summary>Receives engine visibility after all opaque submissions.</summary>
    public partial int VisibilityDepth { set; }
    /// <summary>Receives depth before cube particles overwrite visibility.</summary>
    public partial int BeforeDepth { set; }
    /// <summary>Receives depth after the original cube-particle draw.</summary>
    public partial int AfterDepth { set; }
    /// <summary>Receives isolated original particle normal metadata.</summary>
    public partial DynamicTexture2D? ParticleNormal { set; }
    /// <summary>Receives isolated original particle position metadata.</summary>
    public partial DynamicTexture2D? ParticlePosition { set; }
    #endregion

    #region Internal API
    /// <summary>Uses normal generated contracts and engine shader compilation.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
