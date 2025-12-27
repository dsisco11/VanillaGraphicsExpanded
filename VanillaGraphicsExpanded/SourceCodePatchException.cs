using System;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Exception thrown when source code patching fails due to parse errors or missing expected constructs.
/// </summary>
public class SourceCodePatchException : Exception
{
    /// <summary>
    /// The character position in the source where the error occurred, or -1 if not applicable.
    /// </summary>
    public long Position { get; }

    /// <summary>
    /// The name of the source being patched, if available.
    /// </summary>
    public string? SourceName { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="SourceCodePatchException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public SourceCodePatchException(string message)
        : base(message)
    {
        Position = -1;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SourceCodePatchException"/> with position information.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The character position where the error occurred.</param>
    public SourceCodePatchException(string message, long position)
        : base($"{message} (at position {position})")
    {
        Position = position;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SourceCodePatchException"/> with source name and position.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="sourceName">The name of the source being patched.</param>
    /// <param name="position">The character position where the error occurred.</param>
    public SourceCodePatchException(string message, string sourceName, long position = -1)
        : base(position >= 0
            ? $"[{sourceName}] {message} (at position {position})"
            : $"[{sourceName}] {message}")
    {
        Position = position;
        SourceName = sourceName;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="SourceCodePatchException"/> with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public SourceCodePatchException(string message, Exception innerException)
        : base(message, innerException)
    {
        Position = -1;
    }
}
