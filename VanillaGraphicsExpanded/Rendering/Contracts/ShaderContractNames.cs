using System;
using System.Linq;

namespace VanillaGraphicsExpanded.Rendering.Contracts;

/// <summary>Validates identifiers and portable relative asset paths at declaration boundaries.</summary>
internal static class ShaderContractNames
{
    #region Declaration validation
    /// <summary>Rejects preprocessor/compiler-reserved identifiers and invalid GLSL names.</summary>
    public static void ValidateIdentifier(string name)
    {
        if (string.IsNullOrEmpty(name) || !((name[0] is >= 'A' and <= 'Z' or >= 'a' and <= 'z') || name[0] == '_') ||
            name.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9') && c != '_') || name.StartsWith("gl_", StringComparison.Ordinal) ||
            name.IndexOf("__", StringComparison.Ordinal) >= 0 || name.StartsWith("GL_", StringComparison.Ordinal) || name == "defined")
            throw new ArgumentException($"Invalid or reserved shader identifier '{name}'.");
    }

    /// <summary>Requires canonical relative paths to make collision checks independent of the host filesystem.</summary>
    public static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains('\\') || path.Contains(':') ||
            path.Split('/').Any(p => p is "" or "." or ".." || p.Any(c => !(c is >= 'A' and <= 'Z' or >= 'a' and <= 'z' or >= '0' and <= '9') && c is not '_' and not '-' and not '.')))
            throw new ArgumentException($"Invalid shader identity/asset path '{path}'.");
    }
    #endregion
}
