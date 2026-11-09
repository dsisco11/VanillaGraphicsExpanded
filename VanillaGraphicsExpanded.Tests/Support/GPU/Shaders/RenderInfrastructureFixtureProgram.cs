using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.RHI;
using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Rendering.Shaders.Fixtures;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

/// <summary>Owns the declared fullscreen shader used to verify render test infrastructure output.</summary>
internal sealed class RenderInfrastructureFixtureProgram : GpuProgram
{
    #region Public API
    /// <summary>Creates the packaged infrastructure shader owner.</summary>
    public RenderInfrastructureFixtureProgram() { }

    /// <summary>Uses the generated declaration for the packaged infrastructure shader stages.</summary>
    internal override GpuShaderContract ProgramContract => RenderInfrastructureShaderProgram.Contract;
    #endregion

    #region Protected API
    /// <summary>Captures the shader's declared empty resource table.</summary>
    protected override RHIResourceBindings CaptureBindingsCore() => new(Shader);
    #endregion
}
