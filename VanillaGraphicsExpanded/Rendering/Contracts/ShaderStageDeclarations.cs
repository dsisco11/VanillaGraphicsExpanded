using System;
using System.Collections.Concurrent;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Interns compatible immutable stages within an explicit catalog scope.</summary>
internal static class ShaderStageDeclarations
{
    private static readonly ConcurrentDictionary<(string Scope, string Identity), ShaderStageContract> stages = new();

    #region Shared declaration ownership
    /// <summary>Returns one stage instance and rejects conflicting definitions independently of initialization order.</summary>
    public static ShaderStageContract Share(ShaderStageContract declaration, string scope = "production")
    {
        var shared = stages.GetOrAdd((scope, declaration.Identity), declaration);
        if (!shared.Equivalent(declaration)) throw new ArgumentException($"Conflicting shared stage '{declaration.Identity}' in scope '{scope}'.");
        return shared;
    }
    #endregion
}
