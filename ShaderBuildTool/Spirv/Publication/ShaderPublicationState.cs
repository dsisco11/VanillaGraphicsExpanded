namespace ShaderBuildTool.Spirv;

/// <summary>Identifies the durable state of a journaled shader generation transaction.</summary>
internal enum ShaderPublicationState
{
    // Keep zero undefined so missing JSON state cannot silently become a valid transition.
    Staging = 1,
    Prepared,
    Installed,
    Committed
}
