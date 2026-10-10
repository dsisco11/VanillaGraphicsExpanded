using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Identifies the inputs needed to reuse emission independently of current output membership.</summary>
internal sealed record ShaderVariantRecordInputs(string ExpandedDigest, string Emission, string InputPath, string WorkingDirectory);

/// <summary>Returns checked binary and metadata without retaining mutable preprocessing objects.</summary>
internal sealed record ShaderCachedVariant(byte[] Binary, ShaderBinaryDigest.Entry Digest, PackagedShaderInterface Interface, bool InterfaceReused);

/// <summary>Associates emission results with the existing compiler cache and independent interface records.</summary>
internal sealed class ShaderVariantRecordCache(string outputRoot)
{
    private readonly ShaderRecordStore records = new(outputRoot, "variants");
    /// <summary>Persists complete emission provenance and digest-only compiler/interface references.</summary>
    internal sealed record Record(int Version, string ExpandedDigest, string Contract, string Emission, string CompilerIdentity,
        string EmittedText, string EmittedDigest, string CompilerKey, ShaderBinaryDigest.Entry Binary, string InterfaceKey, string Extraction);

    #region Public API
    /// <summary>Keys emission reuse by effective inputs while keeping extractor changes out of compiler eligibility.</summary>
    internal static string Key(ShaderVariantRecordInputs inputs, ShaderStageSelection selection, ShaderVariantCache compiler) =>
        ShaderCacheKey.Create("variant-record-v1", inputs.ExpandedDigest, ShaderInterfaceCache.Contract(selection), inputs.Emission,
            compiler.CompilerIdentity, ShaderCompilerProcess.GenerateDebugInfo ? Path.GetFullPath(inputs.InputPath) : "",
            ShaderCompilerProcess.GenerateDebugInfo ? Path.GetFullPath(inputs.WorkingDirectory) : "");

    /// <summary>Records only a verified compiler artifact and validated packaged interface.</summary>
    internal void Store(ShaderVariantRecordInputs inputs, ShaderStageSelection selection, string emittedText,
        ShaderVariantCache compiler, ShaderInterfaceCache interfaces, string extraction, Action<string>? miss = null)
    {
        ValidateInputs(inputs);
        string compilerKey = CompilerKey(inputs, selection, emittedText, compiler);
        if (!compiler.TryRead(compilerKey, out var binary, out var digest, miss))
            throw new InvalidDataException("Variant record requires an existing verified compiler result.");
        interfaces.GetOrExtract(binary, digest, selection, extraction, out _, miss);
        records.Store(Key(inputs, selection, compiler), new Record(1, inputs.ExpandedDigest, ShaderInterfaceCache.Contract(selection),
            inputs.Emission, compiler.CompilerIdentity, emittedText, ShaderRecordStore.Digest(Encoding.UTF8.GetBytes(emittedText)),
            compilerKey, digest, ShaderInterfaceCache.Key(digest.Digest, selection, extraction), extraction));
    }

    /// <summary>Reuses emission after verifying every association; missing interfaces repair without compiling.</summary>
    internal bool TryRead(ShaderVariantRecordInputs inputs, ShaderStageSelection selection, ShaderVariantCache compiler,
        ShaderInterfaceCache interfaces, string extraction, out ShaderCachedVariant variant, Action<string>? miss = null,
        Func<byte[], ShaderStageSelection, PackagedShaderInterface>? extract = null)
    {
        variant = null!;
        ValidateInputs(inputs);
        if (!records.TryRead<Record>(Key(inputs, selection, compiler), out var record, miss)) return false;
        try
        {
            if (record.Version != 1 || record.ExpandedDigest != inputs.ExpandedDigest || record.Contract != ShaderInterfaceCache.Contract(selection)
                || record.Emission != inputs.Emission || record.CompilerIdentity != compiler.CompilerIdentity || record.EmittedText is null
                || record.EmittedDigest != ShaderRecordStore.Digest(Encoding.UTF8.GetBytes(record.EmittedText))
                || !ShaderCacheKey.IsValid(record.CompilerKey) || record.CompilerKey != CompilerKey(inputs, selection, record.EmittedText, compiler)
                || record.Binary is null || record.Binary.Interface is not null || !ShaderRecordStore.IsDigest(record.Binary.Digest)
                || string.IsNullOrWhiteSpace(record.Extraction)
                || record.InterfaceKey != ShaderInterfaceCache.Key(record.Binary.Digest, selection, record.Extraction))
                throw new InvalidDataException("Variant record schema, identity or artifact association mismatch.");
            if (!compiler.TryRead(record.CompilerKey, out var bytes, out var digest, miss)) return false;
            if (digest != record.Binary) throw new InvalidDataException("Variant binary association mismatch.");
            var payload = interfaces.GetOrExtract(bytes, digest, selection, extraction, out bool reused, miss, extract);
            variant = new(bytes, digest, payload, reused);
            return true;
        }
        catch (InvalidDataException ex) { miss?.Invoke("variant miss: " + ex.Message); return false; }
    }
    #endregion

    #region Private
    /// <summary>Rejects incomplete caller identities before looking up records.</summary>
    private static void ValidateInputs(ShaderVariantRecordInputs inputs)
    {
        if (!ShaderRecordStore.IsDigest(inputs.ExpandedDigest) || string.IsNullOrWhiteSpace(inputs.Emission)
            || string.IsNullOrWhiteSpace(inputs.InputPath) || string.IsNullOrWhiteSpace(inputs.WorkingDirectory))
            throw new ArgumentException("Variant reuse inputs are incomplete.", nameof(inputs));
    }

    /// <summary>Derives the artifact reference from the same compiler input policy used by normal builds.</summary>
    private static string CompilerKey(ShaderVariantRecordInputs inputs, ShaderStageSelection selection, string source, ShaderVariantCache compiler) =>
        compiler.Key(source, ShaderCompilerStage.Name(selection.Stage.Kind), selection.Stage.EntryPoint,
            inputs.InputPath, inputs.WorkingDirectory);
    #endregion
}
