using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using TinyTokenizer;

using Vintagestory.API.Common;

namespace VanillaGraphicsExpanded;

/// <summary>
/// A fluent builder for patching GLSL shader source code using token-based parsing.
/// Accumulates insertions and applies them in a single <see cref="Build"/> call.
/// </summary>
/// <remarks>
/// Uses TinyTokenizer for robust parsing that correctly handles comments, strings, 
/// nested blocks, and preprocessor directives.
/// </remarks>
/// <example>
/// <code>
/// var patched = new ShaderSourcePatcher(shaderCode)
///     .AfterVersionDirective().Insert("// Injected by VGE\n")
///     .BeforeMainClose().Insert("    outNormal = vec4(normal, 1.0);\n")
///     .Build();
/// </code>
/// </example>
public class ShaderSourcePatcher
{
    #region Fields

    private readonly string _source;
    private readonly ImmutableArray<Token> _tokens;
    private readonly List<PendingInsertion> _insertions = [];
    private long _currentInsertionPoint = -1;
    private readonly string? _shaderName;

    #endregion

    #region Accessors

    public string ShaderName => _shaderName ?? "<unknown>";

    #endregion

    #region Nested Types

    /// <summary>
    /// Represents a pending text insertion or replacement.
    /// </summary>
    /// <param name="Position">The position in the source to insert/replace at.</param>
    /// <param name="Text">The text to insert.</param>
    /// <param name="RemoveLength">Number of characters to remove before inserting (0 for pure insertion).</param>
    private readonly record struct PendingInsertion(long Position, string Text, int RemoveLength = 0);

    /// <summary>
    /// Represents a found #import directive in shader source.
    /// </summary>
    private readonly record struct ImportDirective(long DirectiveStart, long LineEnd, string FileName);

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new shader source patcher for the given GLSL source code.
    /// </summary>
    /// <param name="source">The GLSL shader source code to patch.</param>
    /// <param name="shaderName">Optional shader name for error messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="ShaderPatchException">Thrown if tokenization produces errors.</exception>
    public ShaderSourcePatcher(string source, string? shaderName = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _shaderName = shaderName;

        // Configure tokenizer for GLSL (C-style comments, relevant symbols)
        var options = TokenizerOptions.Default
            .WithCommentStyles(CommentStyle.CStyleSingleLine, CommentStyle.CStyleMultiLine);

        _tokens = source.TokenizeToTokens(options);

        // Check for parse errors
        if (_tokens.HasErrors())
        {
            var firstError = _tokens.GetErrors().First();
            throw CreateException($"Tokenization error: {firstError.ErrorMessage}", firstError.Position);
        }
    }

    #endregion

    #region Fluent Location Methods

    /// <summary>
    /// Sets the insertion point to immediately after the <c>#version</c> directive line.
    /// </summary>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if no <c>#version</c> directive is found.</exception>
    public ShaderSourcePatcher AfterVersionDirective()
    {
        var position = FindEndOfDirectiveLine("version");
        if (position < 0)
        {
            throw CreateException("No #version directive found in shader source");
        }

        _currentInsertionPoint = position;
        return this;
    }

    /// <summary>
    /// Sets the insertion point to immediately after the last <c>layout(...) out</c> declaration.
    /// Falls back to after <c>#version</c> if no layout declarations exist.
    /// </summary>
    /// <returns>This patcher instance for method chaining.</returns>
    public ShaderSourcePatcher AfterLayoutDeclarations()
    {
        var position = FindLastLayoutOutDeclaration();
        if (position < 0)
        {
            // Fall back to after #version
            return AfterVersionDirective();
        }

        _currentInsertionPoint = position;
        return this;
    }

    /// <summary>
    /// Sets the insertion point to immediately before the closing brace of the <c>main()</c> function.
    /// </summary>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if <c>main()</c> function is not found.</exception>
    public ShaderSourcePatcher BeforeMainClose() => BeforeFunctionClose("main");

    /// <summary>
    /// Sets the insertion point to immediately after the opening brace of the <c>main()</c> function.
    /// </summary>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if <c>main()</c> function is not found.</exception>
    public ShaderSourcePatcher AtTopOfMain() => AtTopOfFunction("main");

