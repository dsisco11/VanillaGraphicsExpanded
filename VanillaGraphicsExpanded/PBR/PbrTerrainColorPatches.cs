using System;
using System.Linq;
using TinyTokenizer.Ast;

namespace VanillaGraphicsExpanded.PBR;

/// <summary>Separates opaque terrain material color from the engine's forward lighting.</summary>
internal static class PbrTerrainColorPatches
{
    #region Material capture

    /// <summary>Preserves vertex alpha and lighting side outputs while removing RGB light modulation.</summary>
    internal static void ApplyVertex(SyntaxTree tree, string sourceName)
    {
        if (sourceName is not ("chunkopaque.vsh" or "chunktopsoil.vsh")) return;
        var editor = tree.CreateEditor();
        ApplyVertex(editor, sourceName);
        editor.Commit();
    }

    /// <summary>Queues vertex material capture edits into a stage-scoped transaction.</summary>
    internal static void ApplyVertex(SyntaxEditor editor, string sourceName)
    {
        if (sourceName is not ("chunkopaque.vsh" or "chunktopsoil.vsh")) return;
        editor.InsertBefore(Query.Syntax<GlFunctionNode>().Named("main"), """

        uniform vec3 vge_atmosphereEnvironment;
        out vec4 vge_environment;

        """)
            .InsertBefore(
            Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"),
            """

                // Deferred lighting consumes unlit material RGB; retain the engine fade alpha.
                rgba.rgb = vec3(1.0);
                vge_environment = vec4(max(rgbaLightIn.rgb, vec3(0.0)) + vge_atmosphereEnvironment * clamp(rgbaLightIn.a, 0.0, 1.0), clamp(rgbaLightIn.a, 0.0, 1.0));

            """);
    }

    /// <summary>Captures color-mapped terrain before forward effects without changing coverage or glow outputs.</summary>
    internal static void ApplyFragment(SyntaxTree tree, string sourceName)
    {
        if (sourceName is not ("chunkopaque.fsh" or "chunktopsoil.fsh")) return;
        var editor = tree.CreateEditor();
        ApplyFragment(tree, editor, sourceName);
        editor.Commit();
    }

    /// <summary>Queues fragment material capture edits into a stage-scoped transaction.</summary>
    internal static void ApplyFragment(SyntaxTree tree, SyntaxEditor editor, string sourceName)
    {
        if (sourceName is not ("chunkopaque.fsh" or "chunktopsoil.fsh")) return;

        // Both installed shaders enter their forward-only effects at this declaration.
        // Fail on a changed engine layout rather than silently publishing lit material color.
        var main = tree.Select(Query.Syntax<GlFunctionNode>().Named("main")).OfType<GlFunctionNode>().Single();
        var children = main.Body.Children.ToArray();
        SyntaxNode? boundary = null;
        for (int index = 0; index + 1 < children.Length; index++)
        {
            if (children[index] is not SyntaxToken { Text: "float" }
                || children[index + 1] is not SyntaxToken { Text: "murkiness" }) continue;
            if (boundary is not null)
                throw new InvalidOperationException($"Ambiguous material capture boundary in {sourceName}.");
            boundary = children[index];
        }
        if (boundary is null)
            throw new InvalidOperationException($"Unsupported material capture boundary in {sourceName}.");
        string color = sourceName == "chunkopaque.fsh" ? "texColor.rgb" : "outColor.rgb";
        // Restore after the complete body, independently of its final output statement.
        editor.InsertBefore(boundary, $"""
            vec3 vge_materialColor = VgeSrgbToLinear({color});

            """)
            .InsertBefore(Query.Syntax<GlFunctionNode>().Named("main").InnerEnd("body"),
                """

                #if NORMALVIEW == 0
                    outColor.rgb = vge_materialColor;
                #endif

                """);
    }

    #endregion
}
