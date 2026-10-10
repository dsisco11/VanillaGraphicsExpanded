using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Checks captured source, catalogue and implementation identities at publication boundaries.</summary>
internal sealed class ShaderBuildInputSnapshot
{
    private readonly string assetsRoot, domain, workingDirectory, target, scope, fingerprint, cataloguePath, catalogueHash;
    private readonly bool warnings, verifyContents;
    private readonly ShaderVariantResolver registry;

    #region Public API
    /// <summary>Captures catalogue file content alongside the already resolved input fingerprint and registry.</summary>
    internal ShaderBuildInputSnapshot(string assetsRoot, string domain, string workingDirectory, string target, bool warnings,
        ShaderVariantResolver registry, string scope, string fingerprint, bool verifyContents)
    {
        this.assetsRoot = assetsRoot; this.domain = domain; this.workingDirectory = workingDirectory;
        this.target = target; this.warnings = warnings; this.registry = registry; this.scope = scope;
        this.fingerprint = fingerprint; this.verifyContents = verifyContents;
        cataloguePath = typeof(TestShaderPrograms).Assembly.Location;
        catalogueHash = ShaderRecordStore.Digest(File.ReadAllBytes(cataloguePath));
    }

    /// <summary>Rejects changes without substituting new on-disk declarations for the registry used to compile.</summary>
    internal void Validate(string outputRoot, ShaderBuildGeneration generation)
    {
        var verification = new ShaderFileHashIndex(outputRoot, verifyContents);
        generation.ValidateInputs(verification);
        var current = ShaderBuildIdentities.Capture(workingDirectory, target, warnings, verification);
        string currentFingerprint = ShaderBuildReceipt.Fingerprint(assetsRoot, domain, current.Receipt(registry, scope), verification);
        if (currentFingerprint != fingerprint || ShaderRecordStore.Digest(File.ReadAllBytes(cataloguePath)) != catalogueHash)
            throw new IOException("Shader build inputs changed during processing; rerun the build.");
    }
    #endregion
}
