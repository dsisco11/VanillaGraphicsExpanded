using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

/// <summary>Exercises generated accessors against actual program settings without allocating GPU objects.</summary>
[ShaderProgram("Contract", "generated/accessors", 2, Scope = "generator-fixture")]
[ShaderStage("Contract", ShaderStageKind.Vertex, "tests/render_infrastructure.vsh")]
[ShaderStage("Contract", ShaderStageKind.Fragment, "tests/render_infrastructure.fsh")]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Enabled))]
[ShaderUse("Contract", ShaderStageKind.Fragment, nameof(Steps), SpecializationId = 4, When = "active")]
[ShaderEquals("active", nameof(Enabled), true)]
internal partial class GeneratedAccessorShader : GpuProgram
{
    [ShaderOption("GENERATED_ENABLED", false, Aliases = new[] { "GENERATED_LEGACY" })]
    public partial bool Enabled { get; set; }

    [ShaderOption("GENERATED_STEPS", 10, Minimum = 1, Maximum = 32)]
    public partial int Steps { get; set; }

    internal override GpuShaderContract ProgramContract => Contract;
    internal int ReloadRequests { get; private set; }

    #region Scheduling observation
    /// <summary>Records requests at the existing scheduling boundary without requiring a game API.</summary>
    protected override void RequestRecompile() => ReloadRequests++;
    #endregion
}
