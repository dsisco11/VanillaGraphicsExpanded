using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Shaders;

namespace VanillaGraphicsExpanded.PBR.SceneColor;

/// <summary>Separates deferred receiver depth from ordered particle radiance without modifying engine visibility.</summary>
[ShaderProgram("Contract", "scene_color_particles", 2)]
[ShaderStage("Contract", ShaderStageKind.Vertex, "pbr_composite.vsh", Identity = "scene_color_particles.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "scene_color_particles.fsh")]
public sealed partial class SceneColorParticleShaderProgram : GpuProgram, ISceneColorParticleShaderProgramBindings
{
    #region Public API
    /// <summary>Registers the immutable input layout for the receiver separation draw.</summary>
    public SceneColorParticleShaderProgram()
    {
        ProgramLayout.RegisterContract(Contract.Stages[1].Bindings);
    }

    /// <summary>Receives current engine depth without sampling an active output attachment.</summary>
    public partial int VisibilityDepth { set; }
    /// <summary>Receives the depth snapshot preceding the unchanged particle callback.</summary>
    public partial int BeforeDepth { set; }
    /// <summary>Receives the depth snapshot following the unchanged particle callback.</summary>
    public partial int AfterDepth { set; }
    /// <summary>Receives linear premultiplied particle radiance and coverage.</summary>
    public partial int ParticleColor { set; }
    #endregion

    #region Internal API
    /// <summary>Uses the generated shader contract and normal engine compilation path.</summary>
    internal override GpuShaderContract ProgramContract => Contract;
    #endregion
}
