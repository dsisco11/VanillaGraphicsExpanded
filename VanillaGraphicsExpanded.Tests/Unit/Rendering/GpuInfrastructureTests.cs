using VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

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


    /// <summary>Verifies processed shader diagnostics can be constructed for the test assets.</summary>
    [Fact]
    public void ShaderProcessedSourceDiagnostics_CanBeCreated()
    {
        // Arrange: Require the shared headless context and locate both copied shader asset directories.
        _fixture.EnsureContextValid();
        
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        // Skip if assets not found (e.g., running from wrong directory)
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        // Act: Construct the helper after confirming its shader inputs are available.
        var helper = new ShaderProcessedSourceDiagnostics();
        // Assert: Construction succeeds for the test asset layout.
        Assert.NotNull(helper);
    }

    #endregion
}
