using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Integration.Rendering.Shaders;

/// <summary>Checks published shader artifacts against their generated catalog and digest manifest.</summary>
public sealed class PackagedShaderArtifactTests
{
    private static readonly ShaderVariantResolver Registry = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
    private static string Root => Path.Combine(AppContext.BaseDirectory, "assets", "shaders");

    #region Public API
    /// <summary>Every generated catalog variant matches the digest of its published binary payload.</summary>
    [Fact]
    public void PublishedShaderBinariesMatchCatalogDigests()
    {
        // Arrange: Read the build-produced manifest and derive the generated binary catalog.
        var manifest = ShaderBinaryDigest.Parse(TestAssetSnapshot.Built.Copy("shaders/" + ShaderBinaryDigest.FileName));
        Assert.NotNull(manifest);
        // Act: Read each published binary payload and resolve its manifest digest.
        Assert.Equal(Registry.Binaries.Count, manifest.Binaries.Count);
        Assert.DoesNotContain(TestAssetSnapshot.Built.Paths, path => path.EndsWith(".sha256", StringComparison.Ordinal));
        foreach (var selection in Registry.Binaries)
        {
            string path = Path.Combine(Root, selection.BinaryPath);
            byte[] binary = TestAssetSnapshot.Built.Copy("shaders/" + selection.BinaryPath);
            Assert.True(ShaderBinaryDigest.TryRead(manifest, selection.BinaryPath, binary.Length, out byte[] digest), path);
            // Assert: The manifest's digest describes the exact build-published bytes.
            Assert.Equal(System.Security.Cryptography.SHA256.HashData(binary), digest);
        }
    }

    #endregion
}
