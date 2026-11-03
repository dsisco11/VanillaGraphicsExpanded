using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

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
        _fixture.EnsureContextValid();
        
        Assert.NotNull(_fixture.GLVersion);
        Assert.NotNull(_fixture.GLRenderer);
        Assert.True(_fixture.IsContextValid);
    }

    /// <summary>Verifies processed shader diagnostics can be constructed for the test assets.</summary>
    [Fact]
    public void ShaderProcessedSourceDiagnostics_CanBeCreated()
    {
        _fixture.EnsureContextValid();
        
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        // Skip if assets not found (e.g., running from wrong directory)
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!TestAssetSnapshot.HasDirectory(includePath), $"Include path not found: {includePath}");

        var helper = new ShaderProcessedSourceDiagnostics();
        Assert.NotNull(helper);
    }

    #endregion
}