    /// <summary>
    /// Sets the insertion point to immediately before the closing brace of the specified function.
    /// </summary>
    /// <param name="functionName">The name of the function to target.</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if the function is not found.</exception>
    public ShaderSourcePatcher BeforeFunctionClose(string functionName)
    {
        var bodyBlock = FindFunctionBody(functionName);
        if (bodyBlock == null)
        {
            throw CreateException($"No {functionName}() function found in shader source");
        }

        // Position is at the opening brace, we need to find the closing brace
        // The block's full content includes both braces, so end position is Position + Content.Length - 1
        long closingBracePosition = bodyBlock.Position + bodyBlock.Content.Length - 1;
        _currentInsertionPoint = closingBracePosition;
        return this;
    }

    /// <summary>
    /// Sets the insertion point to immediately after the opening brace of the specified function.
    /// </summary>
    /// <param name="functionName">The name of the function to target.</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if the function is not found.</exception>
    public ShaderSourcePatcher AtTopOfFunction(string functionName)
    {
        var bodyBlock = FindFunctionBody(functionName);
        if (bodyBlock == null)
        {
            throw CreateException($"No {functionName}() function found in shader source");
        }

        // Position after the opening brace
        _currentInsertionPoint = bodyBlock.Position + 1;
        return this;
    }

    /// <summary>
    /// Sets the insertion point to immediately after a specific preprocessor directive line.
    /// </summary>
    /// <param name="directive">The directive name (e.g., "if", "ifdef", "define").</param>
    /// <param name="condition">Optional condition text to match (e.g., "SSAOLEVEL > 0").</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if the directive is not found.</exception>
    public ShaderSourcePatcher AfterPreprocessorDirective(string directive, string? condition = null)
    {
        var position = FindEndOfDirectiveLine(directive, condition);
        if (position < 0)
        {
            var desc = condition != null ? $"#{directive} {condition}" : $"#{directive}";
            throw CreateException($"Preprocessor directive '{desc}' not found in shader source");
        }

        _currentInsertionPoint = position;
        return this;
    }

    /// <summary>
    /// Sets the insertion point to immediately before the <c>#endif</c> that matches a specific <c>#if</c>/<c>#ifdef</c>.
    /// </summary>
    /// <param name="directive">The opening directive ("if" or "ifdef").</param>
    /// <param name="condition">Optional condition text to match.</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if the matching #endif is not found.</exception>
    public ShaderSourcePatcher BeforePreprocessorEnd(string directive, string? condition = null)
    {
        var position = FindMatchingEndif(directive, condition);
        if (position < 0)
        {
            var desc = condition != null ? $"#{directive} {condition}" : $"#{directive}";
            throw CreateException($"No matching #endif found for '{desc}'");
        }

        _currentInsertionPoint = position;
        return this;
    }

    #endregion

    #region Insertion Method

    /// <summary>
    /// Queues text to be inserted at the current insertion point.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no insertion point has been set.</exception>
    public ShaderSourcePatcher Insert(string text)
    {
        if (_currentInsertionPoint < 0)
        {
            throw new InvalidOperationException(
                "No insertion point set. Call a location method (e.g., AfterVersionDirective) before Insert.");
        }

        _insertions.Add(new PendingInsertion(_currentInsertionPoint, text));
        return this;
    }

    /// <summary>
    /// Finds all <c>#import</c> directives and queues insertions to comment them out
    /// and inject the referenced file contents below each one.
    /// </summary>
    /// <param name="importsCache">Dictionary mapping import file names to their contents.</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="ShaderPatchException">Thrown if an imported file is not found in the cache.</exception>
    public ShaderSourcePatcher ProcessImports(IReadOnlyDictionary<string, string> importsCache, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(importsCache);

        var importDirectives = FindImportDirectives();

        foreach (var import in importDirectives)
        {
            if (!importsCache.TryGetValue(import.FileName, out var importContent))
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
            _insertions.Add(new PendingInsertion(
                import.DirectiveStart,
                injection.ToString(),
                (int)(import.LineEnd - import.DirectiveStart)));

            log?.Audit($"[VGE] Processed #import '{import.FileName}' at position {import.DirectiveStart}");
        }

        return this;
    }

    #endregion

    #region Build Method

