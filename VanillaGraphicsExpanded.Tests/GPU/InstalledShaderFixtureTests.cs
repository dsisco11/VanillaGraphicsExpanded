using VanillaGraphicsExpanded.Rendering.Shaders;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that original installed stages link with their reflected GLSL interface on the real driver.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class InstalledShaderFixtureTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Only a successfully linked installed GLSL program publishes the live scene convention capability.</summary>
    [Theory]
    [InlineData("particlescube", 0)]
    [InlineData("particlescube", 1)]
    [InlineData("final", 0)]
    public void InstalledStagesLinkAndExposeSceneConvention(string name, int ssao)
    {
        EnsureContextValid();
        using var shader = new InstalledShaderFixture(name, ssao);
        Assert.True(shader.Engine.HasUniform("vge_sceneLinear"));
        Assert.True(ShaderCapabilities.Has(shader.Engine, ShaderCapability.SceneColorConvention));
        Assert.True(shader.Layout.GetUniformLocation(shader.Engine.ProgramId, "vge_sceneLinear") >= 0);
    }
    #endregion
}
