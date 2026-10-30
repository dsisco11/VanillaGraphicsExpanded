using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that original installed stages link with their reflected GLSL interface on the real driver.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class InstalledShaderFixtureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Patched particle programs expose the scene convention; the superseded native final program stays unmodified.</summary>
    [Theory]
    [InlineData("particlescube", 0)]
    [InlineData("particlescube", 1)]
    [InlineData("final", 0)]
    public void InstalledStagesLinkAndExposeSceneConvention(string name, int ssao)
    {
        EnsureContextValid();
        using var shader = new InstalledShaderFixture(name, ssao);
        bool consumesSceneConvention = name != "final";
        Assert.Equal(consumesSceneConvention, shader.Engine.HasUniform("vge_sceneLinear"));
        Assert.Equal(consumesSceneConvention, ShaderCapabilities.Has(shader.Engine, ShaderCapability.SceneColorConvention));
        Assert.Equal(consumesSceneConvention, shader.Layout.GetUniformLocation(shader.Engine.ProgramId, "vge_sceneLinear") >= 0);
    }
    #endregion
}
