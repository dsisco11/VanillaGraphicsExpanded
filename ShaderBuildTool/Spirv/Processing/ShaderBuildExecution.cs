namespace ShaderBuildTool.Spirv;

/// <summary>Shares invocation identities and input hashing with selective processing.</summary>
internal sealed record ShaderBuildExecution(ShaderBuildIdentities Identities, ShaderFileHashIndex Hashes);
