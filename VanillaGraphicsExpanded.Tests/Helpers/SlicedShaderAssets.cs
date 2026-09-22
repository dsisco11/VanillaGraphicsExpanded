namespace VanillaGraphicsExpanded.Tests.Helpers;

/// <summary>Serves shader assets from bounded slices and records their complete backing buffers.</summary>
internal sealed class SlicedShaderAssets(Func<string, byte[]> read)
{
    private readonly List<(byte[] Buffer, byte[] Original)> buffers = [];

    #region Asset access and verification
    /// <summary>Surrounds each asset with invalid bytes so consumers must respect both slice boundaries.</summary>
    public ReadOnlySpan<byte> Read(string path)
    {
        byte[] content = read(path);
        byte[] buffer = new byte[content.Length + 14];
        buffer.AsSpan().Fill(0xff);
        content.CopyTo(buffer.AsSpan(7));
        buffers.Add((buffer, (byte[])buffer.Clone()));
        return buffer.AsSpan(7, content.Length);
    }

    /// <summary>Checks payloads and guard bytes for accidental writes through the loading path.</summary>
    public void AssertUnchanged()
    {
        foreach (var (buffer, original) in buffers) Assert.Equal(original, buffer);
    }
    #endregion
}
