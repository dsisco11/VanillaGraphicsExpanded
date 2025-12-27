using System;
using System.Collections.Generic;
using System.Text;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

/// <summary>
/// A specialized <see cref="SourceCodePatcher"/> for processing <c>#import</c> directives in shader source code.
/// </summary>
/// <remarks>
/// This processor finds all <c>#import</c> directives and replaces them with the referenced file contents,
/// commenting out the original directive for debugging purposes.
/// </remarks>
/// <example>
/// <code>
/// // Instance usage with chaining
/// var patcher = new SourceCodeImportsProcessor(shaderCode, importsCache, "myshader.fsh");
/// patcher.ProcessImports(logger)
///     .FindFunction("main").BeforeClose().Insert("// additional code")
///     .Build();
/// 
/// // Static convenience method for one-off processing
/// var processed = SourceCodeImportsProcessor.Process(shaderCode, importsCache, "myshader.fsh", logger);
/// </code>
/// </example>
public class SourceCodeImportsProcessor : SourceCodePatcher
{
    #region Nested Types

    /// <summary>
    /// Represents a found #import directive in shader source.
    /// </summary>
    private readonly record struct ImportDirective(long DirectiveStart, long LineEnd, string FileName);

    #endregion

    #region Fields

    private readonly IReadOnlyDictionary<string, string> _importsCache;

    #endregion

    #region Constructors

    /// <summary>
    /// Creates a new import processor for the given source code.
    /// </summary>
    /// <param name="source">The source code to process.</param>
    /// <param name="importsCache">Dictionary mapping import file names to their contents.</param>
    /// <param name="sourceName">Optional source name for error messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> or <paramref name="importsCache"/> is null.</exception>
    public SourceCodeImportsProcessor(string source, IReadOnlyDictionary<string, string> importsCache, string? sourceName = null)
        : base(source, sourceName)
    {
        _importsCache = importsCache ?? throw new ArgumentNullException(nameof(importsCache));
    }

    #endregion

    #region Static Methods

    /// <summary>
    /// Processes a shader source code string to resolve all <c>#import</c> directives.
    /// This is a convenience method for one-off import processing without additional patching.
    /// </summary>
    /// <param name="source">The source code to process.</param>
    /// <param name="importsCache">Dictionary mapping import file names to their contents.</param>
    /// <param name="sourceName">Optional source name for error messages.</param>
    /// <param name="logger">Optional logger for audit messages.</param>
    /// <returns>The processed source code with imports resolved.</returns>
    /// <exception cref="SourceCodePatchException">Thrown if an imported file is not found in the cache.</exception>
    public static string Process(
        string source,
        IReadOnlyDictionary<string, string> importsCache,
        string? sourceName = null,
        ILogger? logger = null)
    {
        return new SourceCodeImportsProcessor(source, importsCache, sourceName)
            .ProcessImports(logger)
            .Build();
    }

    #endregion

    #region Instance Methods

    /// <summary>
    /// Finds all <c>#import</c> directives and queues insertions to comment them out
    /// and inject the referenced file contents below each one.
    /// </summary>
    /// <param name="logger">Optional logger for audit messages.</param>
    /// <returns>This processor instance for method chaining.</returns>
    /// <exception cref="SourceCodePatchException">Thrown if an imported file is not found in the cache.</exception>
    public SourceCodeImportsProcessor ProcessImports(ILogger? logger = null)
    {
        var importDirectives = FindImportDirectives();

        foreach (var import in importDirectives)
        {
            if (!_importsCache.TryGetValue(import.FileName, out var importContent))
            {
                throw CreateException(
                    $"Import file '{import.FileName}' not found in imports cache",
                    import.DirectiveStart);
            }

            // Build the replacement: commented original line + newline + import contents
            var injection = new StringBuilder();
            injection.Append("//");  // Comment out the #import line
            injection.Append(_source.AsSpan((int)import.DirectiveStart, (int)(import.LineEnd - import.DirectiveStart)));
            injection.AppendLine();
            injection.Append(importContent);

            // Ensure import content ends with newline
            if (!importContent.EndsWith('\n'))
            {
                injection.AppendLine();
            }

            // Queue a replacement (remove original, insert new)
            AddInsertion(new PendingInsertion(
                import.DirectiveStart,
                injection.ToString(),
                (int)(import.LineEnd - import.DirectiveStart)));

            logger?.Audit($"[VGE] Processed #import '{import.FileName}' at position {import.DirectiveStart}");
        }

        return this;
    }

    #endregion

    #region Private Helper Methods

    /// <summary>
    /// Finds all #import directives in the tokenized shader source.
    /// </summary>
    private List<ImportDirective> FindImportDirectives()
    {
        var imports = new List<ImportDirective>();

        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            // Look for # symbol
            if (token is not TinyTokenizer.SymbolToken { Content.Length: 1 } symbolToken ||
                symbolToken.Content.Span[0] != '#')
            {
                continue;
            }

            long directiveStart = symbolToken.Position;

            // Find next non-whitespace token (should be "import")
            int nextIdx = FindNextNonWhitespace(i + 1);
            if (nextIdx < 0 ||
                _tokens[nextIdx] is not TinyTokenizer.IdentToken identToken ||
                !identToken.Content.Span.Equals("import".AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            // Find the file name (could be in quotes or angle brackets)
            string? fileName = ExtractImportFileName(nextIdx + 1, out int fileNameEndIdx);
            if (fileName == null)
            {
                continue;
            }

            // Find end of line
            long lineEnd = FindEndOfLine(fileNameEndIdx);

            imports.Add(new ImportDirective(directiveStart, lineEnd, fileName));
        }

        return imports;
    }

    /// <summary>
    /// Extracts the file name from an #import directive.
    /// Handles both "filename" and &lt;filename&gt; syntax.
    /// </summary>
    private string? ExtractImportFileName(int startIdx, out int endIdx)
    {
        endIdx = startIdx;

        int nextIdx = FindNextNonWhitespace(startIdx);
        if (nextIdx < 0)
        {
            return null;
        }

        var token = _tokens[nextIdx];

        // Check for string literal (quoted filename like "vgeshared.ash")
        if (token is TinyTokenizer.StringToken stringToken)
        {
            endIdx = nextIdx;
            // Remove the quotes from the string content
            var content = stringToken.Content.Span;
            if (content.Length >= 2)
            {
                return content[1..^1].ToString();
            }
            return null;
        }

        // Check for angle bracket syntax (<filename>)
        if (token is TinyTokenizer.SymbolToken { Content.Length: 1 } ltSymbol && ltSymbol.Content.Span[0] == '<')
        {
            var fileNameBuilder = new StringBuilder();
            int idx = nextIdx + 1;

            while (idx < _tokens.Length)
            {
                var innerToken = _tokens[idx];

                if (innerToken is TinyTokenizer.SymbolToken { Content.Length: 1 } gtSymbol && gtSymbol.Content.Span[0] == '>')
                {
                    endIdx = idx;
                    return fileNameBuilder.ToString();
                }

                // Don't include whitespace in filename for angle bracket syntax
                if (innerToken is not TinyTokenizer.WhitespaceToken)
                {
                    fileNameBuilder.Append(innerToken.Content.Span);
                }

                idx++;
            }
        }

        return null;
    }

    #endregion
}
