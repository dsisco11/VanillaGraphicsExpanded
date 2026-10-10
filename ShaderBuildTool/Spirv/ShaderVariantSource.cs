using TinyTokenizer.Ast;
using VanillaGraphicsExpanded;
using System.Text;
using VanillaGraphicsExpanded.Rendering.Contracts;

namespace ShaderBuildTool.Spirv;

/// <summary>Expands imports and emits the configuration selected by the shared typed resolver.</summary>
internal sealed class ShaderVariantSource
{
    private readonly string root;
    private readonly string domain;
    #region Public API
    /// <summary>Uses the asset root and domain for shared AST import processing.</summary>
    public ShaderVariantSource(string assetsRoot, string assetDomain)
    {
        root = Path.GetFullPath(assetsRoot); domain = assetDomain;
    }

    /// <summary>Resolves imports beneath the assets root; GLSL guards still govern duplicate declarations.</summary>
    public string Expand(string relative) => new ShaderSourcePreprocessor(root, domain).Expand(relative).Text;

    /// <summary>Inserts typed configuration before imported defaults, preserving shared availability and stable IDs.</summary>
    public string Emit(string source, ShaderStageSelection selection)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var version = Query.Syntax<GlDirectiveNode>().Named("version");
        if (tree.Select(version).Count() != 1)
            throw new InvalidOperationException($"Stage '{selection.Stage.Identity}' must contain exactly one #version directive.");
        // Preserve the authored language baseline so compilation catches unsupported features.
        // Compute and other advanced stages declare their higher requirements in their source.
        // The generated SPIR-V interface uses explicit resource bindings and varying/uniform
        // locations. Enable those contracts without raising the authored GLSL language version.
        var header = new StringBuilder("\n#extension GL_ARB_shading_language_420pack : require\n"
            + "#extension GL_ARB_separate_shader_objects : require\n"
            + "#extension GL_ARB_explicit_uniform_location : require\n"
            + "#extension GL_EXT_control_flow_attributes : require\n");
        foreach (var pair in selection.Stage.FixedDefines.OrderBy(p => p.Key, StringComparer.Ordinal))
            header.AppendLine($"#define {pair.Key} {pair.Value.MacroLiteral}");
        foreach (var pair in selection.Structural)
            header.AppendLine($"#define {pair.Key} {pair.Value.MacroLiteral}");
        // The resolver supplies active IDs using the same conditions as runtime argument projection.
        // Compiler defaults are declaration defaults, never an individual user's numeric selection.
        var active = selection.Specializations.Select(s => s.Id).ToHashSet();
        foreach (var constant in selection.Stage.Specializations.Where(s => active.Contains(s.Id)))
        {
            string symbol = "vgeSpecialization" + constant.Id;
            var value = constant.Option.Default;
            header.AppendLine($"layout(constant_id = {constant.Id}) const {value.GlslType} {symbol} = {value.GlslLiteral};");
            header.AppendLine($"#define {constant.Option.Name} {symbol}");
        }
        tree.CreateEditor().InsertAfter(version, header.ToString()).Commit();
        return DerivedGlobalConstants.Apply(tree.ToText());
    }
    #endregion
}
