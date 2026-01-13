using System;
using System.Linq;
using System.Threading;

using TinyPreprocessor;
using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Public helper for preprocessing GLSL sources.
/// 
/// Responsibilities:
/// - Parse GLSL source into a <see cref="SyntaxTree"/> (GLSL schema)
/// - Inline <c>@import</c> directives via the VGE import system (AssetManager-backed)
/// - Return diagnostics and (if available) a source map from the underlying preprocessor
/// 
/// NOTE: This does not apply any vanilla shader patch injection logic; that remains opt-in elsewhere.
/// </summary>
public static class GlslPreprocessor
{
    public sealed class Result
    {
        public required string SourceName { get; init; }
        public required SyntaxTree Tree { get; init; }
        public required bool HadImports { get; init; }
        public required bool Success { get; init; }
        public required SyntaxTree OutputTree { get; init; }
        public required string[] Diagnostics { get; init; }
        public required PreprocessResult<SyntaxTree>? RawResult { get; init; }

        public override string ToString() => $"{SourceName} (imports={HadImports}, success={Success})";
    }

    /// <summary>
    /// Inlines <c>@import</c> directives for an already-parsed GLSL <see cref="SyntaxTree"/>.
    /// Use this when you have already mutated the tree (e.g., patching) and want to preserve those edits.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if preprocessing fails.</exception>
    public static Result InlineImports(
        SyntaxTree tree,
        string sourceName,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        bool hadImports = tree.Select(Query.Syntax<GlImportNode>()).Any();
        if (!hadImports)
        {
            return new Result
            {
                SourceName = sourceName,
                Tree = tree,
                HadImports = false,
                Success = true,
                OutputTree = tree,
                Diagnostics = Array.Empty<string>(),
                RawResult = null
            };
        }

        var raw = ShaderImportsSystem.Instance.ProcessImports(tree, sourceName, ct);
        if (!raw.Success)
        {
            string diagText = string.Join("\n", raw.Diagnostics.Select(static d => d.ToString() ?? string.Empty));
            (log ?? ShaderImportsSystem.Instance.Logger)?.Error($"[VGE] GLSL preprocessing failed for '{sourceName}':\n{diagText}");
            throw new InvalidOperationException($"GLSL preprocessing failed for '{sourceName}'");
        }

        return new Result
        {
            SourceName = sourceName,
            Tree = tree,
            HadImports = true,
            Success = true,
            OutputTree = raw.Content,
            Diagnostics = raw.Diagnostics.Select(static d => d.ToString() ?? string.Empty).ToArray(),
            RawResult = raw
        };
    }

    /// <summary>
    /// Parses GLSL and inlines <c>@import</c> directives.
    /// </summary>
    /// <exception cref="InvalidOperationException">Thrown if preprocessing fails.</exception>
    public static Result ParseAndInlineImports(
        string sourceCode,
        string sourceName,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);

        var tree = ShaderImportsSystem.Instance.CreateSyntaxTree(sourceCode, sourceName)
            ?? throw new InvalidOperationException($"Failed to parse GLSL for '{sourceName}' (empty source?)");

        bool hadImports = tree.Select(Query.Syntax<GlImportNode>()).Any();
        if (!hadImports)
        {
            return new Result
            {
                SourceName = sourceName,
                Tree = tree,
                HadImports = false,
                Success = true,
                OutputTree = tree,
                Diagnostics = Array.Empty<string>(),
                RawResult = null
            };
        }

        var raw = ShaderImportsSystem.Instance.ProcessImports(tree, sourceName, ct);
        if (!raw.Success)
        {
            string diagText = string.Join("\n", raw.Diagnostics.Select(static d => d.ToString() ?? string.Empty));
            (log ?? ShaderImportsSystem.Instance.Logger)?.Error($"[VGE] GLSL preprocessing failed for '{sourceName}':\n{diagText}");
            throw new InvalidOperationException($"GLSL preprocessing failed for '{sourceName}'");
        }

        return new Result
        {
            SourceName = sourceName,
            Tree = tree,
            HadImports = true,
            Success = true,
            OutputTree = raw.Content,
            Diagnostics = raw.Diagnostics.Select(static d => d.ToString() ?? string.Empty).ToArray(),
            RawResult = raw
        };
    }
}
