using System.Collections.Concurrent;
using System.Diagnostics;
using VanillaGraphicsExpanded.Rendering.Contracts;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace ShaderBuildTool.Spirv;

/// <summary>Owns variant record reuse and bounded processing with one compiler job per emitted input key.</summary>
internal sealed class ShaderVariantProcessor(string assetsRoot, string domain, string outputRoot, string workingDirectory,
    string target, bool warningsAsErrors, bool incremental, ShaderBuildIdentities identities,
    ShaderVariantCache compiler, ShaderBuildStatistics statistics, Action<string> miss)
{
    private readonly ShaderVariantRecordCache variants = new(outputRoot);
    private readonly ShaderInterfaceCache interfaces = new(outputRoot);
    private readonly ConcurrentDictionary<string, Lazy<Task<Artifact>>> jobs = new(StringComparer.Ordinal);
    internal readonly ConcurrentDictionary<string, byte[]> Binaries = new(StringComparer.Ordinal);
    internal readonly ConcurrentDictionary<string, ShaderBinaryDigest.Entry> Digests = new(StringComparer.Ordinal);

    /// <summary>Shares the successful compiler result or diagnostics across equivalent emitted variants.</summary>
    private sealed record Artifact(ShaderCompilerResult Result, byte[]? Bytes, ShaderBinaryDigest.Entry? Digest, bool Compiled);

    #region Public API
    /// <summary>Returns work only when a verified variant record cannot satisfy the current selection.</summary>
    internal ShaderCompilationJob? Select(ShaderStageSelection selection, ShaderExpandedSource expanded)
    {
        string input = Path.Combine(outputRoot, "_tmp", selection.Stage.Identity + "." + ShaderVariantIdentifier.Create(selection.Key) + ".glsl");
        var recordInputs = new ShaderVariantRecordInputs(ShaderRecordStore.Digest(System.Text.Encoding.UTF8.GetBytes(expanded.Text)),
            identities.Emission, input, workingDirectory);
        if (incremental && variants.TryRead(recordInputs, selection, compiler, interfaces, identities.Interface, out var cached, miss, Extract))
        {
            statistics.VariantsReused++;
            statistics.CompilerReused++;
            if (cached.InterfaceReused) statistics.InterfacesReused++;
            Accept(selection, cached.Binary, cached.Digest, cached.Interface);
            return null;
        }
        File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
        return new ShaderCompilationJob($"stage '{selection.Stage.Identity}', source '{selection.Stage.Source}', configuration '{selection.Key}'",
            token => ProcessAsync(selection, expanded.Text, recordInputs, token));
    }
    #endregion

    #region Private
    /// <summary>Emits selected variants and shares compiler work without sharing mutable syntax trees.</summary>
    private async Task<ShaderCompilerResult> ProcessAsync(ShaderStageSelection selection, string expanded,
        ShaderVariantRecordInputs inputs, CancellationToken token)
    {
        long start = Stopwatch.GetTimestamp();
        string extension = ShaderCompilerStage.Extension(selection.Stage.Kind);
        string source = ShaderSourceLayout.Apply(new ShaderVariantSource(assetsRoot, domain).Emit(expanded, selection), extension, selection.Stage.Bindings);
        Interlocked.Increment(ref statistics.VariantsEmitted);
        Interlocked.Add(ref statistics.EmissionTicks, Stopwatch.GetTimestamp() - start);
        string key = compiler.Key(source, ShaderCompilerStage.Name(selection.Stage.Kind), selection.Stage.EntryPoint, inputs.InputPath, workingDirectory);
        var candidate = new Lazy<Task<Artifact>>(() => CompileAsync(key, source, selection, inputs.InputPath, token), LazyThreadSafetyMode.ExecutionAndPublication);
        var shared = jobs.GetOrAdd(key, candidate);
        var artifact = await shared.Value;
        if (!artifact.Compiled || !ReferenceEquals(candidate, shared)) Interlocked.Increment(ref statistics.CompilerReused);
        if (artifact.Result.ExitCode != 0) return artifact.Result;
        var payload = interfaces.GetOrExtract(artifact.Bytes!, artifact.Digest!, selection, identities.Interface, out bool reused, miss, Extract);
        if (reused) Interlocked.Increment(ref statistics.InterfacesReused);
        variants.Store(inputs, selection, source, compiler, interfaces, identities.Interface, miss);
        Accept(selection, artifact.Bytes!, artifact.Digest!, payload);
        return artifact.Result;
    }

    /// <summary>Verifies existing artifacts or invokes the compiler once and persists only successful bytes.</summary>
    private async Task<Artifact> CompileAsync(string key, string source, ShaderStageSelection selection, string input, CancellationToken token)
    {
        if (incremental && compiler.TryRead(key, out var cached, out var digest, miss))
            return new(new ShaderCompilerResult(0, "", ""), cached, digest, false);
        string pending = input + ".spv";
        Directory.CreateDirectory(Path.GetDirectoryName(input)!);
        File.Delete(pending);
        try
        {
            File.WriteAllText(input, source);
            token.ThrowIfCancellationRequested();
            long start = Stopwatch.GetTimestamp();
            Interlocked.Increment(ref statistics.CompilerInvocations);
            ShaderCompilerResult result;
            try
            {
                result = await ShaderCompilerProcess.CompileAsync(workingDirectory, input, pending,
                    ShaderCompilerStage.Name(selection.Stage.Kind), target, warningsAsErrors,
                    selection.Stage.EntryPoint, token);
            }
            finally { Interlocked.Add(ref statistics.CompilerTicks, Stopwatch.GetTimestamp() - start); }
            token.ThrowIfCancellationRequested();
            if (result.ExitCode != 0) return new(result, null, null, true);
            byte[] bytes = File.ReadAllBytes(pending);
            var entry = ShaderVariantCache.Digest(bytes);
            // Preserve successful compiler work even if a later interface or different variant fails.
            compiler.Store(key, bytes, entry);
            return new(result, bytes, entry, true);
        }
        finally { File.Delete(pending); }
    }

    /// <summary>Counts actual reflection, including local repair discovered while checking variant records.</summary>
    private PackagedShaderInterface Extract(byte[] bytes, ShaderStageSelection selection)
    {
        File.Delete(Path.Combine(outputRoot, "build-receipt.json"));
        long start = Stopwatch.GetTimestamp();
        Interlocked.Increment(ref statistics.InterfacesExtracted);
        try { return ShaderInterfaceExtraction.Extract(bytes, selection); }
        finally { Interlocked.Add(ref statistics.InterfaceTicks, Stopwatch.GetTimestamp() - start); }
    }

    /// <summary>Associates checked artifacts with exactly the current runtime membership.</summary>
    private void Accept(ShaderStageSelection selection, byte[] bytes, ShaderBinaryDigest.Entry digest, PackagedShaderInterface payload)
    {
        Binaries[selection.BinaryPath] = bytes;
        Digests[selection.BinaryPath] = digest with { Interface = payload };
    }
    #endregion
}