    /// <summary>
    /// Applies all queued insertions and returns the patched shader source.
    /// </summary>
    /// <returns>The patched shader source code.</returns>
    /// <remarks>
    /// Insertions are applied in reverse position order to preserve position accuracy.
    /// Multiple insertions at the same position are applied in the order they were added.
    /// </remarks>
    public string Build()
    {
        if (_insertions.Count == 0)
        {
            return _source;
        }

        // Sort by position descending, but preserve order for same position
        var sortedInsertions = _insertions
            .Select((ins, idx) => (ins, idx))
            .OrderByDescending(x => x.ins.Position)
            .ThenByDescending(x => x.idx)
            .Select(x => x.ins)
            .ToList();

        var result = new StringBuilder(_source);

        foreach (var insertion in sortedInsertions)
        {
            if (insertion.RemoveLength > 0)
            {
                result.Remove((int)insertion.Position, insertion.RemoveLength);
            }
            result.Insert((int)insertion.Position, insertion.Text);
        }

        return result.ToString();
    }

    #endregion

    #region Private Token Search Methods

    /// <summary>
    /// Finds the end position of a preprocessor directive line.
    /// </summary>
    private long FindEndOfDirectiveLine(string directiveName, string? condition = null)
    {
        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            // Look for # symbol
            if (token is SymbolToken { Content.Length: 1 } symbolToken &&
                symbolToken.Content.Span[0] == '#')
            {
                // Next non-whitespace should be the directive name
                var nextIdent = FindNextNonWhitespace(i + 1);
                if (nextIdent >= 0 && _tokens[nextIdent] is IdentToken identToken &&
                    identToken.Content.Span.Equals(directiveName.AsSpan(), StringComparison.Ordinal))
                {
                    // If condition specified, check it matches
                    if (condition != null)
                    {
                        var lineContent = GetRestOfLine(nextIdent + 1);
                        if (!lineContent.Contains(condition, StringComparison.Ordinal))
                        {
                            continue;
                        }
                    }

                    // Find end of this line (newline token or end of tokens)
                    return FindEndOfLine(nextIdent);
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the end position of the last layout(...) out declaration line.
    /// </summary>
    private long FindLastLayoutOutDeclaration()
    {
        long lastPosition = -1;

        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            // Look for 'layout' identifier
            if (token is IdentToken { Content.Length: 6 } identToken &&
                identToken.Content.Span.Equals("layout".AsSpan(), StringComparison.Ordinal))
            {
                // Should be followed by a parenthesis block
                var nextNonWs = FindNextNonWhitespace(i + 1);
                if (nextNonWs >= 0 && _tokens[nextNonWs] is BlockToken { OpeningDelimiter: '(' })
                {
                    // Check if 'out' appears after the block
                    var afterBlock = FindNextNonWhitespace(nextNonWs + 1);
                    if (afterBlock >= 0 && _tokens[afterBlock] is IdentToken outToken &&
                        outToken.Content.Span.Equals("out".AsSpan(), StringComparison.Ordinal))
                    {
                        // Find semicolon to get end of declaration
                        var endOfDecl = FindEndOfStatement(afterBlock);
                        if (endOfDecl > lastPosition)
                        {
                            lastPosition = endOfDecl;
                        }
                    }
                }
            }
        }

        return lastPosition;
    }

    /// <summary>
    /// Finds the body block of a function by name.
    /// </summary>
    private BlockToken? FindFunctionBody(string functionName)
    {
        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            // Look for function name identifier
            if (token is IdentToken identToken &&
                identToken.Content.Span.Equals(functionName.AsSpan(), StringComparison.Ordinal))
            {
                // Should be followed by parameter block (...)
                var paramBlockIdx = FindNextNonWhitespace(i + 1);
                if (paramBlockIdx >= 0 && _tokens[paramBlockIdx] is BlockToken { OpeningDelimiter: '(' })
                {
                    // Should be followed by body block {...}
                    var bodyBlockIdx = FindNextNonWhitespace(paramBlockIdx + 1);
                    if (bodyBlockIdx >= 0 && _tokens[bodyBlockIdx] is BlockToken bodyBlock &&
                        bodyBlock.OpeningDelimiter == '{')
                    {
                        return bodyBlock;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Finds the position of the #endif that matches a specific #if/#ifdef directive.
    /// </summary>
    private long FindMatchingEndif(string directive, string? condition)
    {
        int depth = 0;
        bool foundStart = false;

        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            if (token is SymbolToken { Content.Length: 1 } symbolToken &&
                symbolToken.Content.Span[0] == '#')
            {
                var nextIdent = FindNextNonWhitespace(i + 1);
                if (nextIdent < 0 || _tokens[nextIdent] is not IdentToken identToken)
                    continue;

                var directiveSpan = identToken.Content.Span;

                if (directiveSpan.Equals("if".AsSpan(), StringComparison.Ordinal) ||
                    directiveSpan.Equals("ifdef".AsSpan(), StringComparison.Ordinal) ||
                    directiveSpan.Equals("ifndef".AsSpan(), StringComparison.Ordinal))
                {
                    if (!foundStart &&
                        directiveSpan.Equals(directive.AsSpan(), StringComparison.Ordinal))
                    {
                        if (condition == null)
                        {
                            foundStart = true;
                            depth = 1;
                        }
                        else
                        {
                            var lineContent = GetRestOfLine(nextIdent + 1);
                            if (lineContent.Contains(condition, StringComparison.Ordinal))
                            {
                                foundStart = true;
                                depth = 1;
                            }
                        }
                    }
                    else if (foundStart)
                    {
                        depth++;
                    }
                }
                else if (directiveSpan.Equals("endif".AsSpan(), StringComparison.Ordinal) && foundStart)
                {
                    depth--;
                    if (depth == 0)
                    {
                        // Return position of the # symbol
                        return symbolToken.Position;
                    }
                }
            }
        }

        return -1;
    }

    #endregion

    #region Private Helper Methods

    private int FindNextNonWhitespace(int startIndex)
    {
        for (int i = startIndex; i < _tokens.Length; i++)
        {
            if (_tokens[i] is not WhitespaceToken && _tokens[i] is not CommentToken)
            {
                return i;
            }
        }
        return -1;
    }

    private long FindEndOfLine(int tokenIndex)
    {
        for (int i = tokenIndex; i < _tokens.Length; i++)
        {
            var token = _tokens[i];
            if (token is WhitespaceToken wsToken)
            {
                // Check if whitespace contains newline
                var span = wsToken.Content.Span;
                int newlineIdx = span.IndexOf('\n');
                if (newlineIdx >= 0)
                {
                    return wsToken.Position + newlineIdx + 1;
                }
            }
        }

        // No newline found, return end of source
        return _source.Length;
    }

    private long FindEndOfStatement(int tokenIndex)
    {
        for (int i = tokenIndex; i < _tokens.Length; i++)
        {
            var token = _tokens[i];
            if (token is SymbolToken symbolToken && symbolToken.Content.Span[0] == ';')
            {
                // Find end of line after semicolon
                return FindEndOfLine(i);
            }
        }
        return -1;
    }

    private string GetRestOfLine(int tokenIndex)
    {
        var sb = new StringBuilder();
        for (int i = tokenIndex; i < _tokens.Length; i++)
        {
            var token = _tokens[i];
            if (token is WhitespaceToken wsToken)
            {
                var span = wsToken.Content.Span;
                if (span.Contains('\n'))
                {
                    break;
                }
            }
            sb.Append(token.Content.Span);
        }
        return sb.ToString();
    }

    private ShaderPatchException CreateException(string message, long position = -1)
    {
        if (_shaderName != null)
        {
            return new ShaderPatchException(message, _shaderName, position);
        }
        return position >= 0
            ? new ShaderPatchException(message, position)
            : new ShaderPatchException(message);
    }

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
            if (token is not SymbolToken { Content.Length: 1 } symbolToken ||
                symbolToken.Content.Span[0] != '#')
            {
                continue;
            }

            long directiveStart = symbolToken.Position;

            // Find next non-whitespace token (should be "import")
            int nextIdx = FindNextNonWhitespace(i + 1);
            if (nextIdx < 0 ||
                _tokens[nextIdx] is not IdentToken identToken ||
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
        if (token is StringToken stringToken)
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
        if (token is SymbolToken { Content.Length: 1 } ltSymbol && ltSymbol.Content.Span[0] == '<')
        {
            var fileNameBuilder = new StringBuilder();
            int idx = nextIdx + 1;

            while (idx < _tokens.Length)
            {
                var innerToken = _tokens[idx];

                if (innerToken is SymbolToken { Content.Length: 1 } gtSymbol && gtSymbol.Content.Span[0] == '>')
                {
                    endIdx = idx;
                    return fileNameBuilder.ToString();
                }

                // Don't include whitespace in filename for angle bracket syntax
                if (innerToken is not WhitespaceToken)
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
