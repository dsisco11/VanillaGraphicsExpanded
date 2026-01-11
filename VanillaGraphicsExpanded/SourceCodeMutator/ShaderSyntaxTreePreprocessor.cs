using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using TinyAst.Preprocessor.Bridge;

using TinyPreprocessor;
using TinyPreprocessor.Core;

using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

#region Shader SyntaxTree Preprocessor

/// <summary>
/// Thin setup wrapper around <see cref="SyntaxTreePreprocessor{TImportNode}"/> for GLSL shader sources.
/// </summary>
public sealed class ShaderSyntaxTreePreprocessor
{
    private static readonly IReadOnlyDictionary<string, object> EmptyMetadata = new Dictionary<string, object>();

    private readonly SyntaxTreePreprocessor<GlImportNode> _preprocessor;

    public ShaderSyntaxTreePreprocessor(IResourceResolver<SyntaxTree> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        _preprocessor = new SyntaxTreePreprocessor<GlImportNode>(
            resolver,
            static node => node.ImportString);
    }

    /// <summary>
    /// Runs preprocessing starting from <paramref name="root"/>.
    /// </summary>
    public ValueTask<PreprocessResult<SyntaxTree>> ProcessAsync(
        ResourceId rootId,
        SyntaxTree root,
        object? context = null,
        PreprocessorOptions? options = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(root);

        var resource = new Resource<SyntaxTree>(rootId, root, EmptyMetadata);
        return _preprocessor.ProcessAsync(resource, context ?? new object(), options ?? PreprocessorOptions.Default, ct);
    }

    /// <summary>
    /// Synchronous convenience wrapper over <see cref="ProcessAsync"/>.
    /// </summary>
    public PreprocessResult<SyntaxTree> Process(
        ResourceId rootId,
        SyntaxTree root,
        object? context = null,
        PreprocessorOptions? options = null,
        CancellationToken ct = default)
    {
        return ProcessAsync(rootId, root, context, options, ct).GetAwaiter().GetResult();
    }
}

#endregion
