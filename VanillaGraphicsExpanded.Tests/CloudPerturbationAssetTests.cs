using System.Security.Cryptography;
using VanillaGraphicsExpanded.PBR.Clouds;

namespace VanillaGraphicsExpanded.Tests;

/// <summary>Checks the shipped immutable layout and rejects corruption before GPU admission.</summary>
public sealed class CloudPerturbationAssetTests
{
    #region Public API
    /// <summary>One decoded asset covers identity, bounded channels and complete failure atomicity.</summary>
    [Fact]
    public void PackagedAssetHasExactIdentityAndRejectsMalformedPayloads()
    {
        using var source=typeof(CloudVolumeSettings).Assembly.GetManifestResourceStream("VGE.CloudPerturbation")!;
        using var copy=new MemoryStream();source.CopyTo(copy);byte[] bytes=copy.ToArray();
        Assert.Equal(CloudPerturbationAsset.PayloadBytes+32,bytes.Length);
        Assert.Equal(CloudPerturbationAsset.Digest,Convert.ToHexString(SHA256.HashData(bytes)));
        var asset=new CloudPerturbationAsset(new MemoryStream(bytes));
        Assert.Equal(bytes.AsSpan(32).ToArray(),asset.Pixels);
        Assert.NotEqual(asset.Pixels.Where((_,i)=>i%4==0).ToArray(),asset.Pixels.Where((_,i)=>i%4==1).ToArray());
        foreach(int index in new[]{0,4,8,12,16,20,24,28,32,bytes.Length-1})
        {
            byte[] malformed=(byte[])bytes.Clone();malformed[index]^=1;
            Assert.Throws<InvalidDataException>(()=>new CloudPerturbationAsset(new MemoryStream(malformed)));
        }
        Assert.Throws<InvalidDataException>(()=>new CloudPerturbationAsset(new MemoryStream(bytes[..^1])));
        Assert.Throws<InvalidDataException>(()=>new CloudPerturbationAsset(new MemoryStream([..bytes,0])));
    }
    #endregion
}
