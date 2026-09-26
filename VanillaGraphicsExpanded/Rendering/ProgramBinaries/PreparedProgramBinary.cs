using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.ProgramBinaries;

/// <summary>Owns captured stage bytes and their build-generated cache identity.</summary>
internal sealed class PreparedProgramBinary
{
    private readonly Dictionary<string, byte[]> binaries = new(StringComparer.Ordinal);
    internal string? Key { get; }
    internal ProgramBinaryStore? Store { get; }

    /// <summary>Captures stage assets once and consumes the digests packaged by the same successful build.</summary>
    internal PreparedProgramBinary(ShaderLoadPlan plan, ShaderAssetReader read, ProgramBinaryStore? store, string driver,
        Func<ShaderBinaryDigest.Manifest?> digestIndex)
    {
        Store = store;
        foreach (var selection in plan.Stages)
            if (!binaries.ContainsKey(selection.BinaryPath)) binaries.Add(selection.BinaryPath, read(selection.BinaryPath).ToArray());
        if (store == null) return;
        ShaderBinaryDigest.Manifest? manifest;
        try { manifest = digestIndex(); }
        catch (Exception) { Store = null; return; }
        var digests = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        // Missing metadata disables caching, not shader loading. Never substitute a runtime stage hash.
        foreach (var pair in binaries)
        {
            try
            {
                if (!ShaderBinaryDigest.TryRead(manifest, pair.Key, pair.Value.Length, out var digest))
                { Store = null; return; }
                digests.Add(pair.Key, digest);
            }
            catch (Exception) { Store = null; return; }
        }
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, true);
        writer.Write(ProgramBinaryStore.Schema);
        writer.Write(driver);
        // Both owners link monolithic executables with no API-assigned attributes, outputs or transform feedback.
        // Add every future pre-link setting here before enabling it on a cached path.
        writer.Write("monolithic;shader-explicit-locations;no-transform-feedback;v1");
        writer.Write(plan.Stages.Count);
        foreach (var selection in plan.Stages)
        {
            var bytes = binaries[selection.BinaryPath];
            writer.Write((int)selection.Stage.Kind);
            writer.Write(selection.Stage.EntryPoint);
            writer.Write(bytes.Length); writer.Write(digests[selection.BinaryPath]);
            writer.Write(selection.Specializations.Count);
            foreach (var value in selection.Specializations.OrderBy(value => value.Id))
            {
                writer.Write(value.Id); writer.Write(value.Value.Bits);
            }
        }
        writer.Flush();
        if (store != null) Key = Convert.ToHexString(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    /// <summary>Borrows the captured bytes for synchronous specialization without rereading assets.</summary>
    internal ReadOnlySpan<byte> Read(string path) => binaries[path];
}
