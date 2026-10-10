using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Checks captured source, catalogue and implementation bytes at publication boundaries.</summary>
internal sealed class ShaderBuildInputSnapshot
{
    private readonly string assetsRoot, domain, workingDirectory, cataloguePath;
    private readonly ShaderFileHashIndex hashes;
    private readonly ShaderVariantResolver registry;
    private readonly byte[] contracts;
    private readonly Dictionary<string, string> inputs;

    #region Public API
    /// <summary>Captures the same physical inputs used by invocation identities without recalculating projections.</summary>
    internal ShaderBuildInputSnapshot(string assetsRoot, string domain, string workingDirectory, ShaderFileHashIndex hashes, ShaderVariantResolver registry)
    {
        this.assetsRoot = assetsRoot; this.domain = domain; this.workingDirectory = workingDirectory;
        cataloguePath = typeof(TestShaderPrograms).Assembly.Location;
        this.hashes = hashes;
        this.registry = registry;
        contracts = ShaderContractProjection.Membership(registry, "snapshot");
        inputs = InputFiles().ToDictionary(path => path, path => Convert.ToHexString(hashes.GetHash(path)), StringComparer.Ordinal);
    }

    /// <summary>Rejects changed membership or content without substituting declarations for the loaded registry.</summary>
    internal void Validate(IEnumerable<ShaderInputObservation> consumedInputs)
    {
        // Each commit boundary starts a new epoch: strict verification rereads each distinct file,
        // while normal mode deliberately retains the documented metadata-assisted shortcut.
        hashes.BeginVerification();
        if (!contracts.AsSpan().SequenceEqual(ShaderContractProjection.Membership(registry, "snapshot")))
            throw new IOException("Shader contracts changed during processing; rerun the build.");
        if (!inputs.Keys.ToHashSet(StringComparer.Ordinal).SetEquals(InputFiles()))
            throw new IOException("Shader build input membership changed during processing; rerun the build.");
        foreach (var input in inputs)
            if (Convert.ToHexString(hashes.GetHash(input.Key)) != input.Value)
                throw new IOException("Shader build input changed during processing: " + input.Key);
        foreach (var input in consumedInputs)
            if (Convert.ToHexString(hashes.GetHash(input.Path)) != input.Hash)
                throw new IOException("Shader input changed during build: " + input.Path);
    }
    #endregion

    #region Private
    /// <summary>Inventories shader and implementation paths using the same owners as identity capture.</summary>
    private IEnumerable<string> InputFiles() => Directory.EnumerateFiles(Path.Combine(assetsRoot, domain), "*", SearchOption.AllDirectories)
        .Where(path => path.Contains(Path.DirectorySeparatorChar + "shaders" + Path.DirectorySeparatorChar)
            || path.Contains(Path.DirectorySeparatorChar + "shaderincludes" + Path.DirectorySeparatorChar))
        .Concat(ShaderBuildIdentities.ImplementationFiles()).Concat(ShaderBuildIdentities.ReflectionFiles())
        .Concat(ShaderBuildReceipt.CompilerInputs(workingDirectory)).Append(cataloguePath)
        .Select(Path.GetFullPath).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
    #endregion
}
