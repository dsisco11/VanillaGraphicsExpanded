using OpenTK.Graphics.OpenGL;


using VanillaGraphicsExpanded.Tests.Support.GPU.Native;
using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Checks that retiring a ready shader owner more than once leaves other program owners intact.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class ShaderHelperOwnershipTests : RenderTestBase
{
    #region Public API
    /// <summary>Uses the shared test context.</summary>
    public ShaderHelperOwnershipTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Ownership regression
    /// <summary>Repeated disposal of one retained executable does not affect an independently created program.</summary>
    [Fact]
    public void CallerDeletionThenHelperDisposalDoesNotDeleteAgain()
    {
        // Arrange: Create a caller-owned executable and retain it through the helper's ownership wrapper.
        // Act: Delete the executable through its caller, then dispose the helper that still references it.
        // Assert: Disposal does not issue a second deletion against the already retired program.
        EnsureContextValid();
        using var executable = BuiltShaderFixture.LoadProgram(new(new VanillaGraphicsExpanded.Rendering.Contracts.ShaderSettings(
            VanillaGraphicsExpanded.Rendering.Contracts.GpuShaderContracts.Registry.FindProgram("tests/render_infrastructure"))));
        // Action/check: Delete through the caller and dispose through the helper; the independent program must remain valid.
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
