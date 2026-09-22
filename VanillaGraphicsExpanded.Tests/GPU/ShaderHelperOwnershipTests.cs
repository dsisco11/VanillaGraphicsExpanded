using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Exercises ownership transfer between raw-handle fixture callers and their allocating helper.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class ShaderHelperOwnershipTests : RenderTestBase
{
    /// <summary>Uses the shared test context.</summary>
    public ShaderHelperOwnershipTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Ownership regression
    /// <summary>Explicit caller deletion releases helper ownership before a later disposal can reuse stale handles.</summary>
    [Fact]
    public void CallerDeletionThenHelperDisposalDoesNotDeleteAgain()
    {
        EnsureContextValid();
        string root = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        using var helper = new ShaderTestHelper(root, Path.Combine(root, "includes"));
        var result = helper.CompileProgram("tests/render_infrastructure");
        Assert.True(result.IsSuccess, result.ErrorMessage);
        GL.GetProgram(result.ProgramId, GetProgramParameterName.AttachedShaders, out int count);
        var attached = new int[count];
        GL.GetAttachedShaders(result.ProgramId, count, out _, attached);
        TestShaderInterfaces.DeleteProgram(result.ProgramId);
        foreach (int shader in attached) TestShaderInterfaces.DeleteShader(shader);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
        int survivor = GL.CreateProgram();
        try
        {
            helper.Dispose();
            helper.Dispose();
            Assert.Equal(ErrorCode.NoError, GL.GetError());
            Assert.True(GL.IsProgram(survivor));
        }
        finally { GL.DeleteProgram(survivor); }
    }
    #endregion
}
