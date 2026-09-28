using ShaderBuildTool.Spirv;
using System.Security.Cryptography;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Tests;

/// <summary>Ensures incremental build receipts retain paired binary and digest outputs.</summary>
public sealed class ShaderDigestReceiptTests
{
    #region Receipt integrity
    /// <summary>Deleting or corrupting a published digest forces regeneration instead of preserving an incomplete package.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrChangedDigestInvalidatesReceipt(bool remove)
    {
        string directory = Path.Combine(Path.GetTempPath(), "VGE.DigestReceipt", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            byte[] binary = [1, 2, 3, 4];
            string path = Path.Combine(directory, "fixture.spv");
            File.WriteAllBytes(path, binary);
            string manifest = Path.Combine(directory, ShaderBinaryDigest.FileName);
            File.WriteAllBytes(manifest, ShaderBinaryDigest.Encode(new Dictionary<string, ShaderBinaryDigest.Entry>
            { ["fixture.spv"] = new(binary.Length, Convert.ToHexString(SHA256.HashData(binary))) }));
            ShaderBuildReceipt.Publish(directory, "fixture");
            Assert.True(ShaderBuildReceipt.IsCurrent(directory, "fixture"));
            if (remove) File.Delete(manifest);
            else File.WriteAllBytes(manifest, [0]);
            Assert.False(ShaderBuildReceipt.IsCurrent(directory, "fixture"));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    /// <summary>Compiler scratch files and variant-cache contents neither enter nor invalidate the published receipt.</summary>
    [Fact]
    public void CacheAndTemporaryContentsDoNotAffectReceipt()
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(fixture.Output);
        File.WriteAllBytes(Path.Combine(fixture.Output, "fixture.spv"), [1, 2, 3]);
        foreach (string excluded in new[] { "_cache", "_tmp" })
        {
            string nested = Path.Combine(fixture.Output, excluded, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "existing.bin"), "initial");
        }
        ShaderBuildReceipt.Publish(fixture.Output, "fixture");
        string receipt = File.ReadAllText(Path.Combine(fixture.Output, "build-receipt.json"));
        Assert.DoesNotContain("_cache", receipt);
        Assert.DoesNotContain("_tmp", receipt);
        foreach (string excluded in new[] { "_cache", "_tmp" })
        {
            string nested = Path.Combine(fixture.Output, excluded, "nested");
            File.WriteAllText(Path.Combine(nested, "existing.bin"), "changed");
            File.WriteAllText(Path.Combine(nested, "new.bin"), "new");
        }
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "fixture"));
        Directory.Delete(Path.Combine(fixture.Output, "_cache"), recursive: true);
        Directory.Delete(Path.Combine(fixture.Output, "_tmp"), recursive: true);
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "fixture"));
    }

    /// <summary>Malformed or incomplete receipt data requests a rebuild instead of accepting an invalid success marker.</summary>
    [Theory]
    [InlineData("{")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("{\"Inputs\":\"fixture\",\"Outputs\":null}")]
    [InlineData("{\"Inputs\":\"fixture\",\"Outputs\":{}}")]
    public void MalformedReceiptIsRejected(string receipt)
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(fixture.Output);
        File.WriteAllText(Path.Combine(fixture.Output, "build-receipt.json"), receipt);
        Assert.False(ShaderBuildReceipt.IsCurrent(fixture.Output, "fixture"));
    }
    #endregion
}
