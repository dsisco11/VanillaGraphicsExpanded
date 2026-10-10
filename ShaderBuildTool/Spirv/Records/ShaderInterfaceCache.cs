using System.Collections.Concurrent;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Reuses validated portable interfaces independently of compiler execution.</summary>
internal sealed class ShaderInterfaceCache(string outputRoot)
{
    private readonly ShaderRecordStore records = new(outputRoot, "interfaces");
    private readonly ConcurrentDictionary<string, object> locks = new(StringComparer.Ordinal);
    /// <summary>Associates a complete interface payload with binary, contract and extraction implementation.</summary>
    internal sealed record Record(int Version, string BinaryDigest, string Contract, string Extraction, string Configuration, PackagedShaderInterface Payload);

    #region Public API
    /// <summary>Includes all effective extraction inputs independently of output membership.</summary>
    internal static string Key(string binaryDigest, ShaderStageSelection selection, string extraction) =>
        ShaderCacheKey.Create("interface-record-v1", binaryDigest, Contract(selection), extraction, Configuration);

    /// <summary>Shares the owning model's canonical contract projection rather than recreating extractor semantics.</summary>
    internal static string Contract(ShaderStageSelection selection) => ShaderRecordStore.Digest(ShaderContractProjection.Effective(selection));

    /// <summary>Returns verified metadata or extracts only the missing interface from a verified binary.</summary>
    internal PackagedShaderInterface GetOrExtract(byte[] binary, ShaderBinaryDigest.Entry expected, ShaderStageSelection selection,
        string extraction, out bool reused, Action<string>? miss = null,
        Func<byte[], ShaderStageSelection, PackagedShaderInterface>? extract = null)
    {
        if (binary.Length == 0 || ShaderVariantCache.Digest(binary) != expected with { Interface = null })
            throw new InvalidDataException("Interface input binary failed integrity validation.");
        ArgumentException.ThrowIfNullOrWhiteSpace(extraction);
        string key = Key(expected.Digest, selection, extraction);
        lock (locks.GetOrAdd(key, static _ => new()))
        {
            if (records.TryRead<Record>(key, out var record, miss))
            {
                try
                {
                    if (record.Version != 1 || record.BinaryDigest != expected.Digest || record.Contract != Contract(selection)
                        || record.Extraction != extraction || record.Configuration != Configuration)
                        throw new InvalidDataException("Interface record identity/schema mismatch.");
                    Validate(record.Payload, binary, selection);
                    reused = true;
                    return record.Payload;
                }
                catch (InvalidDataException ex) { miss?.Invoke("interface miss: " + ex.Message); }
            }
            // This operation has no compiler dependency. Corrupt metadata repairs locally from checked bytes.
            var payload = (extract ?? ShaderInterfaceExtraction.Extract)(binary, selection);
            Validate(payload, binary, selection);
            records.Store(key, new Record(1, expected.Digest, Contract(selection), extraction, Configuration, payload));
            reused = false;
            return payload;
        }
    }
    #endregion

    #region Private
    private static string Configuration => ShaderCompilerProcess.GenerateDebugInfo ? "Debug" : "Release";

    /// <summary>Applies the same schema and numeric-shape validation as runtime packaging.</summary>
    private static void Validate(PackagedShaderInterface payload, byte[] binary, ShaderStageSelection selection)
    {
        if (payload is null || payload.Extractor != ShaderInterfaceExtraction.ExtractorIdentity || payload.Configuration != Configuration)
            throw new InvalidDataException("Interface extraction identity/configuration mismatch.");
        var entry = ShaderVariantCache.Digest(binary) with { Interface = payload };
        PackagedInterfaceValidation.Resolve(new ShaderBinaryDigest.Manifest(2,
            new Dictionary<string, ShaderBinaryDigest.Entry> { [selection.BinaryPath] = entry }), selection, binary);
    }
    #endregion
}
