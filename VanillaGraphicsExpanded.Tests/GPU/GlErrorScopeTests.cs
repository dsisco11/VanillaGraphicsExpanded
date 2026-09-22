using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;
using VanillaGraphicsExpanded.Tests.GPU.Fixtures;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>Verifies that GL diagnostics distinguish pending errors from errors inside an operation.</summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public sealed class GlErrorScopeTests : RenderTestBase
{
    /// <summary>Uses the shared real OpenGL context.</summary>
    public GlErrorScopeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Attribution
    /// <summary>A pre-existing error is reported before the named operation can begin.</summary>
    [Fact]
    public void PendingErrorIsAttributedToEntry()
    {
        EnsureContextValid();
        GlDebug.ThrowIfErrors("Test setup");
        GL.Viewport(0, 0, -1, 1);
        var error = Assert.Throws<InvalidOperationException>(() => new GlDebug.ErrorScope("test binding"));
        Assert.Contains("Before test binding", error.Message);
        Assert.Contains("InvalidValue", error.Message);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An error generated inside the operation is reported at its exit.</summary>
    [Fact]
    public void OperationErrorIsAttributedToExit()
    {
        EnsureContextValid();
        var error = Assert.Throws<InvalidOperationException>(() =>
        {
            using var scope = new GlDebug.ErrorScope("test binding");
            GL.Viewport(0, 0, -1, 1);
        });
        Assert.Contains("After test binding", error.Message);
        Assert.Contains("InvalidValue", error.Message);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }
    #endregion
}
