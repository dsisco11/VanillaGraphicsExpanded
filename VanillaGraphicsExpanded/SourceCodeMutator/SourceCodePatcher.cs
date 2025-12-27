using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;

using TinyTokenizer;

namespace VanillaGraphicsExpanded;

/// <summary>
/// A fluent builder for patching source code using token-based parsing.
/// Accumulates insertions and applies them in a single <see cref="Build"/> call.
/// </summary>
/// <remarks>
/// Uses TinyTokenizer for robust parsing that correctly handles comments, strings, 
/// nested blocks, and preprocessor directives.
/// </remarks>
/// <example>
/// <code>
/// var patched = new SourceCodePatcher(shaderCode)
///     .FindVersionDirective().After().Insert("// Injected by VGE\n")
///     .FindFunction("main").BeforeClose().Insert("    outNormal = vec4(normal, 1.0);\n")
///     .Build();
/// </code>
/// </example>
public class SourceCodePatcher
{
    #region Fields

    protected readonly string _source;
    protected readonly ImmutableArray<Token> _tokens;
    protected readonly List<PendingInsertion> _insertions = [];
    protected long _currentInsertionPoint = -1;
    protected readonly string? _sourceName;

    #endregion

    #region Accessors

    /// <summary>
    /// Gets the name of the source being patched, or "&lt;unknown&gt;" if not specified.
    /// </summary>
    public string SourceName => _sourceName ?? "<unknown>";

    #endregion

    #region Nested Types

    /// <summary>
    /// Represents a pending text insertion or replacement.
    /// </summary>
    /// <param name="Position">The position in the source to insert/replace at.</param>
    /// <param name="Text">The text to insert.</param>
    /// <param name="RemoveLength">Number of characters to remove before inserting (0 for pure insertion).</param>
    protected readonly record struct PendingInsertion(long Position, string Text, int RemoveLength = 0);

    #endregion

    #region Constructor

    /// <summary>
    /// Creates a new source code patcher for the given source code.
    /// </summary>
    /// <param name="source">The source code to patch.</param>
    /// <param name="sourceName">Optional source name for error messages.</param>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="source"/> is null.</exception>
    /// <exception cref="SourceCodePatchException">Thrown if tokenization produces errors.</exception>
    public SourceCodePatcher(string source, string? sourceName = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _sourceName = sourceName;

        // Configure tokenizer for C-style languages (C-style comments, tag prefixes for preprocessor directives and imports)
        var options = TokenizerOptions.Default
            .WithCommentStyles(CommentStyle.CStyleSingleLine, CommentStyle.CStyleMultiLine)
            .WithTagPrefixes('#', '@');

        _tokens = source.TokenizeToTokens(options);

        // Check for parse errors
        if (_tokens.HasErrors())
        {
            var firstError = _tokens.GetErrors().First();
            throw CreateException($"Tokenization error: {firstError.ErrorMessage}", firstError.Position);
        }
    }

    #endregion

    #region Find Methods

    /// <summary>
    /// Finds the <c>#version</c> preprocessor directive.
    /// </summary>
    /// <returns>A selector for choosing the insertion position relative to the directive.</returns>
    public DirectiveSelector FindVersionDirective()
    {
        return FindPreprocessorDirective("version");
    }

    /// <summary>
    /// Finds a preprocessor directive by name.
    /// </summary>
    /// <param name="directive">The directive name (e.g., "version", "define", "if").</param>
    /// <param name="condition">Optional condition text to match (e.g., "SSAOLEVEL > 0").</param>
    /// <returns>A selector for choosing the insertion position relative to the directive.</returns>
    public DirectiveSelector FindPreprocessorDirective(string directive, string? condition = null)
    {
        long directiveStart = -1;
        long lineEnd = -1;

        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            // Look for tagged identifier with # prefix (e.g., #version, #define)
            if (token is TaggedIdentToken { Tag: '#' } taggedToken &&
                taggedToken.Content.Span.Equals(directive.AsSpan(), StringComparison.Ordinal))
            {
                // If condition specified, check it matches
                if (condition != null)
                {
                    var lineContent = GetRestOfLine(i + 1);
                    if (!lineContent.Contains(condition, StringComparison.Ordinal))
                    {
                        continue;
                    }
                }

                directiveStart = taggedToken.Position;
                lineEnd = FindEndOfLine(i);
                break;
            }
        }

