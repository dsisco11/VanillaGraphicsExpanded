using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using TinyPreprocessor;
using TinyPreprocessor.Core;
using TinyTokenizer.Ast;

using VanillaGraphicsExpanded.PBR;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Loads and prepares GLSL stage source code from Vintage Story assets.
/// 
/// Pipeline:
/// - Load stage file via AssetManager
/// - Parse into a <see cref="SyntaxTree"/>
/// - Inject defines into the tree (after <c>#version</c>)
/// - Inline <c>@import</c> directives using <see cref="GlslPreprocessor"/>
/// - Emit final GLSL source (optionally stripping non-ASCII characters)
/// 
/// NOTE: If any post-string transforms are applied after emitting GLSL (e.g. <see cref="SourceCodeImportsProcessor.StripNonAscii"/>),
/// the offsets in the preprocessor source map may no longer align with the final GLSL string.
/// </summary>
public sealed class ShaderSourceCode
{
    public required string ShaderName { get; init; }
    public required string StageExtension { get; init; }
    public required string AssetDomain { get; init; }

    public string StageSourceName => $"{ShaderName}.{StageExtension}";
    public string AssetPath => $"shaders/{StageSourceName}";

    public required string RawSource { get; init; }
    public required SyntaxTree ParsedTree { get; init; }

    public required IReadOnlyDictionary<string, string?> Defines { get; init; }

    public required GlslPreprocessor.Result ImportInlining { get; init; }

    public SyntaxTree FinalTree => ImportInlining.OutputTree;

    /// <summary>
    /// Raw preprocessor result (diagnostics + source map) when imports were present; otherwise null.
    /// </summary>
    public PreprocessResult<SyntaxTree>? ImportResult => ImportInlining.RawResult;

    /// <summary>
    /// Final emitted GLSL (before any non-ASCII stripping).
    /// </summary>
    public required string EmittedSourceUnstripped { get; init; }

    /// <summary>
    /// Final emitted GLSL, suitable for passing to the engine/GL.
    /// </summary>
    public required string EmittedSource { get; init; }

    public static ShaderSourceCode Load(
        ICoreAPI api,
        string shaderName,
        string stageExtension,
        IReadOnlyDictionary<string, string?>? defines = null,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(api);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);

        string domain = ShaderImportsSystem.DefaultDomain;
        string stageSourceName = $"{shaderName}.{stageExtension}";
        string assetPath = $"shaders/{stageSourceName}";

        var loc = AssetLocation.Create(assetPath, domain);
        IAsset? asset = api.Assets.TryGet(loc, loadAsset: true);
        if (asset is null)
        {
            throw new InvalidOperationException($"Shader stage asset not found: {domain}:{assetPath}");
        }

        ct.ThrowIfCancellationRequested();

        string raw = asset.ToText();
        if (string.IsNullOrEmpty(raw))
        {
            throw new InvalidOperationException($"Shader stage asset was empty: {domain}:{assetPath}");
        }

        var parsed = ShaderImportsSystem.Instance.CreateSyntaxTree(raw, stageSourceName)
            ?? throw new InvalidOperationException($"Failed to parse GLSL for '{stageSourceName}'");

        IReadOnlyDictionary<string, string?> defineMap = defines is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(defines);
        if (defineMap.Count > 0)
        {
            InjectDefinesAfterVersion(parsed, defineMap);
        }

        var preprocess = GlslPreprocessor.InlineImports(parsed, stageSourceName, log, ct);

        string emittedUnstripped = preprocess.OutputTree.ToText();
        string emitted = SourceCodeImportsProcessor.StripNonAscii(emittedUnstripped);

