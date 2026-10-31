using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VanillaGraphicsExpanded.Tests.Unit.Rendering;

/// <summary>Checks capture input failures without native resources or physical files.</summary>
public sealed class CapturedPayloadTests
{
    #region Public API
    /// <summary>Rejects missing bytes, truncated payloads, bad digests and incompatible declared extents before upload.</summary>
    [Theory]
    [InlineData("missing")]
    [InlineData("truncated")]
    [InlineData("digest")]
    [InlineData("extent")]
    public void InvalidPayloadCannotBeConsumed(string failure)
    {
        byte[] original = [1, 2, 3, 4];
        var files = new Dictionary<string, byte[]>();
        if (failure != "missing") files["payload.bin"] = failure == "truncated" ? [1, 2] : original;
        var snapshot = new TestAssetSnapshot(files);
        using var manifest = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file = "payload.bin", bytes = failure == "extent" ? 8 : 4,
            sha256 = failure == "digest" ? "incorrect" : Convert.ToHexString(SHA256.HashData(original))
        }));
        if (failure == "missing")
            Assert.Throws<KeyNotFoundException>(() => CapturedPayload.Read(snapshot, manifest.RootElement, 4));
        else
            Assert.ThrowsAny<Xunit.Sdk.XunitException>(() => CapturedPayload.Read(snapshot, manifest.RootElement, 4));
    }

    /// <summary>Preserves validated bytes and nested input identities independently of mutable overrides.</summary>
    [Fact]
    public void SnapshotPreservesIdentityAndIsolatesPayloads()
    {
        byte[] original = [1, 2, 3, 4];
        var snapshot = new TestAssetSnapshot(new Dictionary<string, byte[]>
        {
            ["payload.bin"] = original,
            ["shaders/includes/a/common.glsl"] = Encoding.UTF8.GetBytes("first"),
            ["shaders/includes/b/common.glsl"] = Encoding.UTF8.GetBytes("second")
        });
        using var manifest = JsonDocument.Parse(JsonSerializer.Serialize(new
        {
            file = "payload.bin", bytes = 4, sha256 = Convert.ToHexString(SHA256.HashData(original))
        }));
        original[0] = 99;
        byte[] received = CapturedPayload.Read(snapshot, manifest.RootElement, 4);
        Assert.Equal(new byte[] { 1, 2, 3, 4 }, received);
        received[0] = 88;
        Assert.Equal(1, snapshot.Copy("payload.bin")[0]);
        Assert.Equal("first", snapshot.Text("shaders/includes/a/common.glsl"));
        Assert.Equal("second", snapshot.Text("shaders/includes/b/common.glsl"));
    }
    #endregion
}
