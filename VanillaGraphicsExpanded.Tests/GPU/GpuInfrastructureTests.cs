using VanillaGraphicsExpanded.Tests.GPU.Fixtures;
using VanillaGraphicsExpanded.Tests.GPU.Helpers;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU;

/// <summary>
/// Sanity tests to verify the GPU test infrastructure works.
/// </summary>
[Collection("GPU")]
[Trait("Category", "GPU")]
public class GpuInfrastructureTests
{
    private readonly HeadlessGLFixture _fixture;

    public GpuInfrastructureTests(HeadlessGLFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public void HeadlessGLFixture_CreatesValidContext()
    {
        _fixture.EnsureContextValid();
        
        Assert.NotNull(_fixture.GLVersion);
        Assert.NotNull(_fixture.GLRenderer);
        Assert.True(_fixture.IsContextValid);
    }

    [Fact]
    public void ShaderTestHelper_CanBeCreated()
    {
        _fixture.EnsureContextValid();
        
        var shaderPath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders");
        var includePath = Path.Combine(AppContext.BaseDirectory, "assets", "shaders", "includes");

        // Skip if assets not found (e.g., running from wrong directory)
        Assert.SkipWhen(!Directory.Exists(shaderPath), $"Shader path not found: {shaderPath}");
        Assert.SkipWhen(!Directory.Exists(includePath), $"Include path not found: {includePath}");

        using var helper = new ShaderTestHelper(shaderPath, includePath);
        Assert.NotNull(helper);
    }
}
