using System;
using System.Collections.Generic;
using System.Linq;

using TinyTokenizer.Ast;

using TinyPreprocessor.Core;

using VanillaGraphicsExpanded.PBR;

namespace VanillaGraphicsExpanded.Tests.Support.GPU.Shaders;

/// <summary>
/// Processes shader source for CPU-only diagnostics using production AST import handling.
/// </summary>
public sealed class ShaderProcessedSourceDiagnostics
{
    #region Public API
    #region Source retrieval
    /// <summary>Uses the immutable build-output snapshot for source diagnostics.</summary>
    public ShaderProcessedSourceDiagnostics() { }

    /// <summary>
    /// Gets the processed shader source with @import directives resolved.
    /// Useful for AST-based uniform extraction and analysis.
    /// </summary>
    /// <param name="filename">Shader filename (e.g., "lumon_gather.fsh").</param>
    /// <returns>Processed source with all imports inlined, or null if file not found.</returns>
    public string? GetProcessedSource(string filename)
    {
        return GetProcessedSource(filename, defines: null);
    }

    /// <summary>
    /// Gets the processed shader source with @import directives resolved and optional defines injected.
    /// Defines are inserted immediately after the first <c>#version</c> directive.
    /// </summary>
    public string? GetProcessedSource(string filename, IReadOnlyDictionary<string, string?>? defines)
    {
        var source = TestAssetSnapshot.Built.TryText("shaders/" + filename);
        if (source is null) return null;
        return BuildProcessedSource(source, defines);
    }

    /// <summary>
    /// Gets the processed shader sources for both vertex and fragment shaders.
    /// </summary>
    /// <param name="vertexFilename">Vertex shader filename.</param>
    /// <param name="fragmentFilename">Fragment shader filename.</param>
    /// <returns>Tuple of (vertexSource, fragmentSource), or (null, null) if either file not found.</returns>
    public (string? VertexSource, string? FragmentSource) GetProcessedSources(
        string vertexFilename, string fragmentFilename)
    {
        var vertexSource = GetProcessedSource(vertexFilename, defines: null);
        var fragmentSource = GetProcessedSource(fragmentFilename, defines: null);
        return (vertexSource, fragmentSource);
    }

    /// <summary>
    /// Gets the processed shader sources for both vertex and fragment shaders with optional defines injected.
    /// </summary>
    public (string? VertexSource, string? FragmentSource) GetProcessedSources(
        string vertexFilename,
        string fragmentFilename,
        IReadOnlyDictionary<string, string?>? defines)
    {
        var vertexSource = GetProcessedSource(vertexFilename, defines);
        var fragmentSource = GetProcessedSource(fragmentFilename, defines);
        return (vertexSource, fragmentSource);
    }
    #endregion
    #endregion

    #region Private
    #region Captured imports
    private Dictionary<string, string>? _importsCache;
    private ShaderSyntaxTreePreprocessor? _preprocessor;

    /// <summary>Gets or builds the imports cache from the immutable include snapshot.</summary>
    private Dictionary<string, string> ImportsCache => _importsCache ??= BuildImportsCache();

    /// <summary>Gets the lazily initialized production preprocessor for captured shader includes.</summary>
    private ShaderSyntaxTreePreprocessor Preprocessor => _preprocessor ??= BuildPreprocessor();

    /// <summary>Copies captured include sources into the resolver's immutable input view.</summary>
    private Dictionary<string, string> BuildImportsCache()
    {
        var cache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Sources are build inputs captured once, not runtime file probes per program.
        foreach (var path in TestAssetSnapshot.Built.Paths.Where(path => path.StartsWith("shaders/includes/", StringComparison.Ordinal)))
            cache[path] = TestAssetSnapshot.Built.Text(path);
        return cache;
    }

    /// <summary>Builds a CPU-only preprocessor over captured shader include sources.</summary>
    private ShaderSyntaxTreePreprocessor BuildPreprocessor()
    {
        var resolver = new DictionarySyntaxTreeResourceResolver(ImportsCache, ShaderImportsSystem.DefaultDomain);
        return new ShaderSyntaxTreePreprocessor(resolver);
    }
    #endregion

