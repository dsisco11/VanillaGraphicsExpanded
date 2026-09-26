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
    #endregion
}