        return new ShaderSourceCode
        {
            ShaderName = shaderName,
            StageExtension = stageExtension,
            AssetDomain = domain,
            RawSource = raw,
            ParsedTree = parsed,
            Defines = defineMap,
            ImportInlining = preprocess,
            EmittedSourceUnstripped = emittedUnstripped,
            EmittedSource = emitted
        };
    }

    /// <summary>
    /// Creates a <see cref="ShaderSourceCode"/> instance from raw shader text.
    /// Intended for unit tests and tooling where an <see cref="ICoreAPI"/> asset manager is not available.
    /// </summary>
    /// <remarks>
    /// The caller supplies a <see cref="ShaderSyntaxTreePreprocessor"/> to resolve <c>@import</c> directives.
    /// VGE's production path uses an AssetManager-backed preprocessor via <see cref="ShaderImportsSystem"/>.
    /// </remarks>
    public static ShaderSourceCode FromSource(
        string shaderName,
        string stageExtension,
        string rawSource,
        string sourceName,
        ShaderSyntaxTreePreprocessor importPreprocessor,
        IReadOnlyDictionary<string, string?>? defines = null,
        ILogger? log = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(stageExtension);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceName);
        ArgumentNullException.ThrowIfNull(importPreprocessor);

        if (string.IsNullOrEmpty(rawSource))
        {
            throw new InvalidOperationException($"Shader stage source was empty: {sourceName}");
        }

        var parsed = ShaderImportsSystem.Instance.CreateSyntaxTree(rawSource, sourceName)
            ?? throw new InvalidOperationException($"Failed to parse GLSL for '{sourceName}'");

        ct.ThrowIfCancellationRequested();

        IReadOnlyDictionary<string, string?> defineMap = defines is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(defines);

        if (defineMap.Count > 0)
        {
            InjectDefinesAfterVersion(parsed, defineMap);
        }

        bool hadImports = parsed.Select(Query.Syntax<GlImportNode>()).Any();

        PreprocessResult<SyntaxTree>? rawResult = null;
        SyntaxTree outputTree = parsed;
        string[] diagnostics = Array.Empty<string>();

        if (hadImports)
        {
            var rootId = new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/{sourceName}");
            rawResult = importPreprocessor.Process(rootId, parsed, context: null, options: null, ct);
            if (!rawResult.Success)
            {
                string diagText = string.Join("\n", rawResult.Diagnostics.Select(static d => d.ToString() ?? string.Empty));
                log?.Error($"[VGE] GLSL preprocessing failed for '{sourceName}':\n{diagText}");
                throw new InvalidOperationException($"GLSL preprocessing failed for '{sourceName}'");
            }

            outputTree = rawResult.Content;
            diagnostics = rawResult.Diagnostics.Select(static d => d.ToString() ?? string.Empty).ToArray();
        }

        var preprocess = new GlslPreprocessor.Result
        {
            SourceName = sourceName,
            Tree = parsed,
            HadImports = hadImports,
            Success = true,
            OutputTree = outputTree,
            Diagnostics = diagnostics,
            RawResult = rawResult
        };

        string emittedUnstripped = preprocess.OutputTree.ToText();
        string emitted = SourceCodeImportsProcessor.StripNonAscii(emittedUnstripped);

        return new ShaderSourceCode
        {
            ShaderName = shaderName,
            StageExtension = stageExtension,
            AssetDomain = ShaderImportsSystem.DefaultDomain,
            RawSource = rawSource,
            ParsedTree = parsed,
            Defines = defineMap,
            ImportInlining = preprocess,
            EmittedSourceUnstripped = emittedUnstripped,
            EmittedSource = emitted
        };
    }

    private static void InjectDefinesAfterVersion(SyntaxTree tree, IReadOnlyDictionary<string, string?> defines)
    {
        ArgumentNullException.ThrowIfNull(tree);
        ArgumentNullException.ThrowIfNull(defines);

        string defineBlock = BuildDefineBlock(defines);
        if (string.IsNullOrEmpty(defineBlock))
        {
            return;
        }

        // Define injection must be placed after the #version directive.
        var versionQuery = Query.Syntax<GlDirectiveNode>().Named("version");
        bool hasVersion = tree.Select(versionQuery).Any();

        if (!hasVersion)
        {
            throw new InvalidOperationException("Shader source did not contain a #version directive; cannot safely inject defines");
        }

        tree.CreateEditor()
            .InsertAfter(versionQuery, defineBlock)
            .Commit();
    }

    private static string BuildDefineBlock(IReadOnlyDictionary<string, string?> defines)
    {
        if (defines.Count == 0)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        builder.Append("\n// VGE: shader defines\n");

        foreach (var (name, value) in defines.OrderBy(kvp => kvp.Key, StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            builder.Append("#define ");
            builder.Append(name.Trim());

            if (!string.IsNullOrWhiteSpace(value))
            {
                builder.Append(' ');
                builder.Append(value.Trim());
            }

            builder.Append('\n');
        }

        builder.Append('\n');
        return builder.ToString();
    }
}
