using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

using TinyTokenizer.Ast;

using TinyPreprocessor;
using TinyPreprocessor.Core;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>
/// Singleton system responsible for loading shader import files from mod assets
/// and inlining @import directives into shader IAsset instances.
/// </summary>
public sealed class ShaderImportsSystem
{
    private const string ModDomain = "vanillagraphicsexpanded";

    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static ShaderImportsSystem Instance { get; } = new();

    /// <summary>
    /// Default domain for domain-less imports.
    /// </summary>
    public const string DefaultDomain = ModDomain;

    private ILogger? _logger;
    private ShaderSyntaxTreePreprocessor? _preprocessor;

    internal ILogger? Logger => _logger;

    // Private constructor for singleton pattern
    private ShaderImportsSystem() { }

    /// <summary>
    /// Initializes the imports system by loading all shader import files from the mod's assets.
    /// Should be called during AssetsLoaded.
    /// </summary>
    /// <param name="api">The core API instance.</param>
    public void Initialize(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Client)
        {
            return;
        }

        _logger = api.Logger;

        var resolver = new AssetSyntaxTreeResourceResolver(api.Assets, DefaultDomain);
        _preprocessor = new ShaderSyntaxTreePreprocessor(resolver);
    }

    /// <summary>
    /// Clears the imports cache. Should be called on mod dispose.
    /// </summary>
    public void Clear()
    {
        _logger = null;
        _preprocessor = null;
    }

    /// <summary>
    /// Creates a SyntaxTree for the given asset using the GLSL schema.
    /// Use this when you need to apply patches to shader source.
    /// </summary>
    /// <param name="asset">The shader asset to process.</param>
    /// <returns>A SyntaxTree instance, or null if the asset is empty.</returns>
    public SyntaxTree? CreateSyntaxTree(IAsset asset)
    {
        if (asset.Data is null || asset.Data.Length == 0)
        {
            return null;
        }

        var sourceCode = asset.ToText();
        return SyntaxTree.Parse(sourceCode, GlslSchema.Instance);
    }

    /// <summary>
    /// Creates a SyntaxTree for the given source code using the GLSL schema.
    /// </summary>
    /// <param name="sourceCode">The shader source code.</param>
    /// <param name="sourceName">Optional source name for error messages.</param>
    /// <returns>A SyntaxTree instance, or null if the source is empty.</returns>
    public SyntaxTree? CreateSyntaxTree(string sourceCode, string sourceName)
    {
        if (string.IsNullOrEmpty(sourceCode))
        {
            return null;
        }
        return SyntaxTree.Parse(sourceCode, GlslSchema.Instance);
    }

    /// <summary>
    /// Expands all <c>@import</c> directives using <see cref="TinyAst.Preprocessor.Bridge.SyntaxTreePreprocessor{TImportNode}"/>
    /// and the Vintage Story asset system.
    /// </summary>
    /// <param name="tree">The SyntaxTree to process.</param>
    /// <param name="sourceName">The source name for logging.</param>
    /// <param name="log">Optional logger for warnings/errors.</param>
    /// <param name="outputTree">The processed tree (same as input if no imports were present).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>True if preprocessing was performed (imports present), false otherwise.</returns>
    /// <exception cref="InvalidOperationException">Thrown when preprocessing fails (e.g., missing import).</exception>
    public bool TryPreprocessImports(
        SyntaxTree tree,
        string sourceName,
        out SyntaxTree outputTree,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        outputTree = tree;

        if (_preprocessor is null)
        {
            return false;
        }

        bool hasImports = tree.Select(Query.Syntax<GlImportNode>()).Any();
        if (!hasImports)
        {
            return false;
        }

        var rootId = new ResourceId($"{DefaultDomain}:shaders/{sourceName}");
        var result = _preprocessor.Process(rootId, tree, context: null, options: null, ct);

        if (!result.Success)
        {
            string diagText = string.Join("\n", result.Diagnostics.Select(static d => d.ToString()));
            (log ?? _logger)?.Error($"[VGE] Shader import preprocessing failed for '{sourceName}':\n{diagText}");
            throw new InvalidOperationException($"Shader import preprocessing failed for '{sourceName}'");
        }

        outputTree = result.Content;
        return true;
    }

    /// <summary>
    /// Runs import preprocessing and returns the raw preprocessor result (diagnostics, source map, etc.).
    /// Intended for higher-level helpers such as <see cref="GlslPreprocessor"/>.
    /// </summary>
    /// <remarks>
    /// Only valid after <see cref="Initialize"/> has been called on the client.
    /// </remarks>
    internal PreprocessResult<SyntaxTree> ProcessImports(SyntaxTree tree, string sourceName, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        if (_preprocessor is null)
        {
            throw new InvalidOperationException("ShaderImportsSystem was not initialized (preprocessor unavailable)");
        }

        var rootId = new ResourceId($"{DefaultDomain}:shaders/{sourceName}");
        return _preprocessor.Process(rootId, tree, context: null, options: null, ct);
    }
}

