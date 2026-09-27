using System;
using System.Text;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Prepares matching tessellation sources from the terrain patch's typed output declarations.</summary>
internal static class TerrainTessellationStages
{
    /// <summary>Immutable stage templates prepared before engine compilation.</summary>
    internal sealed record Sources(string Control, string Evaluation);

    #region Interface preparation
    /// <summary>Retains source preprocessing guards and derives interpolation from declarations, without driver reflection.</summary>
    internal static Sources Generate(string vertexSource)
    {
        var tree = SyntaxTree.Parse(vertexSource, GlslSchema.Instance);
        var control = new StringBuilder();
        var evaluation = new StringBuilder();
        var copies = new StringBuilder();
        var interpolations = new StringBuilder();
        int index = 0;
        // Copy directives in their original order so declarations select exactly the same engine
        // variants. Presence markers also guard the corresponding statements in each stage main.
        foreach (var node in tree.Select(Query.AnyOf(Query.Syntax<GlDirectiveNode>(),
            Query.Syntax<GlStageIoNode>(), Query.Syntax<GlInterfaceBlockHeaderNode>())))
        {
            if (node is GlDirectiveNode directive)
            {
                if (directive.Name is "version" or "line") continue;
                control.AppendLine(directive.ToText());
                evaluation.AppendLine(directive.ToText());
                continue;
            }
            if (node is GlInterfaceBlockHeaderNode block && block.Storage == GlInterfaceStorage.Output)
                throw new NotSupportedException($"Terrain output blocks are not supported: {block.Name}.");
            if (node is not GlStageIoNode output || output.Storage != GlInterfaceStorage.Output) continue;
            string type = output.ValueType;
            string qualifier = output.Interpolation;
            if (type is not ("float" or "vec2" or "vec3" or "vec4" or "int" or "uint")
                || output.ToText().Contains('[') || qualifier is not ("" or "smooth" or "flat"))
                throw new NotSupportedException($"Unsupported terrain output declaration: {output.ToText()}.");
            if (type is "int" or "uint" && qualifier != "flat")
                throw new NotSupportedException($"Integer terrain output must be flat: {output.Name}.");
            string marker = $"VGE_TESS_OUTPUT_{index++}";
            string name = output.Name;
            string interpolation = qualifier == "" ? "" : qualifier + " ";
            control.AppendLine($$"""
                #define {{marker}} 1
                {{interpolation}}in {{type}} {{name}}[];
                {{interpolation}}out {{type}} tc_{{name}}[];
                """);
            evaluation.AppendLine($$"""
                #define {{marker}} 1
                {{interpolation}}in {{type}} tc_{{name}}[];
                {{interpolation}}out {{type}} {{name}};
                """);
            copies.AppendLine($$"""
                #if defined({{marker}})
                tc_{{name}}[gl_InvocationID] = {{name}}[gl_InvocationID];
                #endif
                """);
            string value = qualifier == "flat" ? $"tc_{name}[2]" :
                $"tc_{name}[0] * gl_TessCoord.x + tc_{name}[1] * gl_TessCoord.y + tc_{name}[2] * gl_TessCoord.z";
            interpolations.AppendLine($$"""
                #if defined({{marker}})
                {{name}} = {{value}};
                #endif
                """);
        }
        return new Sources($$"""
            #if VGE_ENABLE_TESSELLATION
            {{control}}
            layout(vertices=3) out;
            void main() {
                gl_out[gl_InvocationID].gl_Position = gl_in[gl_InvocationID].gl_Position;
                {{copies}}
                if (gl_InvocationID == 0) {
                    gl_TessLevelOuter[0] = float(VGE_TESSELLATION_LEVEL);
                    gl_TessLevelOuter[1] = float(VGE_TESSELLATION_LEVEL);
                    gl_TessLevelOuter[2] = float(VGE_TESSELLATION_LEVEL);
                    gl_TessLevelInner[0] = float(VGE_TESSELLATION_LEVEL);
                }
            }
            #endif
            """, $$"""
            #if VGE_ENABLE_TESSELLATION
            {{evaluation}}
            layout(triangles, equal_spacing, ccw) in;
            void main() {
                gl_Position = gl_in[0].gl_Position * gl_TessCoord.x
                    + gl_in[1].gl_Position * gl_TessCoord.y + gl_in[2].gl_Position * gl_TessCoord.z;
                {{interpolations}}
            }
            #endif
            """);
    }
    #endregion
}
