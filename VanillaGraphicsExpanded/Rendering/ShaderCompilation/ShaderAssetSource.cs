using System;
using VanillaGraphicsExpanded.Rendering.Spirv;

namespace VanillaGraphicsExpanded.Rendering.ShaderCompilation;

/// <summary>Supplies binary reads and the shared digest service for one asset source and domain.</summary>
internal sealed class ShaderAssetSource(object identity, string domain, ShaderAssetReader read,
    Func<ShaderBinaryDigest.Manifest?> digest, Action<string>? warning = null, Action<string>? error = null)
{
    internal object Identity { get; } = identity;
    internal string Domain { get; } = domain;
    internal ShaderAssetReader Read { get; } = read;
    internal Func<ShaderBinaryDigest.Manifest?> Digest { get; } = digest;
    internal Action<string>? Warning { get; } = warning;
    internal Action<string>? Error { get; } = error;
}
