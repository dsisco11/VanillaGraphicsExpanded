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
    #region Construction and import expansion
    /// <summary>Uses the asset root and domain for shared AST import processing.</summary>
    public ShaderVariantSource(string assetsRoot, string assetDomain)
    {
        root = Path.GetFullPath(assetsRoot); domain = assetDomain;
    }

    /// <summary>Resolves imports beneath the assets root; GLSL guards still govern duplicate declarations.</summary>
    public string Expand(string relative) => new ShaderSourcePreprocessor(root, domain).Expand(relative);
    #endregion

    #region Configuration and emission
    /// <summary>Inserts typed configuration before imported defaults, preserving shared availability and stable IDs.</summary>
    public string Emit(string source, ShaderStageSelection selection)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var version = Query.Syntax<GlDirectiveNode>().Named("version");
        if (tree.Select(version).Count() != 1)
            throw new InvalidOperationException($"Stage '{selection.Stage.Identity}' must contain exactly one #version directive.");
        tree.CreateEditor().Replace(version, "#version 450 core\n").Commit();
        var header = new StringBuilder("\n#extension GL_EXT_control_flow_attributes : require\n");
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
