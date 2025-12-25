using System;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Utility class for locating and patching text content, particularly shader code.
/// Uses <see cref="ReadOnlySpan{T}"/> for efficient, allocation-free text processing.
/// </summary>
public static class TextPatcher
{
    #region Constants

    private const string MainFunctionSignature = "void main()";
    private const char OpenBrace = '{';
    private const char CloseBrace = '}';
    private const char SingleLineCommentStart = '/';
    private const char MultiLineCommentEnd = '*';

    #endregion

    #region Public Methods

    /// <summary>
    /// Locates the index of the closing brace for the <c>main</c> function in the given text.
    /// </summary>
    /// <param name="text">The source text to search (typically shader code).</param>
    /// <returns>
    /// The zero-based index of the closing brace of the <c>main</c> function,
    /// or <c>-1</c> if the function is not found or is malformed.
    /// </returns>
    /// <remarks>
    /// This method handles:
    /// <list type="bullet">
    ///   <item>Both <c>void main()</c> and <c>void main(void)</c> signatures</item>
    ///   <item>Single-line comments (<c>//</c>)</item>
    ///   <item>Multi-line comments (<c>/* */</c>)</item>
    ///   <item>Nested braces within the function body</item>
    /// </list>
    /// </remarks>
    public static int FindMainFunctionClosingBrace(ReadOnlySpan<char> text)
    {
        int mainStart = FindMainFunctionStart(text);
        if (mainStart < 0)
        {
            return -1;
        }

        int openBraceIndex = FindOpeningBrace(text, mainStart);
        if (openBraceIndex < 0)
        {
            return -1;
        }

        return FindMatchingCloseBrace(text, openBraceIndex);
    }

    /// <summary>
    /// Locates the index of the closing brace for the <c>main</c> function in the given string.
    /// </summary>
    /// <param name="text">The source text to search (typically shader code).</param>
    /// <returns>
    /// The zero-based index of the closing brace of the <c>main</c> function,
    /// or <c>-1</c> if the function is not found or is malformed.
    /// </returns>
    public static int FindMainFunctionClosingBrace(string text)
    {
        return FindMainFunctionClosingBrace(text.AsSpan());
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Finds the start position of the <c>main</c> function signature.
    /// </summary>
    private static int FindMainFunctionStart(ReadOnlySpan<char> text)
    {
        return text.IndexOf(MainFunctionSignature.AsSpan());
    }

    /// <summary>
    /// Finds the opening brace of a function starting from a given position.
    /// </summary>
    private static int FindOpeningBrace(ReadOnlySpan<char> text, int startIndex)
    {
        for (int i = startIndex; i < text.Length; i++)
        {
            char c = text[i];

            // Skip single-line comments
            if (c == SingleLineCommentStart && i + 1 < text.Length && text[i + 1] == SingleLineCommentStart)
            {
                i = SkipSingleLineComment(text, i);
                continue;
            }

            // Skip multi-line comments
            if (c == SingleLineCommentStart && i + 1 < text.Length && text[i + 1] == MultiLineCommentEnd)
            {
                i = SkipMultiLineComment(text, i);
                continue;
            }

            if (c == OpenBrace)
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// Finds the matching closing brace for an opening brace, handling nested braces and comments.
    /// </summary>
    private static int FindMatchingCloseBrace(ReadOnlySpan<char> text, int openBraceIndex)
    {
        int braceDepth = 1;

        for (int i = openBraceIndex + 1; i < text.Length; i++)
        {
            char c = text[i];

            // Skip single-line comments
            if (c == SingleLineCommentStart && i + 1 < text.Length && text[i + 1] == SingleLineCommentStart)
            {
                i = SkipSingleLineComment(text, i);
                continue;
            }

            // Skip multi-line comments
            if (c == SingleLineCommentStart && i + 1 < text.Length && text[i + 1] == MultiLineCommentEnd)
            {
                i = SkipMultiLineComment(text, i);
                continue;
            }

            if (c == OpenBrace)
            {
                braceDepth++;
            }
            else if (c == CloseBrace)
            {
                braceDepth--;

                if (braceDepth == 0)
                {
                    return i;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// Skips a single-line comment and returns the index of the last character in the comment.
    /// </summary>
    private static int SkipSingleLineComment(ReadOnlySpan<char> text, int startIndex)
    {
        for (int i = startIndex + 2; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                return i;
            }
        }

        return text.Length - 1;
    }

    /// <summary>
    /// Skips a multi-line comment and returns the index of the last character in the comment.
    /// </summary>
    private static int SkipMultiLineComment(ReadOnlySpan<char> text, int startIndex)
    {
        for (int i = startIndex + 2; i < text.Length - 1; i++)
        {
            if (text[i] == MultiLineCommentEnd && text[i + 1] == SingleLineCommentStart)
            {
                return i + 1;
            }
        }

        return text.Length - 1;
    }

    #endregion
}
