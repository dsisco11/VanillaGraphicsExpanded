using VanillaGraphicsExpanded.Rendering.RHI.Backends.OpenGL;
using OpenTK.Graphics.OpenGL;
using VanillaGraphicsExpanded.Rendering;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>Verifies that GL diagnostics distinguish pending errors from errors inside an operation.</summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public sealed class GlErrorScopeTests : RenderTestBase
{
    /// <summary>Uses the shared real OpenGL context.</summary>
    public GlErrorScopeTests(HeadlessGLFixture fixture) : base(fixture) { }

    #region Attribution
    /// <summary>A pre-existing error is reported before the named operation can begin.</summary>
    [Fact]
    public void PendingErrorIsAttributedToEntry()
    {
        // Arrange: Drain setup errors, then leave one invalid viewport error pending before scope construction.
        EnsureContextValid();
        GlDebug.ThrowIfErrors("Test setup");
        GL.Viewport(0, 0, -1, 1);
        // Act: Constructing the named scope must observe the pending error before its operation can begin.
        var error = Assert.Throws<InvalidOperationException>(() => new GlDebug.ErrorScope("test binding"));
        // Assert: The diagnostic attributes the error to entry and consumes it.
        Assert.Contains("Before test binding", error.Message);
        Assert.Contains("InvalidValue", error.Message);
        Assert.Equal(ErrorCode.NoError, GL.GetError());
    }

    /// <summary>An error generated inside the operation is reported at its exit.</summary>
    [Fact]
    public void OperationErrorIsAttributedToExit()
    {
        // Arrange: Start with no pending driver error so the scope owns the operation's failure.
        EnsureContextValid();
        // Act and assert: The invalid viewport executes inside the scope and is reported at scope exit.
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
