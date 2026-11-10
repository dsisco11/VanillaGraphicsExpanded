using System;
using System.Linq;
using System.Text;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.Rendering.Shadows.Geometry;

/// <summary>Adds baseline page routing after the native shader's final material deformation.</summary>
internal static class ShadowLayerStages
{
    /// <summary>Prepared vertex, geometry and fragment sources; tessellation is generated from the returned vertex source.</summary>
    internal sealed record Sources(string Vertex, string Geometry, string Fragment);

    #region Public API
    /// <summary>Preserves native material code and transports a flat page reference through the final geometry stage.</summary>
    internal static Sources Prepare(string vertex, string fragment)
    {
        var tree = SyntaxTree.Parse(vertex, GlslSchema.Instance);
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        if (tree.Select(main).Count() != 1) throw new NotSupportedException("Shadow vertex stage requires one main function.");
        if (vertex.Contains("vge_pageIndex", StringComparison.Ordinal))
            throw new NotSupportedException("Shadow source is already page-routed.");
        tree.CreateEditor().InsertBefore(main, """
            layout(location = 10) in vec4 vge_pageMatrix0;
            layout(location = 11) in vec4 vge_pageMatrix1;
            layout(location = 12) in vec4 vge_pageMatrix2;
            layout(location = 13) in vec4 vge_pageMatrix3;
            layout(location = 14) in uvec4 vge_pageDestination;
            flat out vec4 vge_pageColumn0;
            flat out vec4 vge_pageColumn1;
            flat out vec4 vge_pageColumn2;
            flat out vec4 vge_pageColumn3;
            flat out uint vge_pageIndex;
            flat out uint vge_pageLayer;

            """).InsertBefore(main.InnerEnd("body"), """

            vge_pageColumn0 = vge_pageMatrix0;
            vge_pageColumn1 = vge_pageMatrix1;
            vge_pageColumn2 = vge_pageMatrix2;
            vge_pageColumn3 = vge_pageMatrix3;
            vge_pageIndex = vge_pageDestination.z;
            vge_pageLayer = vge_pageDestination.x;
            """).Commit();

        var declarations = new StringBuilder();
        var copies = new StringBuilder();
        var aliases = new StringBuilder();
        int index = 0;
        // Carry exactly the source-owned interface, including conditional declarations.
        // No material positions, normals or UVs are recalculated by the routing stage.
        foreach (var node in tree.Select(Query.AnyOf(Query.Syntax<GlDirectiveNode>(),
            Query.Syntax<GlStageIoNode>(), Query.Syntax<GlInterfaceBlockHeaderNode>())))
        {
            if (node is GlDirectiveNode directive)
            {
                if (directive.Name is "version" or "line" or "extension") continue;
                declarations.AppendLine(directive.ToText());
                continue;
            }
            if (node is GlInterfaceBlockHeaderNode block && block.Storage == GlInterfaceStorage.Output)
                throw new NotSupportedException("Shadow routing requires declared scalar/vector stage outputs.");
            if (node is not GlStageIoNode output || output.Storage != GlInterfaceStorage.Output) continue;
            string type = output.ValueType, name = output.Name, interpolation = output.Interpolation;
            if (type is not ("float" or "vec2" or "vec3" or "vec4" or "int" or "uint")
                || output.ToText().Contains('[') || interpolation is not ("" or "smooth" or "flat"))
                throw new NotSupportedException("Unsupported shadow stage output: " + output.ToText());
            if (type is "int" or "uint" && interpolation != "flat")
                throw new NotSupportedException("Integer page/material outputs must be flat.");
            string qualifier = interpolation.Length == 0 ? "" : interpolation + " ";
            string marker = "VGE_LAYER_OUTPUT_" + index++;
            declarations.AppendLine($"#define {marker} 1\n{qualifier}in {type} {name}[];\n{qualifier}out {type} routed_{name};");
            copies.AppendLine($"#if defined({marker})\nrouted_{name} = {name}[vertex];\n#endif");
            if (name != "vge_pageIndex") aliases.AppendLine($"#define {name} routed_{name}");
        }
        string geometry = """
            #version 450 core
            layout(triangles) in;
            layout(triangle_strip, max_vertices = 3) out;

            """ + declarations + """

            void main() {
                uint page = vge_pageIndex[0];
                // A malformed primitive cannot route vertices into different layers.
                if (vge_pageIndex[1] != page || vge_pageIndex[2] != page) return;
                mat4 projection = mat4(vge_pageColumn0[0], vge_pageColumn1[0], vge_pageColumn2[0], vge_pageColumn3[0]);
                for (int vertex = 0; vertex < 3; ++vertex) {
            """ + copies + """
                    gl_Layer = int(vge_pageLayer[0]);
                    gl_Position = projection * gl_in[vertex].gl_Position;
                    EmitVertex();
                }
                EndPrimitive();
            }
            """;
        // Prefix aliases after #version, preserving the native fragment's alpha/material
        // behavior. Preprocessing renames uses and declarations consistently.
        int end = fragment.IndexOf('\n');
        if (!fragment.TrimStart().StartsWith("#version", StringComparison.Ordinal) || end < 0)
            throw new NotSupportedException("Shadow fragment source requires an explicit version.");
        return new(tree.ToText(), geometry, fragment.Insert(end + 1, aliases.ToString()));
    }
    #endregion
}
