using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering;

/// <summary>
/// Sanity tests to verify the GPU test infrastructure works.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GpuIntegration")]
public class GpuInfrastructureTests
{
    private readonly HeadlessGLFixture _fixture;

    #region Public API

    /// <summary>Creates infrastructure tests using the shared GPU fixture.</summary>
    /// <param name="fixture">The headless graphics fixture.</param>
    public GpuInfrastructureTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    /// <summary>Verifies that the shared headless fixture exposes a valid graphics context.</summary>
    [Fact]
    public void HeadlessGLFixture_CreatesValidContext()
    {
        // Arrange: Validate that the shared fixture can make its headless OpenGL context current.
        _fixture.EnsureContextValid();
        // Act and assert: Read the fixture's initialized driver identity and context status.
        Assert.NotNull(_fixture.GLVersion);
        Assert.NotNull(_fixture.GLRenderer);
        Assert.True(_fixture.IsContextValid);
    }


    #endregion
}
