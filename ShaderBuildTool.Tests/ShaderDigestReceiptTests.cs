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
            var reasons = new List<string>();
            Assert.False(ShaderBuildReceipt.IsCurrent(directory, "fixture", report: reasons.Add));
            Assert.Contains(reasons, reason => reason.Contains(remove ? "Published output missing" : "Published output content changed", StringComparison.Ordinal)
                && reason.Contains(ShaderBinaryDigest.FileName, StringComparison.Ordinal));
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

    /// <summary>Invalid filesystem characters in untrusted receipt keys cause a miss instead of escaping validation.</summary>
    [Fact]
    public void InvalidOutputPathInReceiptIsRejected()
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(fixture.Output);
        File.WriteAllText(Path.Combine(fixture.Output, "build-receipt.json"),
            System.Text.Json.JsonSerializer.Serialize(new { Inputs = "fixture", Outputs = new Dictionary<string, string> { ["bad\0.spv"] = "digest" } }));
        Assert.False(ShaderBuildReceipt.IsCurrent(fixture.Output, "fixture"));
    }

    /// <summary>Nested shader directories named like private roots remain part of receipt verification.</summary>
    [Fact]
    public void NestedPrivateNamesRemainPublishedOutputs()
    {
        using var fixture = new ShaderBuildFixture();
        string nested = Path.Combine(fixture.Output, "domain", "shaders", "_cache");
        Directory.CreateDirectory(nested);
        string binary = Path.Combine(nested, "fixture.spv");
        File.WriteAllBytes(binary, [1]);
        ShaderBuildReceipt.Publish(fixture.Output, "fixture");
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "fixture"));
        File.WriteAllBytes(binary, [2]);
        Assert.False(ShaderBuildReceipt.IsCurrent(fixture.Output, "fixture"));
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

    #region Invalidation diagnostics
    /// <summary>Reports exact changed compiler inputs and added/removed sources, preserving unchanged receipt reuse.</summary>
    [Fact]
    public void InputChangesIdentifyFilesAndPolicies()
    {
        using var fixture = new ShaderBuildFixture();
        Directory.CreateDirectory(fixture.Output);
        File.WriteAllBytes(Path.Combine(fixture.Output, "fixture.spv"), [1]);
        var before = new Dictionary<string, string> { ["compiler/tool: builder.dll"] = "old", ["shader: removed.csh"] = "source", ["compiler policy"] = "Debug" };
        ShaderBuildReceipt.Publish(fixture.Output, "before", before);
        var reasons = new List<string>();
        Assert.True(ShaderBuildReceipt.IsCurrent(fixture.Output, "before", before, reasons.Add));
        Assert.Empty(reasons);
        var after = new Dictionary<string, string> { ["compiler/tool: builder.dll"] = "new", ["shader: added.csh"] = "source", ["compiler policy"] = "Release" };
        Assert.False(ShaderBuildReceipt.IsCurrent(fixture.Output, "after", after, reasons.Add));
        Assert.Contains(reasons, reason => reason.Contains("Input changed: compiler/tool: builder.dll; old -> new", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.Contains("Input changed: compiler policy; Debug -> Release", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.Contains("Input added: shader: added.csh", StringComparison.Ordinal));
        Assert.Contains(reasons, reason => reason.Contains("Input removed: shader: removed.csh", StringComparison.Ordinal));
    }

    /// <summary>Distinguishes an absent cache key from a corrupt existing binary without invoking the compiler.</summary>
    [Fact]
    public void CacheMissReportsMissingKeyAndCorruptBinary()
    {
        using var fixture = new ShaderBuildFixture();
        var cache = new ShaderVariantCache(fixture.Output, "compiler");
        string key = cache.Key("source", "compute", "main");
        var reasons = new List<string>();
        Assert.False(cache.TryRead(key, out _, out _, reasons.Add));
        Assert.Equal("cache key has no metadata", Assert.Single(reasons));
        byte[] binary = [1, 2, 3];
        cache.Store(key, binary, ShaderVariantCache.Digest(binary));
        reasons.Clear();
        Assert.True(cache.TryRead(key, out _, out _, reasons.Add));
        Assert.Empty(reasons);
        File.WriteAllBytes(Path.Combine(fixture.Output, "_cache", key + ".bin"), [0]);
        Assert.False(cache.TryRead(key, out _, out _, reasons.Add));
        Assert.Equal("cached binary digest/length invalid", Assert.Single(reasons));
    }
    #endregion
}
