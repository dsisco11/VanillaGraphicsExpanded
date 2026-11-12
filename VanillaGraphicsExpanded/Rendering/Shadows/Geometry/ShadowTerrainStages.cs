using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Adds virtual projection and subdivision interfaces to authored terrain material stages.</summary>
internal static class ShadowTerrainStages
{
    #region Public API
    /// <summary>Wraps the authored terrain position output after geometry decoding and shared deformation.</summary>
    internal static string Vertex(string source, bool displaced = false)
    {
        // Owned vertex sources declare their complete ordinary/subdivision interface.
        // No native shader patch or broad uniform stripping participates in this path.
        string selected = ShadowVirtualStages.SelectViews(source);
        return displaced ? selected : ShadowVirtualStages.FinalProducer(selected, "vge_shadowPosition");
    }

    /// <summary>Preserves atlas rejection while making its temporary material color private.</summary>
    internal static string Fragment(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var outputs = tree.Select(Query.Syntax<GlStageIoNode>()).OfType<GlStageIoNode>()
            .Where(node => node.Storage == GlInterfaceStorage.Output).ToArray();
        if (outputs.Length != 1 || outputs[0].Name != "vge_materialCoverage" || outputs[0].ValueType != "vec4")
            throw new NotSupportedException("Shadow material color declaration changed.");
        // Only the integer images are shadow destinations. The material color still
        // carries sampled alpha for its exact rejection rule, without an attachment write.
        tree.CreateEditor().Replace(outputs[0], "vec4 vge_materialCoverage;\n").Commit();
        return ShadowVirtualStages.Fragment(tree.ToText());
    }

    /// <summary>Rebases the completed native displacement before the shared final clipping and virtual projection wrapper.</summary>
    internal static string Evaluation(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
        tree.CreateEditor().Replace(main.NameNode, "vgeShadowDisplacementMain")
            .InsertAfter(main, "\nvoid main() {\nvgeShadowDisplacementMain();\n" +
            "vge_shadowPosition=vge_shadowOriginTransform*vge_shadowPosition;\n}\n").Commit();
        return ShadowVirtualStages.FinalProducer(tree.ToText(), "vge_shadowPosition");
    }

    /// <summary>Adapts owned stage symbols at the shared native tessellation generator boundary.</summary>
    internal static PBR.Tessellation.TerrainTessellationStages.Sources Subdivision(string vertex,
        PBR.Tessellation.TerrainTessellationStages.Sources templates)
    {
        var shared = PBR.Tessellation.TerrainTessellationStages.Generate(
            TranslateSubdivision(vertex, owned: false), new(
                TranslateSubdivision(templates.Control, owned: false),
                TranslateSubdivision(templates.Evaluation, owned: false)));
        return new(TranslateSubdivision(shared.Control, owned: true), TranslateSubdivision(shared.Evaluation, owned: true));
    }
    #endregion

    #region Private
    /// <summary>Keeps native interface spellings confined to the existing terrain-generator adapter.</summary>
    private static string TranslateSubdivision(string source, bool owned)
    {
        var symbols = new (string Shared, string Owned)[]
        {
            ("worldPos", "vge_materialPosition"), ("normal", "vge_shadowNormal"),
            ("uv", "vge_shadowUv"), ("renderFlags", "vge_shadowVertexFlags"),
            ("mvpMatrix", "vge_materialTransform")
        };
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        var editor = tree.CreateEditor();
        // Rename parsed identifier tokens, including generated varying/guard names.
        // Member selectors belong to their object type, not this stage interface.
        foreach (var pair in symbols)
            foreach (string prefix in new[] { "", "tc_", "VGE_TESS_HAS_" })
                foreach (var identifier in tree.Select(Query.Ident(prefix + (owned ? pair.Shared : pair.Owned))))
                {
                    if (IsMemberIdentifier(identifier)) continue;
                    editor.Replace(identifier, prefix + (owned ? pair.Owned : pair.Shared));
                }
        editor.Commit();
        return tree.ToText();
    }

    /// <summary>Preserves member names owned by structure declarations and member-selection expressions.</summary>
    private static bool IsMemberIdentifier(SyntaxNode identifier)
    {
        if (identifier.PreviousSibling() is { } previous && Query.Symbol(".").Matches(previous)) return true;
        // Structure fields are not stage globals. Their brace block follows either
        // the struct keyword or the declared structure type name in the parsed tree.
        for (var parent = identifier.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is not SyntaxBlock { Kind: NodeKind.BraceBlock }) continue;
            var predecessor = parent.PreviousSibling();
            if (predecessor is not null && (Query.Ident("struct").Matches(predecessor) ||
                predecessor.PreviousSibling() is { } keyword && Query.Ident("struct").Matches(keyword))) return true;
        }
        return false;
    }
    #endregion
}