        return new DirectiveSelector(this, directive, condition, directiveStart, lineEnd);
    }

    /// <summary>
    /// Finds a preprocessor block (e.g., #if ... #endif) by directive and optional condition.
    /// </summary>
    /// <param name="directive">The opening directive ("if", "ifdef", or "ifndef").</param>
    /// <param name="condition">Optional condition text to match.</param>
    /// <returns>A selector for choosing the insertion position relative to the block.</returns>
    public PreprocessorBlockSelector FindPreprocessorBlock(string directive, string? condition = null)
    {
        long blockStart = -1;
        long blockStartLineEnd = -1;
        long endifStart = -1;

        int depth = 0;
        bool foundStart = false;

        for (int i = 0; i < _tokens.Length; i++)
        {
            var token = _tokens[i];

            // Look for tagged identifier with # prefix
            if (token is TaggedIdentToken { Tag: '#' } taggedToken)
            {
                var directiveSpan = taggedToken.Content.Span;

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
                            blockStart = taggedToken.Position;
                            blockStartLineEnd = FindEndOfLine(i);
                        }
                        else
                        {
                            var lineContent = GetRestOfLine(i + 1);
                            if (lineContent.Contains(condition, StringComparison.Ordinal))
                            {
                                foundStart = true;
                                depth = 1;
                                blockStart = taggedToken.Position;
                                blockStartLineEnd = FindEndOfLine(i);
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
                        endifStart = taggedToken.Position;
                        break;
                    }
                }
            }
        }

        return new PreprocessorBlockSelector(this, directive, condition, blockStart, blockStartLineEnd, endifStart);
    }

    /// <summary>
    /// Finds a function definition by name.
    /// </summary>
    /// <param name="functionName">The name of the function to find.</param>
    /// <returns>A selector for choosing the insertion position relative to the function.</returns>
    public FunctionSelector FindFunction(string functionName)
    {
        BlockToken? bodyBlock = null;

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
                    if (bodyBlockIdx >= 0 && _tokens[bodyBlockIdx] is BlockToken block &&
                        block.OpeningDelimiter == '{')
                    {
                        bodyBlock = block;
                        break;
                    }
                }
            }
        }

        return new FunctionSelector(this, functionName, bodyBlock);
    }

    /// <summary>
    /// Finds layout declarations (e.g., <c>layout(...) out</c>).
    /// </summary>
    /// <returns>A selector for choosing the insertion position relative to layout declarations.</returns>
    public LayoutSelector FindLayoutDeclarations()
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

        return new LayoutSelector(this, lastPosition);
    }

    #endregion

    #region Selectors

    /// <summary>
    /// Selector for choosing insertion position relative to a preprocessor directive.
    /// </summary>
    public readonly struct DirectiveSelector
    {
        private readonly SourceCodePatcher _patcher;
        private readonly string _directive;
        private readonly string? _condition;
        private readonly long _directiveStart;
        private readonly long _lineEnd;

        internal DirectiveSelector(SourceCodePatcher patcher, string directive, string? condition, long directiveStart, long lineEnd)
        {
            _patcher = patcher;
            _directive = directive;
            _condition = condition;
            _directiveStart = directiveStart;
            _lineEnd = lineEnd;
        }

        /// <summary>
        /// Gets whether the directive was found.
        /// </summary>
        public bool Found => _directiveStart >= 0;

        /// <summary>
        /// Sets the insertion point to immediately after the directive line.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the directive was not found.</exception>
        public SourceCodePatcher After()
        {
            if (!Found)
            {
                var desc = _condition != null ? $"#{_directive} {_condition}" : $"#{_directive}";
                throw _patcher.CreateException($"Preprocessor directive '{desc}' not found in source");
            }

            _patcher._currentInsertionPoint = _lineEnd;
            return _patcher;
        }

        /// <summary>
        /// Sets the insertion point to immediately before the directive.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the directive was not found.</exception>
        public SourceCodePatcher Before()
        {
            if (!Found)
            {
                var desc = _condition != null ? $"#{_directive} {_condition}" : $"#{_directive}";
                throw _patcher.CreateException($"Preprocessor directive '{desc}' not found in source");
            }

            _patcher._currentInsertionPoint = _directiveStart;
            return _patcher;
        }
    }

    /// <summary>
    /// Selector for choosing insertion position relative to a preprocessor block (#if...#endif).
    /// </summary>
    public readonly struct PreprocessorBlockSelector
    {
        private readonly SourceCodePatcher _patcher;
        private readonly string _directive;
        private readonly string? _condition;
        private readonly long _blockStart;
        private readonly long _blockStartLineEnd;
        private readonly long _endifStart;

        internal PreprocessorBlockSelector(SourceCodePatcher patcher, string directive, string? condition,
            long blockStart, long blockStartLineEnd, long endifStart)
        {
            _patcher = patcher;
            _directive = directive;
            _condition = condition;
            _blockStart = blockStart;
            _blockStartLineEnd = blockStartLineEnd;
            _endifStart = endifStart;
        }

        /// <summary>
        /// Gets whether the preprocessor block was found.
        /// </summary>
        public bool Found => _blockStart >= 0 && _endifStart >= 0;

        /// <summary>
        /// Sets the insertion point to immediately after the opening directive line.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the block was not found.</exception>
        public SourceCodePatcher After()
        {
            if (!Found)
            {
                var desc = _condition != null ? $"#{_directive} {_condition}" : $"#{_directive}";
                throw _patcher.CreateException($"Preprocessor block '{desc}' not found in source");
            }

            _patcher._currentInsertionPoint = _blockStartLineEnd;
            return _patcher;
        }

        /// <summary>
        /// Sets the insertion point to immediately before the matching #endif.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the block was not found.</exception>
        public SourceCodePatcher BeforeEnd()
        {
            if (!Found)
            {
                var desc = _condition != null ? $"#{_directive} {_condition}" : $"#{_directive}";
                throw _patcher.CreateException($"No matching #endif found for '{desc}'");
            }

            _patcher._currentInsertionPoint = _endifStart;
            return _patcher;
        }
    }

    /// <summary>
    /// Selector for choosing insertion position relative to a function.
    /// </summary>
    public readonly struct FunctionSelector
    {
        private readonly SourceCodePatcher _patcher;
        private readonly string _functionName;
        private readonly BlockToken? _bodyBlock;

        internal FunctionSelector(SourceCodePatcher patcher, string functionName, BlockToken? bodyBlock)
        {
            _patcher = patcher;
            _functionName = functionName;
            _bodyBlock = bodyBlock;
        }

        /// <summary>
        /// Gets whether the function was found.
        /// </summary>
        public bool Found => _bodyBlock != null;

        /// <summary>
        /// Sets the insertion point to immediately after the opening brace of the function body.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the function was not found.</exception>
        public SourceCodePatcher AtTop()
        {
            if (_bodyBlock == null)
            {
                throw _patcher.CreateException($"No {_functionName}() function found in source");
            }

            _patcher._currentInsertionPoint = _bodyBlock.Position + 1;
            return _patcher;
        }

        /// <summary>
        /// Sets the insertion point to immediately before the closing brace of the function body.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the function was not found.</exception>
        public SourceCodePatcher BeforeClose()
        {
            if (_bodyBlock == null)
            {
                throw _patcher.CreateException($"No {_functionName}() function found in source");
            }

            long closingBracePosition = _bodyBlock.Position + _bodyBlock.Content.Length - 1;
            _patcher._currentInsertionPoint = closingBracePosition;
            return _patcher;
        }

        /// <summary>
        /// Sets the insertion point to immediately after the closing brace of the function body.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if the function was not found.</exception>
        public SourceCodePatcher AfterClose()
        {
            if (_bodyBlock == null)
            {
                throw _patcher.CreateException($"No {_functionName}() function found in source");
            }

            long afterClosingBrace = _bodyBlock.Position + _bodyBlock.Content.Length;
            _patcher._currentInsertionPoint = afterClosingBrace;
            return _patcher;
        }
    }

    /// <summary>
    /// Selector for choosing insertion position relative to layout declarations.
    /// </summary>
    public readonly struct LayoutSelector
    {
        private readonly SourceCodePatcher _patcher;
        private readonly long _lastPosition;

        internal LayoutSelector(SourceCodePatcher patcher, long lastPosition)
        {
            _patcher = patcher;
            _lastPosition = lastPosition;
        }

        /// <summary>
        /// Gets whether any layout declarations were found.
        /// </summary>
        public bool Found => _lastPosition >= 0;

        /// <summary>
        /// Sets the insertion point to immediately after the last layout declaration.
        /// Falls back to after #version directive if no layout declarations exist.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        public SourceCodePatcher AfterLast()
        {
            if (!Found)
            {
                // Fall back to after #version
                return _patcher.FindVersionDirective().After();
            }

            _patcher._currentInsertionPoint = _lastPosition;
            return _patcher;
        }

        /// <summary>
        /// Sets the insertion point to immediately after the last layout declaration,
        /// or throws if none exist.
        /// </summary>
        /// <returns>The patcher instance for method chaining.</returns>
        /// <exception cref="SourceCodePatchException">Thrown if no layout declarations were found.</exception>
        public SourceCodePatcher AfterLastOrThrow()
        {
            if (!Found)
            {
                throw _patcher.CreateException("No layout declarations found in source");
            }

            _patcher._currentInsertionPoint = _lastPosition;
            return _patcher;
        }
    }

    #endregion

    #region Insertion Methods

    /// <summary>
    /// Queues text to be inserted at the current insertion point.
    /// </summary>
    /// <param name="text">The text to insert.</param>
    /// <returns>This patcher instance for method chaining.</returns>
    /// <exception cref="InvalidOperationException">Thrown if no insertion point has been set.</exception>
    public SourceCodePatcher Insert(string text)
    {
        if (_currentInsertionPoint < 0)
        {
            throw new InvalidOperationException(
                "No insertion point set. Call a Find method and location selector before Insert.");
        }

        _insertions.Add(new PendingInsertion(_currentInsertionPoint, text));
        return this;
    }

    #endregion

    #region Build

    /// <summary>
    /// Applies all queued insertions and returns the patched source code.
    /// </summary>
    /// <returns>The patched source code.</returns>
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

    #region Protected Helper Methods

    /// <summary>
    /// Finds the index of the next non-whitespace, non-comment token.
    /// </summary>
    protected int FindNextNonWhitespace(int startIndex)
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

    /// <summary>
    /// Finds the position at the end of the line containing the given token.
    /// </summary>
    protected long FindEndOfLine(int tokenIndex)
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

    /// <summary>
    /// Finds the position at the end of the statement (after semicolon) starting from the given token.
    /// </summary>
    protected long FindEndOfStatement(int tokenIndex)
    {
        for (int i = tokenIndex; i < _tokens.Length; i++)
        {
            var token = _tokens[i];
            if (token is SymbolToken { Symbol: ';' })
            {
                // Find end of line after semicolon
                return FindEndOfLine(i);
            }
        }
        return -1;
    }

    /// <summary>
    /// Gets the text content from the given token to the end of its line.
    /// </summary>
    protected string GetRestOfLine(int tokenIndex)
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

    /// <summary>
    /// Creates an exception with optional position and source name information.
    /// </summary>
    protected SourceCodePatchException CreateException(string message, long position = -1)
    {
        if (_sourceName != null)
        {
            return new SourceCodePatchException(message, _sourceName, position);
        }
        return position >= 0
            ? new SourceCodePatchException(message, position)
            : new SourceCodePatchException(message);
    }

    /// <summary>
    /// Adds a pending insertion directly. Used by subclasses for specialized operations.
    /// </summary>
    protected void AddInsertion(PendingInsertion insertion)
    {
        _insertions.Add(insertion);
    }

    #endregion
}
