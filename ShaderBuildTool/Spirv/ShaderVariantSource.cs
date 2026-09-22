using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using System.Text;
using System.Text.RegularExpressions;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Expands asset imports and separates finite preprocessing variants from numeric specialization inputs.</summary>
internal sealed class ShaderVariantSource
{
    private readonly string root;
    private readonly string domain;
    #region Construction and import expansion
    /// <summary>Uses the asset root and domain for shared AST import processing.</summary>
    public ShaderVariantSource(string assetsRoot, string assetDomain)
    {
        root = Path.GetFullPath(assetsRoot); domain = assetDomain;
    }

    /// <summary>Resolves imports beneath the assets root; GLSL guards still govern duplicate declarations.</summary>
    public string Expand(string relative)
    {
        return new ShaderSourcePreprocessor(root, domain).Expand(relative);
    }
    #endregion

    #region Configuration and emission
    /// <summary>Emits an OpenGL SPIR-V source with stable specialization IDs and selected structural macros.</summary>
    public string Emit(string source, ShaderStageContract contract, IReadOnlyDictionary<string, string?> structural)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var version = Query.Syntax<GlDirectiveNode>().Named("version");
        if (tree.Select(version).Count() != 1)
            throw new InvalidOperationException("Shader must contain exactly one #version directive.");
        tree.CreateEditor().Replace(version, "#version 450 core\n").Commit();
        var header = new StringBuilder("\n#extension GL_EXT_control_flow_attributes : require\n#define VGE_SPIRV_BUILD 1\n");
        foreach (var pair in structural) header.AppendLine($"#define {pair.Key} {pair.Value}");
        foreach (var constant in contract.Constants(structural))
        {
            string symbol = "vgeSpecialization" + constant.Id;
            header.AppendLine($"layout(constant_id = {constant.Id}) const {constant.Type} {symbol} = {constant.Default};");
            header.AppendLine($"#define {constant.Name} {symbol}");
        }
        tree.CreateEditor().InsertAfter(version, header.ToString()).Commit();
        source = tree.ToText();
        // Derived global const expressions may call functions that GLSL does not permit in specialization initializers.
        // Keep the specialization declarations constant; derived globals are initialized when the entry point executes.
        int depth = 0;
        return Regex.Replace(source, @"/\*[\s\S]*?\*/|//[^\r\n]*|\{|\}|\bconst\b", match =>
        {
            if (match.Value == "{") depth++;
            else if (match.Value == "}") depth--;
            else if (match.Value == "const" && depth == 0)
            {
                int line = source.LastIndexOf('\n', Math.Max(0, match.Index - 1)) + 1;
                int end = source.IndexOf(';', match.Index);
                if (!source[line..match.Index].Contains("constant_id") && end >= 0 && source[match.Index..end].Contains('(')) return "";
            }
            return match.Value;
        });
    }
    #endregion
}


