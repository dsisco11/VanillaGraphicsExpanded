namespace ShaderBuildTool.Spirv;

/// <summary>Shares invocation identities and input hashing with selective processing and receipt validation.</summary>
internal sealed record ShaderBuildExecution(ShaderBuildIdentities Identities, ShaderFileHashIndex Hashes,
    Func<ShaderBuildGeneration, bool>? TryReceipt = null);