    #region Source processing
    /// <summary>Expands imports and applies the same version and define handling used by shader compilation.</summary>
    private string BuildProcessedSource(string source, IReadOnlyDictionary<string, string?>? defines)
    {
        // Process @import directives using the production AST-based system
        var processedSource = ProcessImports(source);

        // Strip non-ASCII characters using the production SIMD-optimized method
        processedSource = SourceCodeImportsProcessor.StripNonAscii(processedSource);

        // Normalize the #version directive:
        // - Some processed sources may end up with #line directives before #version, which is illegal in GLSL.
        // - Compute shaders require the correct higher #version to compile.
        processedSource = NormalizeVersionDirective(processedSource);

        // Inject compile-time defines immediately after #version.
        processedSource = InjectDefinesAfterVersion(processedSource, defines);

        return processedSource;
    }

    /// <summary>Moves an existing version directive to the beginning or supplies the engine-compatible default.</summary>
    private static string NormalizeVersionDirective(string source)
    {
        string trimmed = source.TrimStart();
        if (trimmed.StartsWith("#version", StringComparison.Ordinal))
        {
            return source;
        }

        int versionIndex = source.IndexOf("#version", StringComparison.Ordinal);
        if (versionIndex < 0)
        {
            // Add #version if not present (some VS shaders omit it).
            return "#version 330 core\n" + source;
        }

        // Extract the first #version line and move it to the top.
        int lineEnd = source.IndexOf('\n', versionIndex);
        if (lineEnd < 0)
        {
            // Single-line #version-only source (unlikely); just return it.
            return source.Substring(versionIndex);
        }

        string versionLine = source.Substring(versionIndex, lineEnd - versionIndex).TrimEnd('\r', '\n');
        string before = source.Substring(0, versionIndex);
        string after = source.Substring(lineEnd + 1);

        // Keep the original pre-version content after the version line (helps preserve #line for diagnostics).
        // Ensure we don't accidentally introduce a second #version at the top.
        return versionLine + "\n" + before + after;
    }

    /// <summary>Injects ordered compile-time defines immediately after the normalized version directive.</summary>
    private static string InjectDefinesAfterVersion(
        string source,
        IReadOnlyDictionary<string, string?>? defines)
    {
        if (defines == null || defines.Count == 0)
        {
            return source;
        }

        // Find the first #version line.
        int versionIndex = source.IndexOf("#version", StringComparison.Ordinal);
        if (versionIndex < 0)
        {
            // Should not happen because we add #version above, but fail safe.
            return source;
        }

        int lineEnd = source.IndexOf('\n', versionIndex);
        if (lineEnd < 0)
        {
            // Single-line source; append newline so we can inject.
            lineEnd = source.Length;
            source += "\n";
        }
        else
        {
            lineEnd += 1; // include the newline
        }

        var ordered = defines.OrderBy(kvp => kvp.Key, StringComparer.Ordinal);

        var injected = new System.Text.StringBuilder();
        foreach (var (name, value) in ordered)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;

            // Allow both '#define FOO' and '#define FOO 1.0' styles.
            if (string.IsNullOrWhiteSpace(value))
            {
                injected.Append("#define ").Append(name.Trim()).Append('\n');
            }
            else
            {
                injected.Append("#define ").Append(name.Trim()).Append(' ').Append(value.Trim()).Append('\n');
            }
        }

        if (injected.Length == 0)
        {
            return source;
        }

        return source.Insert(lineEnd, injected.ToString());
    }

    /// <summary>
    /// Processes @import directives in shader source using the production AST-based system.
    /// </summary>
    /// <param name="source">The shader source code.</param>
    /// <returns>Processed source with imports inlined.</returns>
    private string ProcessImports(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);

        var rootId = new ResourceId($"{ShaderImportsSystem.DefaultDomain}:shaders/test");
        var result = Preprocessor.Process(rootId, tree);
        if (!result.Success)
        {
            throw new InvalidOperationException("Shader preprocessing failed in test helper");
        }

        return result.Content.ToText();
    }
    #endregion
    #endregion

}
