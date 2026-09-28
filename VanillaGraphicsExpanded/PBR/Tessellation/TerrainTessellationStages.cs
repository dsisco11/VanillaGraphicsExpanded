using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR.Tessellation;

/// <summary>Prepares matching tessellation sources from the terrain patch's typed output declarations.</summary>
internal static class TerrainTessellationStages
{
    /// <summary>Immutable stage templates prepared before engine compilation.</summary>
    internal sealed record Sources(string Control, string Evaluation);

    #region Interface preparation
    /// <summary>Retains source preprocessing guards and derives interpolation from declarations, without driver reflection.</summary>
    internal static Sources Generate(string vertexSource, Sources templates, bool depthBias = false)
    {
        var tree = SyntaxTree.Parse(vertexSource, GlslSchema.Instance);
        var control = new StringBuilder();
        var evaluation = new StringBuilder();
        var copies = new StringBuilder();
        var interpolations = new StringBuilder();
        int index = 0;
        var outputNames = new HashSet<string>(StringComparer.Ordinal);
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
            outputNames.Add(name);
            string interpolation = qualifier == "" ? "" : qualifier + " ";
            control.AppendLine($$"""
                #define {{marker}} 1
                {{interpolation}}in {{type}} {{name}}[];
                {{interpolation}}out {{type}} tc_{{name}}[];
                """);
            evaluation.AppendLine($$"""
                #define {{marker}} 1
                #define VGE_TESS_HAS_{{name}} 1
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
        bool shadow = outputNames.Contains("vge_shadowPosition");
        if (shadow)
        {
            const string aliases = """
                #define VGE_TESS_SHADOW 1
                #define worldPos vge_shadowPosition
                #define tc_worldPos tc_vge_shadowPosition

                """;
            control.Insert(0, aliases);
            evaluation.Insert(0, aliases);
        }
        foreach (string required in new[] { shadow ? "vge_shadowPosition" : "worldPos", "normal", "uv", "vge_uvBase", "vge_uvExtent", "renderFlags" })
            if (!outputNames.Contains(required)) throw new NotSupportedException($"Adaptive terrain output missing: {required}.");
        // Reuse the engine's actual cascade weighting, not a second approximation of it.
        var shadowFunction = tree.Select(Query.Syntax<GlFunctionNode>().Named("calcShadowMapCoords"))
            .OfType<GlFunctionNode>().SingleOrDefault();
        if (shadowFunction is not null)
        {
            evaluation.AppendLine("""
                #if SHADOWQUALITY > 0
                uniform float shadowRangeFar;
                uniform mat4 toShadowMapSpaceMatrixFar;
                #endif
                #if SHADOWQUALITY > 1
                uniform float shadowRangeNear;
                uniform mat4 toShadowMapSpaceMatrixNear;
                #endif
                """);
            evaluation.AppendLine(shadowFunction.ToText());
        }
        // Only opaque terrain carries the engine's authored decor depth offset.
        evaluation.AppendLine($"#define VGE_TESS_DEPTH_BIAS {(depthBias ? 1 : 0)}");
        string stageHeader = outputNames.Contains("vge_shadowPosition") ? "#define VGE_TESS_SHADOW 1\n" : "";
        return new Sources(stageHeader + Assemble(templates.Control, control.ToString(), copies.ToString()),
            stageHeader + Assemble(templates.Evaluation, evaluation.ToString(), interpolations.ToString()));
    }

    /// <summary>Inserts only the engine-dependent interface into the asset-owned stage body.</summary>
    private static string Assemble(string template, string declarations, string assignments)
    {
        var tree = SyntaxTree.Parse(template, GlslSchema.Instance);
        var main = Query.Syntax<GlFunctionNode>().Named("main");
        tree.CreateEditor()
            .InsertBefore(main, declarations)
            .InsertAfter(main.InnerStart("body"), "\n" + assignments)
            .Commit();
        // The linker supplies the GLSL version and engine prefix.
        return tree.ToText();
    }
    #endregion
}
