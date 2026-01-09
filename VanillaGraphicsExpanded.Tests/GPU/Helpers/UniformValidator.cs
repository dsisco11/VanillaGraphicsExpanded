using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using TinyTokenizer.Ast;
using Xunit;

namespace VanillaGraphicsExpanded.Tests.GPU.Helpers;

/// <summary>
/// Validates that all required shader uniforms are set before rendering.
/// Uses GlslSchema AST parsing to extract uniform declarations from processed shader source.
/// </summary>
public static class UniformValidator
{
    /// <summary>
    /// Extracts all uniform declarations from processed GLSL source code.
    /// </summary>
    /// <param name="processedSource">GLSL source with @import directives resolved.</param>
    /// <returns>List of uniform information (name, type, array size if applicable).</returns>
    public static List<UniformInfo> ExtractUniforms(string processedSource)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(processedSource);

        var uniforms = new List<UniformInfo>();

        try
        {
            var tree = SyntaxTree.Parse(processedSource, GlslSchema.Instance);

            // Use Query.Syntax<T>() to find all GlUniformNode instances
            var uniformQuery = Query.Syntax<GlUniformNode>()
                .Where(node => node is GlUniformNode u && u.UniformKeyword.Text == "uniform");
            var uniformNodes = uniformQuery.Select(tree).OfType<GlUniformNode>();

            foreach (var uniformNode in uniformNodes)
            {
                uniforms.Add(new UniformInfo(
                    uniformNode.Name,
                    uniformNode.TypeName,
                    uniformNode.ArraySize,
                    IsSamplerType(uniformNode.TypeName)));
            }
        }
        catch (Exception ex)
        {
            // If parsing fails, return empty list with a warning
            System.Diagnostics.Debug.WriteLine($"[UniformValidator] Failed to parse shader source: {ex.Message}");
        }

