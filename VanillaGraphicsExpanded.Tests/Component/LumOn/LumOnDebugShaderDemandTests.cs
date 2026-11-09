using VanillaGraphicsExpanded.Rendering.Shaders;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.LumOn;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.ProgramBinaries;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Component.LumOn;

/// <summary>Checks unused debug-program declarations retain settings without performing GPU work.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class LumOnDebugShaderDemandTests(HeadlessGLFixture fixture) : RenderTestBase(fixture)
{
    #region Public API
    /// <summary>Declaring and configuring unused programs does not read assets, link, register or queue work.</summary>
    [Fact]
    public void UnusedDeclarationsRetainSettingsWithoutGpuWork()
    {
        // Arrange the production declarations behind a recording binary asset API.
        EnsureContextValid();
        using var assets = new BinaryShaderApiFixture();
        // Act by registering every declaration and changing settings without selecting a program.
        Assert.True(LumOnDebugShaderProgramFamily.Register(assets.Api));
        var declarations = LumOnDebugShaderProgramFamily.GetAll();
        Assert.Equal(LumOnDebugShaderProgram.Contracts.Select(contract => contract.Identity).Order(),
            declarations.Select(program => program.PassName).Order());
        foreach (var program in declarations)
        {
            Assert.Equal(0, program.ProgramId);
            Assert.True(program.SetDefines(new Dictionary<string, string?> { ["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"] = "true" }));
            Assert.Equal("1", program.RequestedSettings.Values["VGE_LUMON_DIRECT_LOCAL_VISIBILITY"].Canonical);
        }
        // Assert configuration is retained while reads, prepared programs, and scheduled work remain absent.
        Assert.Empty(assets.Reads);
        Assert.Empty(assets.PreparedPrograms);
        Assert.Empty(assets.ScheduledTasks);
    }

    #endregion

}
