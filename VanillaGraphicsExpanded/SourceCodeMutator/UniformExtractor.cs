using System;
using System.Collections.Generic;
using System.Linq;

using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded;

/// <summary>
/// Represents a uniform declaration extracted from GLSL source code.
/// </summary>
/// <param name="Name">The uniform variable name.</param>
/// <param name="TypeName">The GLSL type name (e.g., "mat4", "sampler2D").</param>
/// <param name="ArraySize">The array size if this is an array uniform, otherwise null.</param>
public readonly record struct UniformDeclaration(string Name, string TypeName, int? ArraySize = null)
{
    /// <summary>
    /// Returns true if this is an array uniform declaration.
    /// </summary>
    public bool IsArray => ArraySize.HasValue;

    /// <inheritdoc/>
    public override string ToString() =>
        IsArray ? $"uniform {TypeName} {Name}[{ArraySize}]" : $"uniform {TypeName} {Name}";
}

/// <summary>
/// Utility class for extracting uniform declarations from GLSL shader source code.
/// Uses TinyTokenizer's AST parsing with the <see cref="GlslSchema"/> to find all
/// uniform declarations and return them as structured data.
/// </summary>
public static class UniformExtractor
{
    /// <summary>
    /// Extracts all uniform declarations from the given GLSL source code.
    /// </summary>
    /// <param name="source">The GLSL shader source code.</param>
    /// <returns>An enumerable of uniform declarations found in the source.</returns>
    /// <remarks>
    /// This method parses the source using <see cref="GlslSchema.Instance"/> and
    /// extracts all <see cref="GlUniformNode"/> matches. Duplicate uniform names
    /// are deduplicated (first occurrence wins).
    /// </remarks>
    public static IEnumerable<UniformDeclaration> ExtractUniforms(string source)
    {
        if (string.IsNullOrWhiteSpace(source))
            yield break;

        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);

        foreach (var uniform in ExtractUniforms(tree))
        {
            yield return uniform;
        }
    }

    /// <summary>
    /// Extracts all uniform declarations from a pre-parsed syntax tree.
    /// </summary>
    /// <param name="tree">The syntax tree parsed with <see cref="GlslSchema.Instance"/>.</param>
    /// <returns>An enumerable of uniform declarations found in the tree.</returns>
    /// <exception cref="ArgumentNullException">Thrown if <paramref name="tree"/> is null.</exception>
    public static IEnumerable<UniformDeclaration> ExtractUniforms(SyntaxTree tree)
    {
        ArgumentNullException.ThrowIfNull(tree);

        // Use Query.Syntax<T>() to find all GlUniformNode instances
        var uniformQuery = Query.Syntax<GlUniformNode>();
        var uniformNodes = uniformQuery.Select(tree).OfType<GlUniformNode>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var node in uniformNodes)
        {
            var name = node.Name;

            // Deduplicate by name (first occurrence wins)
            if (!seen.Add(name))
                continue;

            yield return new UniformDeclaration(name, node.TypeName, node.ArraySize);
        }
    }

    /// <summary>
    /// Extracts all uniform declarations from the given GLSL source code as a list.
    /// </summary>
    /// <param name="source">The GLSL shader source code.</param>
    /// <returns>A list of uniform declarations found in the source.</returns>
    public static List<UniformDeclaration> ExtractUniformsList(string source) =>
        ExtractUniforms(source).ToList();

    /// <summary>
    /// Extracts uniform names only from the given GLSL source code.
    /// </summary>
    /// <param name="source">The GLSL shader source code.</param>
    /// <returns>A set of unique uniform names found in the source.</returns>
    public static HashSet<string> ExtractUniformNames(string source) =>
        ExtractUniforms(source).Select(u => u.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// Extracts uniform names from multiple GLSL source files (e.g., vertex + fragment shader).
    /// </summary>
    /// <param name="sources">The GLSL shader source codes to analyze.</param>
    /// <returns>A set of unique uniform names found across all sources.</returns>
    public static HashSet<string> ExtractUniformNamesFromMultiple(params string[] sources)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);

        foreach (var source in sources)
        {
            foreach (var uniform in ExtractUniforms(source))
            {
                names.Add(uniform.Name);
            }
        }

        return names;
    }

    /// <summary>
    /// Compares declared uniforms against actual OpenGL uniform locations.
    /// </summary>
    /// <param name="source">The GLSL shader source code.</param>
    /// <param name="getUniformLocation">
    /// A function that returns the uniform location for a given name.
    /// Returns -1 if the uniform is not found/optimized out.
    /// </param>
    /// <returns>
    /// A tuple containing:
    /// - Active: uniforms declared in source AND found by OpenGL (location >= 0)
    /// - OptimizedOut: uniforms declared in source but NOT found by OpenGL (location == -1)
    /// </returns>
    public static (List<UniformDeclaration> Active, List<UniformDeclaration> OptimizedOut)
        CompareWithGLLocations(string source, Func<string, int> getUniformLocation)
    {
        var active = new List<UniformDeclaration>();
        var optimizedOut = new List<UniformDeclaration>();

        foreach (var uniform in ExtractUniforms(source))
        {
            var location = getUniformLocation(uniform.Name);
            if (location >= 0)
            {
                active.Add(uniform);
            }
            else
            {
                optimizedOut.Add(uniform);
            }
        }

        return (active, optimizedOut);
    }
}
