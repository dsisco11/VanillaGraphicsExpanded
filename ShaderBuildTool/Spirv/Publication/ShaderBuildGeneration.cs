using System.Collections.Immutable;

namespace ShaderBuildTool.Spirv;

/// <summary>Holds complete compiler outputs and the actual source observations consumed by this invocation.</summary>
internal sealed record ShaderBuildGeneration(IReadOnlyDictionary<string, byte[]> Binaries, byte[] Manifest, ImmutableArray<ShaderInputObservation> Inputs)
{
    #region Public API
    /// <summary>Rejects changed consumed files before installation or receipt publication using the selected hash policy.</summary>
    internal void ValidateInputs(ShaderFileHashIndex hashes)
    {
        foreach (var input in Inputs)
            if (Convert.ToHexString(hashes.GetHash(input.Path)) != input.Hash)
                throw new IOException("Shader input changed during build: " + input.Path);
    }
    #endregion
}
