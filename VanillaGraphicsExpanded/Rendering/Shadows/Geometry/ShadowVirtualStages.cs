using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Builds virtual shadow stage interfaces for offline compilation after native material preparation.</summary>
internal static class ShadowVirtualStages
{
    #region Public API
    /// <summary>Adds compact object/view selectors before material varyings are propagated through tessellation.</summary>
    internal static string SelectViews(string vertex)
    {
        var tree = Parse(vertex);
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        tree.CreateEditor().InsertBefore(main, """
            layout(location=10) in uvec2 vge_shadowSelection;
            flat out uint vge_shadowObject;
            flat out uint vge_shadowView;

            """).InsertAfter(main.InnerStart("body"), """

            vge_shadowObject=vge_shadowSelection.x;
            vge_shadowView=vge_shadowSelection.y;
            """ + "\n").Commit();
        return tree.ToText();
    }

    /// <summary>Projects the final deformed origin-relative position and writes all four clipping outputs on every return path.</summary>
    internal static string FinalProducer(string source, string position)
    {
        // Material interface generation must finish first. In particular, gl_PerVertex
        // and gl_ClipDistance are built-ins, never scalar material varyings to interpolate.
        var tree = Parse(source);
        var clips = tree.Select(Query.Syntax<GlStageIoNode>().Named("gl_ClipDistance"))
            .OfType<GlStageIoNode>().ToArray();
        var dimensions = clips.Length == 1 ? clips[0].Children.OfType<SyntaxBlock>()
            .Where(block => block.Kind == NodeKind.BracketBlock).ToArray() : [];
        if (clips.Length != 1 || clips[0].Storage != GlInterfaceStorage.Output || clips[0].ValueType != "float" ||
            dimensions.Length != 1 || dimensions[0].InnerChildren.Count() != 1 ||
            dimensions[0].InnerChildren.Single() is not SyntaxToken { Text: "4" } ||
            tree.Select(Query.Ident("gl_ClipDistance")).Count() != 1 ||
            tree.Select(Query.Ident("gl_CullDistance")).Any())
            throw new NotSupportedException("Virtual shadows require one explicit four-plane clipping output declaration.");
        // A directive can alias an emission builtin without spelling a call here.
        // Inspect parsed identifiers conservatively, while ignoring comments and literals.
        if (tree.Select(Query.AnyOf(Query.Ident("EmitVertex"), Query.Ident("EmitStreamVertex"))).Any())
            throw new NotSupportedException("Geometry emission requires an explicit complete-output shadow variant.");
        if (position is not ("vge_shadowPosition" or "worldPos"))
            throw new ArgumentException("A generated final material position is required.", nameof(position));
        if (tree.Select(Query.Syntax<GlInterfaceBlockHeaderNode>()).OfType<GlInterfaceBlockHeaderNode>()
            .Any(block => block.Storage == GlInterfaceStorage.Output && block.Name == "gl_PerVertex"))
            throw new NotSupportedException("A redeclared final gl_PerVertex block requires an explicit supported variant.");
        return Wrap(tree,
            $$"""
                if (!shadowViewExists(vge_shadowView)) {
                    gl_Position=vec4(0.0,0.0,0.0,1.0);
                    for(int plane=0;plane<4;plane++) gl_ClipDistance[plane]=-1.0;
                    return;
                }
                gl_Position=shadowProject(vge_shadowView,{{position}});
                vec4 distances=shadowClipRectangle(vge_shadowView,gl_Position);
                for(int plane=0;plane<4;plane++) gl_ClipDistance[plane]=distances[plane];
                """);
    }

    /// <summary>Preserves material rejection before guarded atomic publication; no physical layer or extra geometry stage is emitted.</summary>
    internal static string Fragment(string source)
    {
        var tree = Parse(source);
        if (tree.Select(Query.Ident("gl_FragDepth")).Any() ||
            tree.Select(Query.Syntax<GlLayoutNode>()).OfType<GlLayoutNode>()
                .Any(layout => layout.Qualifiers.Any(qualifier => qualifier.Nodes.Count == 1 &&
                    Query.Ident("early_fragment_tests").Matches(qualifier.Nodes[0]))))
            throw new NotSupportedException("Fragment depth replacement or early tests require a separately qualified atomic shadow variant.");
        return Wrap(tree, """
                if (!shadowViewExists(vge_shadowView)) return;
                ShadowWriteView view;
                for(uint index=0u;index<17u;index++)
                    view.key[index]=shadowViewWords(vge_shadowView,6u+index/4u)[index%4u];
                uvec4 limits=shadowViewWords(vge_shadowView,13u);
                view.capacity=limits.x; view.probes=limits.y; view.pageSize=limits.z; view.arrayLayers=limits.w;
                view.batch=shadowViewWords(vge_shadowView,14u).xy;
                ivec2 origin=ivec2(shadowViewWords(vge_shadowView,4u).xy);
                shadowAtomicDepth(view,origin+ivec2(gl_FragCoord.xy),gl_FragCoord.z);
                """);
    }
    #endregion

    #region Private
    /// <summary>Rejects duplicate generation and unsupported entry-point structures before editing a stage.</summary>
    private static SyntaxTree Parse(string source)
    {
        var tree = SyntaxTree.Parse(source, GlslSchema.Instance);
        if (tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).Count() != 1 ||
            tree.Select(Query.Ident("vgeShadowMaterialMain")).Any())
            throw new NotSupportedException("Virtual shadow generation requires one unwrapped main function.");
        return tree;
    }

    /// <summary>Runs the complete material body before final output, including material early returns and fragment discard.</summary>
    private static string Wrap(SyntaxTree tree, string finalOutput)
    {
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
        var editor = tree.CreateEditor();
        var versions = tree.Select(Query.Syntax<GlDirectiveNode>()).OfType<GlDirectiveNode>()
            .Where(node => node.Name == "version").ToArray();
        if (versions.Length > 1) throw new NotSupportedException("A shadow stage requires one GLSL version header.");
        // These are new offline VSM variants: integer images and precise virtual
        // addressing require the established 4.5 source feature set, independently
        // of the original native material's older GLSL version.
        if (versions.Length == 1) editor.Replace(versions[0], "#version 450 core\n");
        editor.Replace(main.NameNode, "vgeShadowMaterialMain")
            .InsertAfter(main, "\nvoid main() {\nvgeShadowMaterialMain();\n" + finalOutput + "\n}\n").Commit();
        return (versions.Length == 0 ? "#version 450 core\n" : "") + tree.ToText();
    }
    #endregion
}
