using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks correspondence between the build-published shader source catalog and generated registry.</summary>
public sealed class ShaderCatalogSourceArtifactTests
{
    #region Public API
    #region Published catalog wiring
    /// <summary>Every published shader source is represented in the generated catalog with unique binary output paths.</summary>
    [Fact]
    public void PublishedCatalogCoversPackagedShaderSources()
    {
        // Arrange: Snapshot source names from the actual build output, including source files eligible for registry wiring.
        string[] extensions = [".vsh", ".fsh", ".csh", ".gsh", ".tcsh", ".tesh", ".glsl"];
        var sources = TestAssetSnapshot.Built.Paths.Where(path => path.StartsWith("shaders/", StringComparison.Ordinal))
            .Where(path => extensions.Contains(Path.GetExtension(path))).Select(path => path["shaders/".Length..])
            // Fixture overlays replace a production source at the same asset path.
            .Where(path => !path.StartsWith("includes/", StringComparison.Ordinal) && Path.GetExtension(path)!=".glsl")
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        // Act: Read source and binary identities from the generated catalog.
        var fixtures = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
        // Assert: Build-published sources map exactly to catalog stages, whose binary paths remain unique.
        Assert.Equal(sources, fixtures.Stages.Values.Select(stage => stage.Source).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal));
        Assert.Equal(fixtures.Binaries.Count, fixtures.Binaries.Select(binary => binary.BinaryPath).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }
    #endregion
    #endregion
}
