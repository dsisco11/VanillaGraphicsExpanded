using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Checks that retiring a ready shader owner more than once leaves other program owners intact.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderHelperOwnershipTests : RenderTestBase
{
    /// <summary>Uses the shared test context.</summary>
    public ShaderHelperOwnershipTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Public API
    #region Ownership regression
    /// <summary>Repeated disposal of one retained executable does not affect an independently created program.</summary>
    [Fact]
    public void CallerDeletionThenHelperDisposalDoesNotDeleteAgain()
    {
        EnsureContextValid();
        using var executable = BuiltShaderFixture.LoadProgram(new(new VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(
            VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram("tests/render_infrastructure"))));
        executable.Dispose();
        var survivor = NativeResourceSetup.CreateProgram();
        try
        {
            executable.Dispose();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.True(GL.IsProgram(survivor.ProgramId));
        }
        finally { survivor.Dispose(); }
    }
    #endregion
    #endregion
}
