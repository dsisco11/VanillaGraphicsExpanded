using System.Security.Cryptography;
using System.Text.Json;

namespace VanillaGraphicsExpanded.Tests.Support.Assets;

/// <summary>Validates captured bytes before their native-format decoding or GPU upload.</summary>
internal static class CapturedPayload
{
    #region Internal API
    /// <summary>Requires the declared extent, payload length and digest to agree with the requested native layout.</summary>
    internal static byte[] Read(TestAssetSnapshot inputs, JsonElement record, int expectedBytes)
    {
        // The manifest describes the original capture. Never infer a replacement extent from truncated data.
        Assert.Equal(expectedBytes, record.GetProperty("bytes").GetInt32());
        byte[] bytes = inputs.Copy(record.GetProperty("file").GetString()!);
        Assert.Equal(expectedBytes, bytes.Length);
        Assert.Equal(record.GetProperty("sha256").GetString(), Convert.ToHexString(SHA256.HashData(bytes)));
        return bytes;
    }
    #endregion
}
