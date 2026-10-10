using System.Text;
using System.Text.Json;

namespace ShaderBuildTool.Spirv;

/// <summary>Persists successful per-root snapshots; historical records do not own catalogue outputs.</summary>
internal sealed class ShaderExpandedSourceCache(string outputRoot)
{
    private readonly ShaderRecordStore records = new(outputRoot, "sources");
    private readonly ShaderRecordStore heads = new(outputRoot, "sourceHeads");
    /// <summary>Associates one resolution context with its latest successful immutable record.</summary>
    private sealed record Head(int Version, string Record);
    /// <summary>Contains all successful processing data, protected by the outer payload digest.</summary>
    internal sealed record Record(int Version, string Preprocessing, string TextDigest, ShaderExpandedSource Source);

    #region Public API
    /// <summary>Includes physical resolution context without interpreting resource identifiers as paths.</summary>
    internal static string Key(string assetsRoot, string root, string preprocessing) => ShaderCacheKey.Create("expanded-source-v1", Path.GetFullPath(assetsRoot), root, preprocessing);

    /// <summary>Publishes the complete root snapshot only after validating all provenance.</summary>
    internal string Store(ShaderExpandedSource source, string preprocessing)
    {
        ShaderExpandedSource.Validate(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(preprocessing);
        var record = new Record(1, preprocessing, ShaderRecordStore.Digest(Encoding.UTF8.GetBytes(source.Text)), source);
        string contentKey = ShaderCacheKey.Content(JsonSerializer.SerializeToUtf8Bytes(record));
        records.Store(contentKey, record);
        // A replaced head cannot remove another root's graph; older successful records remain addressable.
        heads.Store(Key(source.AssetsRoot, source.Root, preprocessing), new Head(1, contentKey));
        return contentKey;
    }

    /// <summary>Checks identities, topology, payload and current input content before returning AST-free data.</summary>
    internal bool TryRead(string assetsRoot, string root, string preprocessing, ShaderFileHashIndex hashes,
        out ShaderExpandedSource source, Action<string>? miss = null)
    {
        if (!TryLoadSnapshot(assetsRoot, root, preprocessing, out source, miss)) return false;
        try
        {
            foreach (var input in source.Inputs)
                if (Convert.ToHexString(hashes.GetHash(input.Path)) != input.Hash)
                    throw new InvalidDataException("Shader dependency changed: " + input.Resource);
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException)
        { source = null!; miss?.Invoke("expanded source miss: " + ex.Message); return false; }
    }

    /// <summary>Loads validated historical topology for planning; callers must verify inputs before reuse.</summary>
    internal bool TryLoadSnapshot(string assetsRoot, string root, string preprocessing,
        out ShaderExpandedSource source, Action<string>? miss = null)
    {
        source = null!;
        if (!heads.TryRead<Head>(Key(assetsRoot, root, preprocessing), out var head, miss)) return false;
        if (head.Version != 1 || !records.TryRead<Record>(head.Record, out var record, miss))
        { miss?.Invoke("expanded source head incompatible or incomplete"); return false; }
        try
        {
            if (record.Version != 1 || record.Preprocessing != preprocessing || record.Source is null
                || record.Source.Root != root || record.Source.AssetsRoot != Path.GetFullPath(assetsRoot)
                || ShaderCacheKey.Content(JsonSerializer.SerializeToUtf8Bytes(record)) != head.Record)
                throw new InvalidDataException("Expanded source identity/schema mismatch.");
            ShaderExpandedSource.Validate(record.Source);
            if (ShaderRecordStore.Digest(Encoding.UTF8.GetBytes(record.Source.Text)) != record.TextDigest)
                throw new InvalidDataException("Expanded text digest mismatch.");
            source = record.Source;
            return true;
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException or ArgumentException or UnauthorizedAccessException)
        { miss?.Invoke("expanded source miss: " + ex.Message); return false; }
    }

    #endregion
}
