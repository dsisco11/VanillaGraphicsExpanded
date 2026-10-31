using VanillaGraphicsExpanded.Rendering.Spirv;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering.Contracts;

/// <summary>Checks immutable packaged shader bytes without driver or context initialization.</summary>
public sealed class PackagedShaderAssetTests
{
    private static readonly ShaderVariantResolver Registry = ShaderBuildTool.Spirv.TestShaderPrograms.Create();
    private static string Root => Path.Combine(AppContext.BaseDirectory, "assets", "shaders");

    #region Public API
    /// <summary>Every packaged variant carries the digest of its exact published binary bytes.</summary>
    [Fact]
    public void PackagedBuildDigestsMatchEveryCompiledVariant()
    {
        var manifest = ShaderBinaryDigest.Parse(TestAssetSnapshot.Built.Copy("shaders/" + ShaderBinaryDigest.FileName));
        Assert.NotNull(manifest);
        Assert.Equal(Registry.Binaries.Count, manifest.Binaries.Count);
        Assert.DoesNotContain(TestAssetSnapshot.Built.Paths, path => path.EndsWith(".sha256", StringComparison.Ordinal));
        foreach (var selection in Registry.Binaries)
        {
            string path = Path.Combine(Root, selection.BinaryPath);
            byte[] binary = TestAssetSnapshot.Built.Copy("shaders/" + selection.BinaryPath);
            Assert.True(ShaderBinaryDigest.TryRead(manifest, selection.BinaryPath, binary.Length, out byte[] digest), path);
            Assert.Equal(System.Security.Cryptography.SHA256.HashData(binary), digest);
        }
    }

    #endregion
}
