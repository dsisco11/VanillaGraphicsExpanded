using ShaderBuildTool.Generation;
using ShaderBuildTool.Tests.Support.Generation;

namespace ShaderBuildTool.Tests.Component.Spirv.Build;

/// <summary>Checks build invocations outside the publication path do not generate angular-table input.</summary>
public sealed class LumonOctahedralShGenerationTests
{
    #region Generation and incremental repair

    /// <summary>Help and invalid arguments cannot create generated shader input.</summary>
    [Fact]
    public void NonBuildInvocationsDoNotGenerateInput()
    {
        using var fixture = new ShaderBuildFixture();
        Assert.Equal(0, Program.Main(["--help", "--assetsRoot", fixture.Assets, "--outputRoot", fixture.Output]));
        Assert.Equal(2, Program.Main(["--assetsRoot", fixture.Assets]));
        Assert.False(Directory.Exists(Path.Combine(fixture.Shaders, "includes")));
    }
    #endregion
}