        return uniforms;
    }

    /// <summary>
    /// Validates that all required uniforms have been set.
    /// Sampler uniforms are excluded by default (they require texture binding, not GL.Uniform calls).
    /// </summary>
    /// <param name="uniforms">List of uniforms extracted from shader source.</param>
    /// <param name="setUniformNames">Names of uniforms that have been set in the test.</param>
    /// <param name="excludeSamplers">If true, sampler uniforms are not required to be in setUniformNames.</param>
    /// <exception cref="Xunit.Sdk.XunitException">Thrown when required uniforms are missing.</exception>
    public static void ValidateUniformsSet(
        IEnumerable<UniformInfo> uniforms,
        IEnumerable<string> setUniformNames,
        bool excludeSamplers = true)
    {
        var setNames = new HashSet<string>(setUniformNames, StringComparer.Ordinal);
        var missingUniforms = new List<UniformInfo>();

        foreach (var uniform in uniforms)
        {
            // Skip samplers if requested
            if (excludeSamplers && uniform.IsSampler)
                continue;

            if (!setNames.Contains(uniform.Name))
            {
                missingUniforms.Add(uniform);
            }
        }

        if (missingUniforms.Count > 0)
        {
            var message = FormatMissingUniformsMessage(missingUniforms);
            Assert.Fail(message);
        }
    }

    /// <summary>
    /// Validates uniforms and returns the result without throwing.
    /// </summary>
    /// <param name="uniforms">List of uniforms extracted from shader source.</param>
    /// <param name="setUniformNames">Names of uniforms that have been set in the test.</param>
    /// <param name="excludeSamplers">If true, sampler uniforms are not required to be in setUniformNames.</param>
    /// <returns>Validation result with status and any missing uniforms.</returns>
    public static UniformValidationResult Validate(
        IEnumerable<UniformInfo> uniforms,
        IEnumerable<string> setUniformNames,
        bool excludeSamplers = true)
    {
        var setNames = new HashSet<string>(setUniformNames, StringComparer.Ordinal);
        var missingUniforms = new List<UniformInfo>();
        var extraUniforms = new List<string>();

        // Check for missing uniforms
        foreach (var uniform in uniforms)
        {
            if (excludeSamplers && uniform.IsSampler)
                continue;

            if (!setNames.Contains(uniform.Name))
            {
                missingUniforms.Add(uniform);
            }
        }

        // Check for extra uniforms (set but not declared - might indicate typos)
        var declaredNames = uniforms.Select(u => u.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var setName in setUniformNames)
        {
            if (!declaredNames.Contains(setName))
            {
                extraUniforms.Add(setName);
            }
        }

        bool isValid = missingUniforms.Count == 0;
        string? message = isValid ? null : FormatMissingUniformsMessage(missingUniforms);

        return new UniformValidationResult(isValid, message, missingUniforms, extraUniforms);
    }

    /// <summary>
    /// Generates a uniform checklist report for documentation and test setup verification.
    /// </summary>
    /// <param name="uniforms">List of uniforms extracted from shader source.</param>
    /// <returns>Formatted string listing all uniforms grouped by type.</returns>
    public static string GenerateUniformChecklist(IEnumerable<UniformInfo> uniforms)
    {
        var sb = new StringBuilder();
        var grouped = uniforms
            .GroupBy(u => u.IsSampler ? "Samplers (bind to texture units)" : "Values (set via GL.Uniform*)")
            .OrderByDescending(g => g.Key); // Values first, then Samplers

        foreach (var group in grouped)
        {
            sb.AppendLine(group.Key + ":");
            foreach (var uniform in group.OrderBy(u => u.Name))
            {
                string arrayPart = uniform.ArraySize.HasValue ? $"[{uniform.ArraySize}]" : "";
                sb.AppendLine($"  [ ] {uniform.Type} {uniform.Name}{arrayPart}");
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Determines if a GLSL type name is a sampler type.
    /// </summary>
    /// <param name="typeName">GLSL type name (e.g., "sampler2D", "vec3").</param>
    /// <returns>True if the type is a sampler or image type.</returns>
    public static bool IsSamplerType(string typeName)
    {
        return GlslKeywords.SamplerTypes.Contains(typeName) ||
               GlslKeywords.ImageTypes.Contains(typeName);
    }

    /// <summary>
    /// Gets the expected GL.Uniform* method for a GLSL type.
    /// </summary>
    /// <param name="typeName">GLSL type name.</param>
    /// <returns>Name of the corresponding GL.Uniform method.</returns>
    public static string GetUniformMethodName(string typeName)
    {
        return typeName switch
        {
            "float" => "Uniform1",
            "int" => "Uniform1",
            "uint" => "Uniform1",
            "bool" => "Uniform1",
            "vec2" => "Uniform2",
            "ivec2" => "Uniform2",
            "uvec2" => "Uniform2",
            "bvec2" => "Uniform2",
            "vec3" => "Uniform3",
            "ivec3" => "Uniform3",
            "uvec3" => "Uniform3",
            "bvec3" => "Uniform3",
            "vec4" => "Uniform4",
            "ivec4" => "Uniform4",
            "uvec4" => "Uniform4",
            "bvec4" => "Uniform4",
            "mat2" or "mat2x2" => "UniformMatrix2",
            "mat3" or "mat3x3" => "UniformMatrix3",
            "mat4" or "mat4x4" => "UniformMatrix4",
            "mat2x3" => "UniformMatrix2x3",
            "mat2x4" => "UniformMatrix2x4",
            "mat3x2" => "UniformMatrix3x2",
            "mat3x4" => "UniformMatrix3x4",
            "mat4x2" => "UniformMatrix4x2",
            "mat4x3" => "UniformMatrix4x3",
            _ when IsSamplerType(typeName) => "Uniform1 (texture unit)",
            _ => "Unknown"
        };
    }

    #region Private Helpers

    private static string FormatMissingUniformsMessage(List<UniformInfo> missingUniforms)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Missing {missingUniforms.Count} required uniform(s):");

        foreach (var uniform in missingUniforms.OrderBy(u => u.Name))
        {
            string arrayPart = uniform.ArraySize.HasValue ? $"[{uniform.ArraySize}]" : "";
            string method = GetUniformMethodName(uniform.Type);
            sb.AppendLine($"  - {uniform.Type} {uniform.Name}{arrayPart}  (use GL.{method})");
        }

        return sb.ToString();
    }

    #endregion
}

/// <summary>
/// Information about a GLSL uniform declaration.
/// </summary>
/// <param name="Name">Uniform name as declared in shader.</param>
/// <param name="Type">GLSL type name (e.g., "vec3", "sampler2D", "mat4").</param>
/// <param name="ArraySize">Array size if declared as array, null otherwise.</param>
/// <param name="IsSampler">True if this is a sampler or image type.</param>
public readonly record struct UniformInfo(string Name, string Type, int? ArraySize, bool IsSampler)
{
    /// <summary>
    /// Returns a string representation of the uniform declaration.
    /// </summary>
    public override string ToString()
    {
        string arrayPart = ArraySize.HasValue ? $"[{ArraySize}]" : "";
        return $"uniform {Type} {Name}{arrayPart}";
    }
}

/// <summary>
/// Result of uniform validation.
/// </summary>
/// <param name="IsValid">True if all required uniforms are set.</param>
/// <param name="Message">Error message if validation failed, null otherwise.</param>
/// <param name="MissingUniforms">List of uniforms declared but not set.</param>
/// <param name="ExtraUniforms">List of uniform names set but not declared (potential typos).</param>
public readonly record struct UniformValidationResult(
    bool IsValid,
    string? Message,
    IReadOnlyList<UniformInfo> MissingUniforms,
    IReadOnlyList<string> ExtraUniforms);
