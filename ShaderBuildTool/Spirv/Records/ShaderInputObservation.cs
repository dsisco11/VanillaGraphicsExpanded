using System.Security.Cryptography;
using System.Text;

namespace ShaderBuildTool.Spirv;

/// <summary>Associates one opaque resource identity with the bytes actually consumed by preprocessing.</summary>
internal sealed record ShaderInputObservation(string Resource, string Path, long Length, long Modified, long Created, string Hash)
{
    #region Public API
    /// <summary>Reads once, preserving BOM-aware decoding and rejecting observable concurrent edits.</summary>
    internal static (string Text, ShaderInputObservation Input) Read(string resource, string path)
    {
        path = System.IO.Path.GetFullPath(path);
        var info = new FileInfo(path);
        long length = info.Length, modified = info.LastWriteTimeUtc.Ticks, created = info.CreationTimeUtc.Ticks;
        byte[] bytes = File.ReadAllBytes(path);
        info.Refresh();
        if (length != bytes.LongLength || length != info.Length || modified != info.LastWriteTimeUtc.Ticks || created != info.CreationTimeUtc.Ticks)
            throw new IOException("Shader input changed while reading: " + path);
        // Decode exactly the captured bytes rather than reopening a potentially changed file.
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return (reader.ReadToEnd(), new(resource, path, length, modified, created, Convert.ToHexString(SHA256.HashData(bytes))));
    }
    #endregion
}
