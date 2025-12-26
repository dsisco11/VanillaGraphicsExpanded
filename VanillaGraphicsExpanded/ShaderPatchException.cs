using System;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Exception thrown when shader source patching fails due to parse errors or missing expected constructs.
/// </summary>
public class ShaderPatchException : Exception
{
    /// <summary>
    /// The character position in the source where the error occurred, or -1 if not applicable.
    /// </summary>
    public long Position { get; }

    /// <summary>
    /// The name of the shader being patched, if available.
    /// </summary>
    public string? ShaderName { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="ShaderPatchException"/>.
    /// </summary>
    /// <param name="message">The error message.</param>
    public ShaderPatchException(string message)
        : base(message)
    {
        Position = -1;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ShaderPatchException"/> with position information.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="position">The character position where the error occurred.</param>
    public ShaderPatchException(string message, long position)
        : base($"{message} (at position {position})")
    {
        Position = position;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ShaderPatchException"/> with shader name and position.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="shaderName">The name of the shader being patched.</param>
    /// <param name="position">The character position where the error occurred.</param>
    public ShaderPatchException(string message, string shaderName, long position = -1)
        : base(position >= 0 
            ? $"[{shaderName}] {message} (at position {position})" 
            : $"[{shaderName}] {message}")
    {
        ShaderName = shaderName;
        Position = position;
    }

    /// <summary>
    /// Initializes a new instance of <see cref="ShaderPatchException"/> with an inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public ShaderPatchException(string message, Exception innerException)
        : base(message, innerException)
    {
        Position = -1;
    }
}
